using System.Diagnostics;

namespace MacAC.Client.Paging;

/// <summary>Immutable, validated per-frame streaming work profile.</summary>
public readonly record struct PagingWorkAllowance
{
    public PagingWorkAllowance(
        TimeSpan maxUpdateTime,
        int maxCompletionAdmissions,
        long maxAdoptedCpuBytes,
        int maxEntityOperations,
        long maxGpuUploadBytes,
        int maxGlRetireOperations,
        float destinationReserveFraction)
    {
        if (maxUpdateTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxUpdateTime));
        if (maxCompletionAdmissions <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCompletionAdmissions));
        if (maxAdoptedCpuBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAdoptedCpuBytes));
        if (maxEntityOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntityOperations));
        if (maxGpuUploadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxGpuUploadBytes));
        if (maxGlRetireOperations <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxGlRetireOperations));
        if (!float.IsFinite(destinationReserveFraction)
            || destinationReserveFraction <= 0f
            || destinationReserveFraction >= 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationReserveFraction));
        }

        UpperRefreshMoment = maxUpdateTime;
        UpperWrapUpAdmissions = maxCompletionAdmissions;
        UpperAdoptedCpuOctets = maxAdoptedCpuBytes;
        UpperActorOps = maxEntityOperations;
        UpperGpuPushOctets = maxGpuUploadBytes;
        UpperGlRetireOps = maxGlRetireOperations;
        DestAllocateRatio = destinationReserveFraction;
    }

    public TimeSpan UpperRefreshMoment { get; }
    public int UpperWrapUpAdmissions { get; }
    public long UpperAdoptedCpuOctets { get; }
    public int UpperActorOps { get; }
    public long UpperGpuPushOctets { get; }
    public int UpperGlRetireOps { get; }
    public float DestAllocateRatio { get; }

    internal void Validate()
    {
        if (UpperRefreshMoment <= TimeSpan.Zero
            || UpperWrapUpAdmissions <= 0
            || UpperAdoptedCpuOctets <= 0
            || UpperActorOps <= 0
            || UpperGpuPushOctets <= 0
            || UpperGlRetireOps <= 0
            || !float.IsFinite(DestAllocateRatio)
            || DestAllocateRatio <= 0f
            || DestAllocateRatio >= 1f)
        {
            throw new ArgumentException(
                "Streaming work budget isn't a valid complete profile",
                nameof(PagingWorkAllowance));
        }
    }

    public PagingWorkAllowance WidenForDestGrip(
        double ceilingMillis)
    {
        if (!double.IsFinite(ceilingMillis) || ceilingMillis <= 0)
            return this;

        double scaling =
            ceilingMillis / UpperRefreshMoment.TotalMilliseconds;
        if (scaling <= 1.0)
            return this;

        float widenedAllocate = (float)(
            1.0 - (1.0 - DestAllocateRatio) / scaling);
        if (widenedAllocate >= 1f)
            widenedAllocate = MathF.BitDecrement(1f);

        return new PagingWorkAllowance(
            TimeSpan.FromMilliseconds(ceilingMillis),
            PagingWorkAllowanceKnobs.Scale(UpperWrapUpAdmissions, scaling),
            PagingWorkAllowanceKnobs.Scale(UpperAdoptedCpuOctets, scaling),
            PagingWorkAllowanceKnobs.Scale(UpperActorOps, scaling),
            PagingWorkAllowanceKnobs.Scale(UpperGpuPushOctets, scaling),
            PagingWorkAllowanceKnobs.Scale(UpperGlRetireOps, scaling),
            widenedAllocate);
    }
}

