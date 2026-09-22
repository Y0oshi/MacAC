using System.Diagnostics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Play;

public interface ISimAvatarDriverSource
{
    AvatarLocomotionDriver? Controller { get; }
}

public interface ISimAvatarMotionSource
{
    MotionUnpacker? Motion { get; }
}

public enum SimLocomotionStatsApplication
{
    AppliedLive,

    AppliedDormant,

    DroppedNoController,

    DroppedIncompleteSnapshot,

    DroppedDisplacedController,
}

public enum SimServerKineticsStateApplication
{
    AppliedLive,

    DroppedDormantActivationOwned,

    DroppedDisplacedController,
}

public readonly record struct SimLocalLocomotionHoldingCapture(
    bool IsDisposed,
    bool HasController,
    bool HasPreparingMotionOwner,
    bool AutoRunActive,
    bool HasCommandInput,
    long Revision,
    ulong ControllerOwnershipEpoch,
    SimAvatarKineticsPublicationHoldingCapture PhysicsPublication)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed && !HasController && !HasPreparingMotionOwner && !AutoRunActive && !HasCommandInput && PhysicsPublication.IsConverged;
        }
    }
}

public sealed class SimAvatarLocomotionLedger : ISimAvatarDriverSource, ISimAvatarMotionSource, ISimLocomotionLens, IDisposable
{
    // What the player (or a script) wants beyond the raw controller state
    private struct Intent
    {
        public bool AutoExec;
        public bool HasDirective;
        public LocomotionInput Command;
        public bool InterpreterDisabled;

        public readonly bool Any => AutoExec || HasDirective || InterpreterDisabled;
    }

    private AvatarLocomotionDriver? _driver;
    private AvatarLocomotionDriver? _loading;
    private SimAvatarKineticsPublicationLedger? _bulletin;
    private Intent _intent;
    private long _rev;
    private bool _destroyed;
    private Action<string, CanonLogTextType>? _interfacePhrase;

    public IDisposable CommenceLocomotionPrep(AvatarLocomotionDriver driver, Action? emptyPrecedingAnimFifo = null)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(driver);
        if (_loading is not null)
            throw new InvalidOperationException("A local player motion owner is by now being prepared");

