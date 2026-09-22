using System.Diagnostics;
using MacAC.Mechanics.Fighting;

namespace MacAC.Sim.Play;

public enum SimInputArming
{
    Press,
    Release,
}

public enum SimFightingAttackDirective
{
    LowAttack,
    MediumAttack,
    HighAttack,
    DecreasePower,
    IncreasePower,
    AbortForMovement,
}

public readonly record struct SimFightingAttackInput(SimFightingAttackDirective Command, SimInputArming Activation);

public interface ISimFightingAttackOps
{
    bool IsDualWield { get; }

    bool AvatarPrimedForAssault { get; }

    bool AutoRepeatAttack { get; }

    bool CanBeginAssault();

    void ReadyAssaultReq();

    bool TransmitAssault(AssaultElevation height, float strength);

    void TransmitAbortAssault();
}

// Callback-backed operations for hosts and tests that do not want a class
internal sealed class DelegateSimFightingAttackOps(
    Func<bool> canStartAttack,
    Func<AssaultElevation, float, bool> sendAttack,
    Action? readyAssaultReq,
    Action? transmitAbortAssault,
    Func<bool>? isDualWield,
    Func<bool>? avatarPrimedForAssault,
    Func<bool>? autoRepeatAssault) : ISimFightingAttackOps
{
    private readonly Func<bool> _canBegin = canStartAttack ?? throw new ArgumentNullException(nameof(canStartAttack));
    private readonly Func<AssaultElevation, float, bool> _transmit = sendAttack ?? throw new ArgumentNullException(nameof(sendAttack));
    private readonly Action _prepare = readyAssaultReq ?? (static () => { });
    private readonly Action _abort = transmitAbortAssault ?? (static () => { });
    private readonly Func<bool> _dualWield = isDualWield ?? (static () => false);
    private readonly Func<bool> _primed = avatarPrimedForAssault ?? (static () => true);
    private readonly Func<bool> _autoRepeat = autoRepeatAssault ?? (static () => false);

    public bool IsDualWield => _dualWield();
    public bool AvatarPrimedForAssault => _primed();
    public bool AutoRepeatAttack => _autoRepeat();
    public bool CanBeginAssault() => _canBegin();
    public void ReadyAssaultReq() => _prepare();
    public bool TransmitAssault(AssaultElevation height, float strength) => _transmit(height, strength);
    public void TransmitAbortAssault() => _abort();
}

public sealed class SimFightingAttackLedger : IDisposable
{
    public const double AssaultPowerUpSeconds = FightInputPlanner.AssaultStrengthUpSecs;
    public const double DualWieldStrengthUpSeconds = FightInputPlanner.DualWieldStrengthUpSecs;
    public const float StartingWantedStrength = 0.5f;
    public const float WantedStrengthHop = 1f / 6f;

    private readonly FightingPhase _fighting;
    private readonly ISimFightingAttackOps _ops;
    private readonly Func<double> _instant;

    // The power bar
    private bool _structure;
    private double _assembleBegunAt;
    private float _currentTier;
    private bool _assembleIsAutomatic;

    // The request in flight
    private bool _tagPinned;
    private bool _expectingSrv;
    private bool _repeating;
    private float _askedStrength;

    // A release that arrived while the server was still answering.
    private bool _queuedFree;
    private float _queuedFreeStrength;

    private bool _destroyed;
    private long _wrapUpRev;

    public SimFightingAttackLedger(
        FightingPhase fighting,
        Func<bool> canBeginAssault,
        Func<AssaultElevation, float, bool> transmitAssault,
        Action? readyAssaultReq = null,
        Action? transmitAbortAssault = null,
        Func<bool>? isDualWield = null,
        Func<bool>? avatarPrimedForAssault = null,
        Func<bool>? autoRepeatAssault = null,
        Func<double>? instant = null)
        : this(
            fighting,
            new DelegateSimFightingAttackOps(canBeginAssault, transmitAssault, readyAssaultReq, transmitAbortAssault, isDualWield, avatarPrimedForAssault, autoRepeatAssault),
            instant)
    {
    }