public readonly record struct PagingWorkCost(
    int CompletionAdmissions = 0,
    long AdoptedCpuBytes = 0,
    int EntityOperations = 0,
    long GpuUploadBytes = 0,
    int GlRetireOperations = 0)
{
    public bool IsZero
    {
        get
        {
            return CompletionAdmissions is 0
        && AdoptedCpuBytes is 0
        && EntityOperations is 0
        && GpuUploadBytes is 0
        && GlRetireOperations is 0;
        }
    }

    internal void Validate()
    {
        if (CompletionAdmissions < 0)
            throw new ArgumentOutOfRangeException(nameof(CompletionAdmissions));
        if (AdoptedCpuBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(AdoptedCpuBytes));
        if (EntityOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(EntityOperations));
        if (GpuUploadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(GpuUploadBytes));
        if (GlRetireOperations < 0)
            throw new ArgumentOutOfRangeException(nameof(GlRetireOperations));
    }

    internal PagingWorkCost Add(PagingWorkCost another)
    {
        return new(
        SaturatingAppend(CompletionAdmissions, another.CompletionAdmissions),
        SaturatingAppend(AdoptedCpuBytes, another.AdoptedCpuBytes),
        SaturatingAppend(EntityOperations, another.EntityOperations),
        SaturatingAppend(GpuUploadBytes, another.GpuUploadBytes),
        SaturatingAppend(GlRetireOperations, another.GlRetireOperations));
    }

    private static int SaturatingAppend(int left, int right) =>
        left > int.MaxValue - right ? int.MaxValue : left + right;

    private static long SaturatingAppend(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;
}

public enum PagingWorkAdmission : byte
{
    Admitted,
    OversizedProgress,
    Yielded,
}

public enum PagingWorkLimit : byte
{
    None,
    Time,
    CompletionAdmissions,
    AdoptedCpuBytes,
    EntityOperations,
    GpuUploadBytes,
    GlRetireOperations,
}

internal enum PagingWorkLane : byte
{
    NonDestination,
    Destination,
}

/// <summary>Immutable observation of one frame's streaming work.</summary>
public readonly record struct PagingWorkMeterCapture(
    PagingWorkCost Used,
    PagingWorkCost DestinationUsed,
    PagingWorkCost NonDestinationUsed,
    int Operations,
    int CompletedOperations,
    int YieldCount,
    int OverrunCount,
    int OversizedProgressCount,
    int FailureCount,
    double ElapsedMilliseconds,
    double MaximumOperationMilliseconds,
    string? MaximumOperationStage,
    string? LastStage,
    PagingWorkLimit LastLimit);

/// <summary>Read-only streaming scheduler facts published to lifecycle artifacts.</summary>
public readonly record struct PagingWorkTelemetry(
    PagingWorkMeterCapture LastFrame,
    long LifetimeFrameOverrunCount,
    long LifetimeOversizedProgressCount,
    double MaximumFrameMilliseconds,
    string? MaximumFrameStage,
    double MaximumOperationMilliseconds,
    string? MaximumOperationStage,
    int DeferredCompletions,
    long DeferredAdoptedCpuBytes,
    double OldestDeferredAgeMilliseconds,
    int PendingPublications,
    int PendingRetirements,
    int WorkerCompletionBacklog,
    int DestinationBacklog,
    int ControlBacklog,
    int UnloadBacklog,
    int NearBacklog,
    int FarBacklog);

public sealed partial class PagingWorkMeter
{
    private readonly PagingWorkAllowance _allowance;

    private readonly Func<long> _stamp;

    private readonly long _frequency;

    private readonly long _begin;

    private readonly bool _destReservationEngaged;

    private PagingWorkCost _consumed;

    private PagingWorkCost _destConsumed;

    private PagingWorkCost _nonDestConsumed;

    private PagingWorkLane _lane;

    private long _destPassedBeats;

    private long _nonDestPassedBeats;

    private long _engagedOpBegin;

    private double _ceilingOpMillis;

    private string? _ceilingOpJuncture;

    private int _ops;

    private int _completed;

    private int _yields;

    private int _overruns;

    private int _oversizedHeadway;

    private int _misses;

    private bool _cycleOverrunRecorded;

    private bool _reservationEngaged;

    private bool _ensuredHeadwayGranted;

    private string? _engagedJuncture;

    private string? _previousJuncture;

    private PagingWorkLimit _previousThreshold;

    public PagingWorkMeter(
        PagingWorkAllowance allowance,
        bool destReservationEngaged = false)
        : this(
            allowance,
            Stopwatch.GetTimestamp,
            Stopwatch.Frequency,
            destReservationEngaged)
    {
    }

    public PagingWorkMeter(
        PagingWorkAllowance allowance,
        Func<long> stamp,
        long timestampFrequency,
        bool destReservationEngaged = false)
    {
        ArgumentNullException.ThrowIfNull(stamp);
        if (timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
        allowance.Validate();

        _allowance = allowance;
        _stamp = stamp;
        _frequency = timestampFrequency;
        _begin = stamp();
        _destReservationEngaged = destReservationEngaged;
    }

    internal readonly struct LaneAmbit : IDisposable
    {
        private readonly PagingWorkMeter _gauge;
        private readonly PagingWorkLane _earlier;

        internal LaneAmbit(
            PagingWorkMeter gauge,
            PagingWorkLane earlier)
        {
            _gauge = gauge;
            _earlier = earlier;
        }

        public void Dispose() => _gauge.ReinstateLane(_earlier);
    }
}
