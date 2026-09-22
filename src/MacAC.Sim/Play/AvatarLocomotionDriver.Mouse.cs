using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

public sealed partial class AvatarLocomotionDriver
{

    public bool CommencePointerGaze(LocomotionInput feed)
    {
        DemandPublished();
        if (_pointerLooking || State != AvatarPhase.InWorld)
            return false;

        _pointerLooking = true;
        GrabControl(feed);
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;
        FlipPointerGazeTravel(feed, entering: true);
        return true;
    }

    private bool _pointerLooking;

    private bool _pointerPivotQueued;

    private float _pointerPivotDiff;

    private uint? _onlinePivotCmd;

    private float _onlinePivotPace;

    private bool _onlinePivotFromPointer;

    private uint? _onlineSidestepCmd;

    private bool _onlineSidestepExecGrip;

    private bool _pointerRelocateSignalLoaded;

    private bool _pointerRelocateSignalQueued;

    private float _pointerRelocateSignalAt;

    private bool _srvDriven = true;

    public const float PointerTravelSignalInterval = 0.5f;

    private const float PointerPivotDeadZone = 0.02f;

    private const float PointerPivotPaceScaling = 2.0f;

    private const float PointerPivotCeilingPace = 1.5f;

    public void SubmitPointerPivotAdjustment(
        float signedAdjustment,
        LocomotionInput feed)
    {
        DemandPublished();
        if (!_pointerLooking || !float.IsFinite(signedAdjustment))
            return;

        GrabControl(feed);
        _pointerPivotDiff = signedAdjustment;
        _pointerPivotQueued = true;
        _pointerRelocateSignalLoaded = true;
    }

    public void StopMouseDrift(LocomotionInput feed)
    {
        DemandPublished();
        if (!_pointerLooking)
            return;

        GrabControl(feed);

        LocomotionParams p = PointerAxis(_onlinePivotPace == 0f
            ? 1f
            : _onlinePivotPace);
        HaltLocomotionAtBoundary(LocomotionDirective.TurnRight, p);
        HaltLocomotionAtBoundary(LocomotionDirective.PivotLeft, p);
        _onlinePivotCmd = null;
        _onlinePivotPace = 0f;
        _onlinePivotFromPointer = false;
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;
        _pointerRelocateSignalLoaded = true;
    }

    public bool EndMouseLook(LocomotionInput feed)
    {
        DemandPublished();
        if (!_pointerLooking && !_onlinePivotFromPointer)
            return false;

        _pointerLooking = false;
        GrabControl(feed);
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;

        FlipPointerGazeTravel(feed, entering: false);
        return true;
    }
    // Strafe keys always sidestep; in mouse-look the turn keys sidestep too, with the run hold key
    private static (uint? Command, bool UseRunHold) SidestepFor(LocomotionInput feed, bool pointerGazeEngaged)
    {
        if (feed.StrafeRight)
            return (LocomotionDirective.FlankHopRight, false);
        if (feed.StrafeLeft)
            return (LocomotionDirective.FlankHopLeft, false);
        if (pointerGazeEngaged && feed.TurnRight)
            return (LocomotionDirective.FlankHopRight, true);
        if (pointerGazeEngaged && feed.TurnLeft)
            return (LocomotionDirective.FlankHopLeft, true);
        return (null, false);
    }

