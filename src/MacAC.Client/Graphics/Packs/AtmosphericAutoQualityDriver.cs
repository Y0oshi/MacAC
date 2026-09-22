namespace MacAC.Client.Graphics.Packs;

using MacAC.Extensibility.RenderPacks;

internal enum AtmosphericFidelityTier : byte
{
    Low,
    Medium,
    High,
}

internal readonly record struct AtmosphericFidelityReading(
    double InclusivePackGpuMillisecondsP99,
    double IncrementalCpuMillisecondsP99,
    long ResidentGpuBytes,
    bool StableFrameBoundary);

internal readonly record struct AtmosphericAutoQualityCapture(
    AtmosphericFidelityTier Current,
    int ConsecutiveOverBudgetFrames,
    int ConsecutiveHeadroomFrames,
    int CooldownFramesRemaining,
    long ChangeGeneration,
    bool SafeFallbackToRetailRequested);

internal readonly record struct AtmosphericQualityAllowance(
    double GpuMillisecondsP99,
    double CpuMillisecondsP99,
    long ResidentGpuBytes)
{
    internal static AtmosphericQualityAllowance FromPreset(QualityLadderStep preset)
    {
        return new(
        preset.MaxIncrementalGpuMillisecondsP99,
        preset.MaxIncrementalCpuMillisecondsP99,
        preset.MaxResidentGpuBytes);
    }
}

internal sealed class AtmosphericAutoQualityDriver
{
    internal const int DowngradeHysteresisCycles = 180;
    internal const int UpgradeHysteresisCycles = 900;
    internal const int EditCooldownCycles = 300;

    private AtmosphericFidelityTier _latest;
    private readonly AtmosphericFidelityTier _floor;
    private readonly AtmosphericFidelityTier _ceiling;
    private readonly AtmosphericQualityAllowance[] _budgets;
    private int _overAllowance;
    private int _headroom;
    private int _cooldown;
    private long _gen;
    private bool _safeBackupToCanonAsked;

    internal AtmosphericAutoQualityDriver(
        AtmosphericFidelityTier starting = AtmosphericFidelityTier.Medium,
        AtmosphericFidelityTier floor = AtmosphericFidelityTier.Low,
        AtmosphericFidelityTier ceiling = AtmosphericFidelityTier.High)
        : this(DefaultBudgets(), starting, floor, ceiling)
    {
    }