        emptyPrecedingAnimFifo?.Invoke();
        _loading = driver;
        return new MotionStaging(this, driver);
    }

    public Action<string, CanonLogTextType>? OnInterfaceText
    {
        get => _interfacePhrase;
        set
        {
            _interfacePhrase = value;
            if (_driver is not null)
                _driver.OnInterfaceWording = value;
        }
    }

    public AvatarLocomotionDriver? Controller
    {
        get => _driver;
        internal set
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!ReferenceEquals(_driver, value))
                Install(value);
        }
    }

    public bool Execute(SimLocomotionDirective directive)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        switch (directive)
        {
            case SimLocomotionDirective.ToggleRunLock:
                _intent.AutoExec = !_intent.AutoExec;
                Touch();
                return true;
            case SimLocomotionDirective.Stop:
                AbortAutoExec();
                WipeDirectiveFeed();
                return true;
            case SimLocomotionDirective.StopCompletely:
                AbortAutoExec();
                WipeDirectiveFeed();
                _ = _driver?.HaltCompletelyAtKineticsObjectBoundary();
                Touch();
                return true;
            case SimLocomotionDirective.FinishJump:
                _driver?.CompleteLeap();
                Touch();
                return true;
            case SimLocomotionDirective.Ready:
            case SimLocomotionDirective.Sit:
            case SimLocomotionDirective.Crouch:
            case SimLocomotionDirective.Sleep:
                AbortAutoExec();
                WipeDirectiveFeed();
                uint posture = directive switch
                {
                    SimLocomotionDirective.Ready => LocomotionDirective.Ready,
                    SimLocomotionDirective.Sit => LocomotionDirective.Sitting,
                    SimLocomotionDirective.Crouch => LocomotionDirective.Crouch,
                    SimLocomotionDirective.Sleep => LocomotionDirective.Sleeping,
                    _ => throw new UnreachableException(),
                };
                return _driver?.ReqPosture(posture) == true;
            default:
                return false;
        }
    }

    public bool AutoRunActive => _intent.AutoExec;
    public bool HasDirectiveFeed => _intent.HasDirective;
    public LocomotionInput DirectiveFeed => _intent.Command;
    public bool DirectiveInterpreterDisabled => _intent.InterpreterDisabled;
    public long Revision => Interlocked.Read(ref _rev);
    public ulong DriverOwnershipEpoch { get; private set; }
    public ISimLocomotionLens View => this;
    public bool IsStandingStill => _driver?.IsStandingStill ?? true;
    public JumpChargeCapture LeapCharge => _driver?.JumpCharge ?? default;

    public Func<bool>? ExecAsDefaultTravelSrc { get; set; }

    public bool RunAsDefaultMovement => ExecAsDefaultTravelSrc?.Invoke() ?? true;

    internal SimAvatarKineticsPublicationLedger KineticsBulletin
    {
        get
        {
            return _bulletin ?? throw new InvalidOperationException("The Runtime local-player physics publication owner isn't bound");
        }
    }

    MotionUnpacker? ISimAvatarMotionSource.Motion => _loading?.Locomotion ?? _driver?.Locomotion;

    public SimLocomotionCapture Snapshot
    {
        get
        {
            return _driver is { } driver
            ? new SimLocomotionCapture(true, driver.OwnEntityId, driver.LatestChamberLocus, driver.CorpusVel, driver.IsAirborne, driver.SimMomentSecs,
                Revision, _intent.AutoExec, _intent.HasDirective, _intent.Command)
            : new SimLocomotionCapture(false, 0u, default, default, false, 0d, Revision, _intent.AutoExec, _intent.HasDirective, _intent.Command);
        }
    }

    public bool RunLocomotion(uint locomotionDirective)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        return _driver?.ReqDirectiveLocomotion(locomotionDirective) == true;
    }

    public bool PivotToHeading(float bearingDeg, bool enactExecGripTag = false)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!float.IsFinite(bearingDeg))
            return false;
        float bearing = bearingDeg % 360f;
        if (bearing < 0f)
            bearing += 360f;
        return _driver?.ReqPivotToBearing(bearing, enactExecGripTag) == true;
    }

    private sealed class MotionStaging(SimAvatarLocomotionLedger holder, AvatarLocomotionDriver driver) : IDisposable
    {
        private SimAvatarLocomotionLedger? _holder = holder;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.DisposeRest(driver);
    }

    public bool AbortAutoExec()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_intent.AutoExec)
            return false;
        _intent.AutoExec = false;
        Touch();
        return true;
    }

    public void AssignDirectiveFeed(in LocomotionInput feed)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_intent.HasDirective && _intent.Command == feed)
            return;
        _intent.Command = feed;
        _intent.HasDirective = true;
        Touch();
    }

    public bool WipeDirectiveFeed()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_intent.HasDirective)
            return false;
        _intent.Command = default;
        _intent.HasDirective = false;
        Touch();
        return true;
    }

    public SimLocomotionStatsApplication ImposeToonTravelStats(SimLocomotionSkillLedger aptitudes)
    {
        ArgumentNullException.ThrowIfNull(aptitudes);
        if (_driver is not { } driver)
            return SimLocomotionStatsApplication.DroppedNoController;
        var capture = aptitudes.Snapshot;
        return capture.IsComplete ? driver.ImposeToonTravelStats(capture) : SimLocomotionStatsApplication.DroppedIncompleteSnapshot;
    }

    public bool RelayExhaustion() => _driver?.AnnounceExhaustionAtTravelBoundary() == true;

    public bool IsPrimedForAssault(FightingManner manner)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_driver is not { } driver)
            return false;
        DecodedMotionState locomotion = driver.Locomotion.InterpretedPhase;
        return FightInputPlanner.AvatarInPrimedLocusForAssault(manner, locomotion.LatestStyle, locomotion.ForwardCommand);
    }

    public bool StageForAssaultReq()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        AbortAutoExec();
        return _driver?.ReadyForAssaultReq() == true;
    }

    public void DeactivateDirectiveInterpreter()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (_intent.InterpreterDisabled)
            return;
        _intent = new Intent { InterpreterDisabled = true };
        Touch();
    }

    public void RestartFeedIntent()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_intent.Any)
            return;
        _intent = default;
        Touch();
    }

    public bool IsDualWield
    {
        get
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            return _driver?.Locomotion.InterpretedPhase.LatestStyle == FightInputPlanner.DualWieldFightingStyling;
        }
    }

    public void ResetSession()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _bulletin?.RestartSess();
        bool altered = _intent.Any || _driver is not null || _loading is not null;
        _intent = default;
        Retire();
        _loading = null;
        if (altered)
            Touch();
    }

    public SimLocalLocomotionHoldingCapture CaptureOwnership()
    {
        return new(
        _destroyed,
        _driver is not null,
        _loading is not null,
        _intent.AutoExec,
        _intent.HasDirective,
        Revision,
        DriverOwnershipEpoch,
        _bulletin?.GrabOwnership() ?? default);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _intent.AutoExec = false;
        _intent.HasDirective = false;
        _intent.Command = default;
        _bulletin?.Dispose();
        Retire();
        _loading = null;
        Touch();
        _destroyed = true;
    }

    internal void FastenKineticsBulletin(SimAvatarKineticsPublicationLedger bulletin)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(bulletin);
        if (_bulletin is not null)
            throw new InvalidOperationException("The Runtime local-player physics publication owner is by now bound");
        _bulletin = bulletin;
    }

    internal bool CanSealCorePossessedDriver(ulong anticipatedEpoch, AvatarLocomotionDriver? anticipatedDriver)
    {
        return !_destroyed && DriverOwnershipEpoch == anticipatedEpoch && ReferenceEquals(_driver, anticipatedDriver);
    }

    internal void SealCorePossessedDriver(AvatarLocomotionDriver driver) => Install(driver);

    private void Touch() => Interlocked.Increment(ref _rev);

    // Retires the current controller (if any) and takes ownership of the new one
    private void Install(AvatarLocomotionDriver? upcoming)
    {
        _driver?.RetireCoreBulletin();
        _driver = upcoming;
        if (upcoming is not null)
            upcoming.OnInterfaceWording = _interfacePhrase;
        ++DriverOwnershipEpoch;
        Touch();
    }

    private void DisposeRest(AvatarLocomotionDriver driver)
    {
        if (_destroyed && _loading is null)
            return;
        if (!ReferenceEquals(_loading, driver))
            throw new InvalidOperationException("The local player motion preparation owner changed unexpectedly");
        _loading = null;
    }

    // Drops the controller without touching the revision; callers decide whether that counts as a
    // change
    private void Retire()
    {
        if (_driver is null)
            return;
        _driver.RetireCoreBulletin();
        _driver = null;
        ++DriverOwnershipEpoch;
    }
}