    private void FlipPointerGazeTravel(LocomotionInput feed, bool entering)
    {
        (uint? sidestep, bool useExecGrip) = SidestepFor(feed, entering);
        SteerSidestep(sidestep, useExecGrip, reapply: !entering);

        uint? pivot = entering
            ? null
            : feed.TurnRight
                ? LocomotionDirective.TurnRight
                : feed.TurnLeft
                    ? LocomotionDirective.PivotLeft
                    : null;
        SteerPivot(pivot, pace: 1f, fromPointer: false, reapply: !entering);

        bool execPinned = _unpacker.RawPhase.CurrentHoldKey == HeldKey.Run;
        if (execPinned != feed.Run)
            _unpacker.set_hold_run(feed.Run, interrupt: true);

        uint? wantedAhead = feed.Forward
            ? LocomotionDirective.WalkForward
            : feed.Backward
                ? LocomotionDirective.StrollBackward
                : null;

        uint rawAhead = _unpacker.RawPhase.ForwardCommand;
        uint? engagedAhead = rawAhead == CrudeLocomotionPhase.Default.ForwardCommand
            ? null
            : rawAhead;
        if (engagedAhead is { } engaged && engaged != wantedAhead)
        {
            HaltLocomotionAtBoundary(
                engaged,
                new LocomotionParams());
        }

        if (wantedAhead is { } directive
            && (engagedAhead != wantedAhead || !entering))
            DoLocomotionAtBoundary(
                directive,
                new LocomotionParams());

        _feedSampled = true;
        _aheadPinnedPrior = feed.Forward;
        _backwardPinnedPrior = feed.Backward;
        _strafeLeftPinnedPrior = feed.StrafeLeft;
        _strafeRightPinnedPrior = feed.StrafeRight;
        _pivotLeftPinnedPrior = feed.TurnLeft;
        _pivotRightPinnedPrior = feed.TurnRight;
        _execPinnedPrior = feed.Run;

        _execGripPrior = feed.Run;
        _hull.PreviousRelocateWasAutonomous = true;
    }

    private bool SteerSidestep(
        uint? wanted,
        bool useExecGrip,
        bool reapply = false)
    {
        bool altered = wanted != _onlineSidestepCmd;
        if (_onlineSidestepCmd is { } engaged
            && (altered || reapply))
        {
            HaltLocomotionAtBoundary(
                engaged,
                _onlineSidestepExecGrip
                    ? PointerAxis(1f)
                    : new());
        }

        if (wanted is { } directive && (altered || reapply))
        {
            DoLocomotionAtBoundary(
                directive,
                useExecGrip ? PointerAxis(1f) : new());
        }

        _onlineSidestepCmd = wanted;
        _onlineSidestepExecGrip = wanted.HasValue && useExecGrip;
        return altered || (reapply && wanted.HasValue);
    }

    private bool SteerPivot(
        uint? wanted,
        float pace,
        bool fromPointer,
        bool reapply = false)
    {
        bool directiveAltered = wanted != _onlinePivotCmd;
        bool bothPresent = wanted.HasValue && _onlinePivotCmd.HasValue;
        bool paceAltered = bothPresent
            && MathF.Abs(pace - _onlinePivotPace) >= 0.0001f;
        bool holderAltered = bothPresent && fromPointer != _onlinePivotFromPointer;
        bool enact = directiveAltered || paceAltered || holderAltered
            || (reapply && wanted.HasValue);

        if (_onlinePivotCmd is { } engaged && enact)
        {
            HaltLocomotionAtBoundary(
                engaged,
                _onlinePivotFromPointer
                    ? PointerAxis(_onlinePivotPace)
                    : new());
        }

        if (wanted is { } directive && enact)
        {
            DoLocomotionAtBoundary(
                directive,
                fromPointer ? PointerAxis(pace) : new());
        }

        _onlinePivotCmd = wanted;
        _onlinePivotPace = wanted.HasValue ? pace : 0f;
        _onlinePivotFromPointer = wanted.HasValue && fromPointer;
        return enact;
    }

    private static LocomotionParams PointerAxis(
        float pace)
    {
        return new()
        {
            Speed = pace,
            SetHoldKey = false,
            GripTagToEnact = HeldKey.Run,
        };
    }

    private void GrabControl(LocomotionInput? latestFeed = null)
    {
        if (!_srvDriven)
            return;

        _srvDriven = false;
        _hull.PreviousRelocateWasAutonomous = true;
        HaltCompletelyAtKineticsObjectBoundary();
        _onlinePivotCmd = null;
        _onlinePivotPace = 0f;
        _onlinePivotFromPointer = false;
        _onlineSidestepCmd = null;
        _onlineSidestepExecGrip = false;

        _aheadPinnedPrior = false;
        _backwardPinnedPrior = false;
        _strafeLeftPinnedPrior = false;
        _strafeRightPinnedPrior = false;
        _pivotLeftPinnedPrior = false;
        _pivotRightPinnedPrior = false;
        _execPinnedPrior = _unpacker.RawPhase.CurrentHoldKey == HeldKey.Run;

        if (latestFeed is { } feed)
            FlipPointerGazeTravel(feed, entering: _pointerLooking);
    }
}