    internal AtmosphericAutoQualityDriver(
        IReadOnlyList<AtmosphericQualityAllowance> budgets,
        AtmosphericFidelityTier initial = AtmosphericFidelityTier.Medium,
        AtmosphericFidelityTier floor = AtmosphericFidelityTier.Low,
        AtmosphericFidelityTier ceiling = AtmosphericFidelityTier.High)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        if (budgets.Count is not 3)
            throw new ArgumentException("Auto quality needs Low, Medium, and High budgets", nameof(budgets));
        if (floor > initial || initial > ceiling)
            throw new ArgumentOutOfRangeException(nameof(initial));
        _budgets = [.. budgets];
        foreach (AtmosphericQualityAllowance allowance in _budgets)
        {
            if (!double.IsFinite(allowance.GpuMillisecondsP99)
                || allowance.GpuMillisecondsP99 < 0d
                || !double.IsFinite(allowance.CpuMillisecondsP99)
                || allowance.CpuMillisecondsP99 < 0d
                || allowance.ResidentGpuBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(budgets),
                    "Automatic-quality budgets has to be finite and non-negative");
            }
        }
        _floor = floor;
        _ceiling = ceiling;
        _latest = initial;
    }

    internal AtmosphericAutoQualityCapture Capture
    {
        get
        {
            return new(
        _latest,
        _overAllowance,
        _headroom,
        _cooldown,
        _gen,
        _safeBackupToCanonAsked);
        }
    }

    internal AtmosphericQualityAllowance LatestAllowance => _budgets[(int)_latest];

    internal AtmosphericAutoQualityCapture Observe(
        in AtmosphericFidelityReading measurement)
    {
        Validate(in measurement);
        if (!measurement.StableFrameBoundary)
            return Capture;
        if (_safeBackupToCanonAsked)
            return Capture;
        if (_cooldown > 0)
        {
            --_cooldown;
            _overAllowance = 0;
            _headroom = 0;
            return Capture;
        }

        var allowance = _budgets[(int)_latest];
        bool over = measurement.InclusivePackGpuMillisecondsP99
                > allowance.GpuMillisecondsP99
            || measurement.IncrementalCpuMillisecondsP99
                > allowance.CpuMillisecondsP99
            || measurement.ResidentGpuBytes > allowance.ResidentGpuBytes;
        if (over)
        {
            ++_overAllowance;
            _headroom = 0;
            if (_overAllowance >= DowngradeHysteresisCycles)
            {
                if (_latest != _floor)
                    Alter((AtmosphericFidelityTier)((int)_latest - 1));
                else
                    ReqSafeBackup();
            }
            return Capture;
        }

        _overAllowance = 0;
        if (_latest == _ceiling)
        {
            _headroom = 0;
            return Capture;
        }

        AtmosphericFidelityTier upcoming =
            (AtmosphericFidelityTier)((int)_latest + 1);
        var upcomingAllowance = _budgets[(int)upcoming];
        bool hasHeadroom = measurement.InclusivePackGpuMillisecondsP99
                <= upcomingAllowance.GpuMillisecondsP99 * 0.70
            && measurement.IncrementalCpuMillisecondsP99
                <= upcomingAllowance.CpuMillisecondsP99 * 0.70
            && measurement.ResidentGpuBytes
                <= (long)(upcomingAllowance.ResidentGpuBytes * 0.70);
        if (!hasHeadroom)
        {
            _headroom = 0;
            return Capture;
        }

        ++_headroom;
        if (_headroom >= UpgradeHysteresisCycles)
            Alter(upcoming);
        return Capture;
    }

    internal void Reset(AtmosphericFidelityTier tier)
    {
        _latest = tier;
        _overAllowance = 0;
        _headroom = 0;
        _cooldown = 0;
        _safeBackupToCanonAsked = false;
        _gen = checked(_gen + 1);
    }

    private void Alter(AtmosphericFidelityTier val)
    {
        _latest = val;
        _overAllowance = 0;
        _headroom = 0;
        _cooldown = EditCooldownCycles;
        _gen = checked(_gen + 1);
    }

    private void ReqSafeBackup()
    {
        _overAllowance = DowngradeHysteresisCycles;
        _headroom = 0;
        _cooldown = 0;
        _safeBackupToCanonAsked = true;
        _gen = checked(_gen + 1);
    }

    private static AtmosphericQualityAllowance[] DefaultBudgets()
    {
        return [
        From(DirectionalShadePreset.Low),
        From(DirectionalShadePreset.Medium),
        From(DirectionalShadePreset.High),
    ];
    }

    private static AtmosphericQualityAllowance From(DirectionalShadePreset preset)
    {
        DirectionalShadeQuality fidelity = DirectionalShadeQuality.For(preset);
        return new AtmosphericQualityAllowance(
            fidelity.IncrementalGpuP99BudgetMilliseconds,
            fidelity.IncrementalCpuP99BudgetMilliseconds,
            fidelity.PackResidentGpuByteBudget);
    }

    private static void Validate(in AtmosphericFidelityReading value)
    {
        if (!double.IsFinite(value.InclusivePackGpuMillisecondsP99)
            || value.InclusivePackGpuMillisecondsP99 < 0
            || !double.IsFinite(value.IncrementalCpuMillisecondsP99)
            || value.IncrementalCpuMillisecondsP99 < 0
            || value.ResidentGpuBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Atmospheric quality measurements has to be finite and non-negative");
        }
    }
}