    public SimFightingAttackLedger(FightingPhase combat, ISimFightingAttackOps operations, Func<double>? instant = null)
    {
        _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
        _ops = operations ?? throw new ArgumentNullException(nameof(operations));
        _instant = instant ?? (static () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

        _fighting.CombatModeChanged += OnFightingMannerAltered;
        _fighting.AttackCommenced += OnAssaultCommenced;
        _fighting.AttackDone += OnAssaultDone;
    }

    public AssaultElevation AskedHeight { get; private set; } = AssaultElevation.Medium;
    public float WantedStrength { get; private set; } = StartingWantedStrength;
    public bool AssaultReqInHeadway => _tagPinned;
    public bool AssaultSrvResponseQueued => _expectingSrv;
    public bool RepeatAssaultInHeadway => _repeating;
    public float AskedAssaultStrength => _askedStrength;
    public bool AssembleInHeadway => _structure;
    public bool IsDisposed => _destroyed;
    public long CompletionRev => _wrapUpRev;
    public uint CompletionSeries { get; private set; }
    public uint CompletionWeenieProblem { get; private set; }

    public float StrengthBarTier => _structure ? Level() : _currentTier;

    public event Action? StateChanged;

    public bool ServiceDirective(in SimFightingAttackInput directive)
    {
        AssaultElevation? height = directive.Command switch
        {
            SimFightingAttackDirective.LowAttack => AssaultElevation.Low,
            SimFightingAttackDirective.MediumAttack => AssaultElevation.Medium,
            SimFightingAttackDirective.HighAttack => AssaultElevation.High,
            _ => null,
        };

        if (height is { } h)
        {
            if (directive.Activation == SimInputArming.Press)
                PressAssault(h);
            else if (directive.Activation == SimInputArming.Release)
                FreeAssault();
            return true;
        }

        if (directive.Activation != SimInputArming.Press)
            return false;

        switch (directive.Command)
        {
            case SimFightingAttackDirective.DecreasePower:
                TickWantedStrength(-1);
                return true;
            case SimFightingAttackDirective.IncreasePower:
                TickWantedStrength(+1);
                return true;
            case SimFightingAttackDirective.AbortForMovement:
                CancelAutomaticAttack();
                return true;
            default:
                return false;
        }
    }

    public void AssignWantedStrength(float strength)
    {
        float clamped = Math.Clamp(strength, 0f, 1f);
        if (Math.Abs(WantedStrength - clamped) < float.Epsilon)
            return;
        WantedStrength = clamped;
        Alert();
    }

    public void PressAssault(AssaultElevation height)
    {
        bool newHeight = AskedHeight != height;
        AskedHeight = height;
        if (newHeight || !_tagPinned)
            CommenceReq();
        Alert();
    }

    public void FreeAssault()
    {
        if (!_tagPinned)
            return;

        _tagPinned = false;
        float tier = Level();
        _askedStrength = Math.Max(WantedStrength, tier);

        if (_expectingSrv)
        {
            _queuedFree = true;
            _queuedFreeStrength = _askedStrength;
        }
        else if (WantedStrength <= tier || _repeating)
        {
            Fire(setSrvQueued: true);
        }

        Alert();
    }

    public void CancelAutomaticAttack()
    {
        if (!_expectingSrv && !_tagPinned && !_repeating)
            return;

        _ops.TransmitAbortAssault();
        _repeating = false;
        if (_structure)
            RestartBar();
        Alert();
    }

    public void Tick()
    {
        if (_tagPinned && !_structure && !_expectingSrv)
            TryBeginAssemble();

        if (!_structure)
            return;

        if (!_ops.AvatarPrimedForAssault)
        {
            if (_tagPinned)
            {
                HaltAssemble();
                _currentTier = 0f;
            }
            else
            {
                _repeating = false;
                RestartBar();
            }
            Alert();
            return;
        }

        float tier = Level();
        _currentTier = tier;
        if (!_tagPinned && tier >= _askedStrength)
        {
            _currentTier = Math.Min(_askedStrength, tier);
            if (_assembleIsAutomatic)
                HaltAssemble();
            else
                Fire(setSrvQueued: true);
        }
        Alert();
    }

    public void ResetSession()
    {
        Drop();
        AskedHeight = AssaultElevation.Medium;
        WantedStrength = StartingWantedStrength;
        Alert();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _fighting.CombatModeChanged -= OnFightingMannerAltered;
        _fighting.AttackCommenced -= OnAssaultCommenced;
        _fighting.AttackDone -= OnAssaultDone;
    }

    private void Alert() => StateChanged?.Invoke();

    private void TickWantedStrength(int dir)
    {
        int sixths = (int)MathF.Round(WantedStrength / WantedStrengthHop);
        AssignWantedStrength((sixths + Math.Sign(dir)) * WantedStrengthHop);
    }

    private void CommenceReq()
    {
        if (!FightInputPlanner.SupportsTargetedAssault(_fighting.LatestMode) || !_ops.CanBeginAssault())
            return;

        _tagPinned = true;
        _askedStrength = 1f;
        _ops.ReadyAssaultReq();
        _assembleIsAutomatic = false;
        TryBeginAssemble();
    }

    private void Fire(bool setSrvQueued)
    {
        HaltAssemble();
        if (!_ops.TransmitAssault(AskedHeight, Math.Clamp(_askedStrength, 0f, 1f)))
        {
            RestartBar();
            return;
        }
        if (_ops.AutoRepeatAttack)
            _repeating = true;
        _expectingSrv = setSrvQueued;
    }

    private void TryBeginAssemble()
    {
        if (_structure || _expectingSrv || !_ops.AvatarPrimedForAssault)
            return;
        BeginAssemble();
    }

    private void BeginAssemble()
    {
        _structure = true;
        _assembleBegunAt = _instant();
        _currentTier = 0f;
    }

    private float Level()
    {
        if (!_structure)
            return 0f;
        double pane = _ops.IsDualWield ? DualWieldStrengthUpSeconds : AssaultPowerUpSeconds;
        return (float)Math.Clamp((_instant() - _assembleBegunAt) / pane, 0d, 1d);
    }

    private void HaltAssemble(bool preserveTier = false)
    {
        if (!preserveTier && _structure)
            _currentTier = Level();
        _structure = false;
        _assembleBegunAt = 0d;
    }

    private void RestartBar()
    {
        HaltAssemble();
        _currentTier = 0f;
        _assembleIsAutomatic = false;
    }

    private void OnAssaultCommenced()
    {
        _expectingSrv = true;
        if (!_tagPinned)
        {
            _currentTier = _askedStrength;
            HaltAssemble(preserveTier: true);
        }
        Alert();
    }

    private void OnAssaultDone(uint assaultSeries, uint weenieProblem)
    {
        CompletionSeries = assaultSeries;
        CompletionWeenieProblem = weenieProblem;
        ++_wrapUpRev;
        _expectingSrv = false;
        if (weenieProblem is not 0)
            _repeating = false;

        if (!_tagPinned && _ops.AutoRepeatAttack && _repeating)
        {
            if (Math.Abs(_askedStrength - WantedStrength) >= 0.01f)
                _askedStrength = WantedStrength;
            Fire(setSrvQueued: false);
        }

        if (!_ops.AutoRepeatAttack || !_repeating)
        {
            _repeating = false;
            RestartBar();
        }
        else if (_tagPinned)
        {
            TryBeginAssemble();
        }
        else
        {
            BeginAssemble();
            _assembleIsAutomatic = true;
        }

        if (_queuedFree)
        {
            float strength = _queuedFreeStrength;
            _queuedFree = false;
            _queuedFreeStrength = 0f;
            CommenceReq();
            if (_tagPinned)
            {
                _askedStrength = strength;
                _tagPinned = false;
                Fire(setSrvQueued: true);
            }
        }

        Alert();
    }

    private void OnFightingMannerAltered(FightingManner manner)
    {
        if (!FightInputPlanner.SupportsTargetedAssault(manner))
            Drop();
        Alert();
    }

    private void Drop()
    {
        _tagPinned = false;
        _expectingSrv = false;
        _queuedFree = false;
        _queuedFreeStrength = 0f;
        _repeating = false;
        _askedStrength = 0f;
        _wrapUpRev = 0;
        CompletionSeries = 0u;
        CompletionWeenieProblem = 0u;
        RestartBar();
    }
}
