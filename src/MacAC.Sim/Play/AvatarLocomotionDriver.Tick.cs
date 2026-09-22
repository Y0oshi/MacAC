using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

/// <summary>
/// The per-frame step: hidden ticks, the full input-driven update, and the frame comparisons it
/// relies on.
/// </summary>
public sealed partial class AvatarLocomotionDriver
{
    private float _quantumPrior = KineticBody.LowerQuantum;

    internal bool AdvancedObjectQuantumPreviousBeat { get; private set; }

    public float PresentedDiffSecs { get; private set; }

    public LocomotionResult BeatConcealed(float dt, Action? hndTargeting = null)
    {
        DemandPublished();
        AdvancedObjectQuantumPreviousBeat = false;
        PresentedDiffSecs = 0f;
        if (!float.IsFinite(dt) || dt <= 0f)
        {
            return GrabExhibitOutcome() with
            {
                RenderPosition = _hull.Position,
                IsOnGround = _hull.OnWalkable,
            };
        }

        _simTimer += dt;
        double queuedPriorSecs = _quantumTimer.QueuedSecs;
        bool reactivated = _quantumTimer.Engage();
        _hull.TransientState |= TransientPhaseFlagSet.Active;
        CanonQuantumBatch lot = reactivated
            ? default
            : _quantumTimer.Advance(dt);
        AdvancedObjectQuantumPreviousBeat = lot.Count > 0;
        PresentedDiffSecs = PresentedDiff(dt, queuedPriorSecs, in lot);
        if (lot.Discarded)
        {
            _hullSpotPrior = _hull.Position;
            _hullSpotInstant = _hull.Position;
        }

        for (int qi = 0; qi < lot.Count; ++qi)
        {
            float quantum = lot.FetchQuantum(qi);
            Vector3 earlierLocus = _hull.Position;
            bool earlierLink = _hull.InContact;
            bool earlierOnPassable = _hull.OnWalkable;

            if (PlaceKeeper is { } keeper)
            {
                MotionDeltaPose diff = _keeperDiffTemp;
                diff.Reset();
                keeper.AdjustOffset(diff, quantum);
                if (diff.Origin != Vector3.Zero)
                    _hull.Position += Vector3.Transform(diff.Origin, _hull.Orientation);
                if (!diff.Orientation.IsIdentity)
                {
                    _hull.Orientation = PoseOps.AssignSpin(
                        _hull.Position,
                        _hull.Orientation,
                        _hull.Orientation * diff.Orientation);
                }
            }

            _animTaps?.Invoke();

            if (_hull.Position != earlierLocus && CellId is not 0 && _engine.LandblockTally > 0)
            {
                var settled = _engine.ResolveWithTransition(
                    earlierLocus,
                    _hull.Position,
                    CellId,
                    orbRadius: 0.48f,
                    orbHeight: 1.835f,
                    hopUpHeight: StepUpHeight,
                    hopDownHeight: StepDownHeight,
                    isOnTerrain: earlierOnPassable,
                    corpus: _hull,
                    carrierFlagSet: MoverState.IsPlayer | MoverState.EdgeSlide | OwnPvpFlagSet,
                    movingActorIdent: OwnEntityId,
                    orbRoster: OrbRoster,
                    orbScaling: ObjectScaling);
                _hull.SealChangeoverLocus(settled.CellId, settled.Position);
                KineticObjUpdate.SealSetLocusChangeover(
                    _hull,
                    settled.InContact,
                    settled.OnWalkable,
                    settled.CollisionNormalValid,
                    settled.CollisionNormal,
                    earlierLink,
                    earlierOnPassable,
                    Movement.HitGround,
                    _unpacker.LeaveGround);
                RenewChamber(settled.CellId, "hidden-position-manager");
            }

            CanonObjectKeeperTail.Run(
                hndTargeting,
                Movement,
                _unpacker.VerifyForCompletedMotions,
                PlaceKeeper);
            _hullSpotPrior = _hull.Position;
            _hullSpotInstant = _hull.Position;
        }

        _hullSpotPrior = _hull.Position;
        _hullSpotInstant = _hull.Position;
        _airbornePrior = !_hull.OnWalkable;

        return new LocomotionResult(
            Position: _hull.Position,
            RenderPosition: _hull.Position,
            CellId: CellId,
            IsOnGround: _hull.OnWalkable,
            MotionStateChanged: false,
            ForwardCommand: null,
            SidestepCommand: null,
            TurnCommand: null,
            ForwardSpeed: null,
            SidestepSpeed: null,
            TurnSpeed: null,
            CurrentStyle: _unpacker.RawPhase.CurrentStyle);
    }

    private bool _airbornePrior;

    private uint? _aheadCmdPrior;

    private uint? _sidestepCmdPrior;

    private uint? _pivotCmdPrior;

    private float? _aheadPacePrior;

    private bool _execGripPrior;

    private bool _aheadPinnedPrior;

    private bool _backwardPinnedPrior;

    private bool _strafeLeftPinnedPrior;

    private bool _strafeRightPinnedPrior;

    private bool _pivotLeftPinnedPrior;

    private bool _pivotRightPinnedPrior;

    private bool _execPinnedPrior;

    private bool _feedSampled;

    private float _simTimer;

    /// <summary>
    /// Sim-time accumulator (advanced by dt at the top of Update). Exposed for the network outbound
    /// layer to stamp NotePositionSent.
    /// </summary>
    public float SimMomentSecs => _simTimer;

    public bool IsAirborne => !_hull.OnWalkable;

    public float VerticalVelocity => _hull.Velocity.Z;

    /// <summary>Full 3D world-space velocity of the physics body. Exposed for diagnostic logging.</summary>
    public Vector3 CorpusVel => _hull.Velocity;

    public Plane ContactPlane => _hull.ContactPlane;

    /// <summary>
    /// Turns this tick's raw input into locomotion commands, and reports whether any of them
    /// actually changed what the avatar is doing.
    ///
    /// Everything here keys off an *edge* — a key going down or coming up — rather than the key
    /// being held, because the animation system is told to start and stop motions, not that a
    /// motion is ongoing. Any edge also takes the avatar back off server control: the player has
    /// just asked for something, so the server's idea of where they are heading is stale.
    ///
    /// <paramref name="travelSignalAsked"/> is raised, never lowered: it carries whether anything
    /// this tick means the server has to be told the avatar is moving.
    /// </summary>
    private bool ApplyLocomotionInput(LocomotionInput feed, ref bool travelSignalAsked)
    {
        bool edgeFired = false;
        bool persistentTravelPinned = feed.IsPersistentCommand
            && (feed.Forward
                || feed.Backward
                || feed.StrafeLeft
                || feed.StrafeRight
                || feed.TurnLeft
                || feed.TurnRight);
        if (_srvDriven && persistentTravelPinned)
            GrabControl();

        bool userFeedRim = feed.Run != _execPinnedPrior
            || feed.Forward != _aheadPinnedPrior
            || feed.Backward != _backwardPinnedPrior
            || feed.StrafeLeft != _strafeLeftPinnedPrior
            || feed.StrafeRight != _strafeRightPinnedPrior
            || feed.TurnLeft != _pivotLeftPinnedPrior
            || feed.TurnRight != _pivotRightPinnedPrior;
        if (userFeedRim)
            GrabControl();

        LocomotionParams p = new LocomotionParams();

        if (feed.Run != _execPinnedPrior)
        {
            _unpacker.set_hold_run(feed.Run, interrupt: true);
            edgeFired = true;
        }

        if (feed.Forward && !_aheadPinnedPrior)
        { DoLocomotionAtBoundary(LocomotionDirective.WalkForward, p); edgeFired = true; }
        else if (feed.Backward && !_backwardPinnedPrior && !feed.Forward)
        { DoLocomotionAtBoundary(LocomotionDirective.StrollBackward, p); edgeFired = true; }
        if (!feed.Forward && _aheadPinnedPrior)
        {
            if (feed.Backward)
                DoLocomotionAtBoundary(LocomotionDirective.StrollBackward, p);
            else
                HaltLocomotionAtBoundary(LocomotionDirective.WalkForward, p);
            edgeFired = true;
        }
        else if (!feed.Backward && _backwardPinnedPrior && !feed.Forward)
        { HaltLocomotionAtBoundary(LocomotionDirective.StrollBackward, p); edgeFired = true; }

        (uint? wantedSidestep, bool sidestepUsesExecGrip) =
            SidestepFor(feed, _pointerLooking);
        if (SteerSidestep(wantedSidestep, sidestepUsesExecGrip))
            edgeFired = true;

        travelSignalAsked |= edgeFired;

        bool keyboardPivotRim = feed.TurnRight != _pivotRightPinnedPrior
                             || feed.TurnLeft != _pivotLeftPinnedPrior;
        if (keyboardPivotRim)
            travelSignalAsked = true;

        uint? wantedPivotDirective = _pointerLooking
            ? null
            : feed.TurnRight
                ? LocomotionDirective.TurnRight
                : feed.TurnLeft
                    ? LocomotionDirective.PivotLeft
                    : null;
        float wantedPivotPace = 1f;
        bool wantedPivotFromPointer = false;

        if (_pointerLooking && _pointerPivotQueued)
        {
            float adjustment = _pointerPivotDiff;
            _pointerPivotQueued = false;
            _pointerPivotDiff = 0f;

            if (MathF.Abs(adjustment) >= PointerPivotDeadZone)
            {
                wantedPivotDirective = adjustment < 0f
                    ? LocomotionDirective.TurnRight
                    : LocomotionDirective.PivotLeft;
                wantedPivotPace = MathF.Min(
                    MathF.Abs(adjustment) * PointerPivotPaceScaling,
                    PointerPivotCeilingPace);
                wantedPivotFromPointer = true;
            }
        }
        else if (_pointerLooking && _onlinePivotFromPointer)
        {
            wantedPivotDirective = _onlinePivotCmd;
            wantedPivotPace = _onlinePivotPace;
            wantedPivotFromPointer = true;
        }

        if (SteerPivot(
                wantedPivotDirective,
                wantedPivotPace,
                wantedPivotFromPointer))
            edgeFired = true;

        if (edgeFired)
        {
            _hull.PreviousRelocateWasAutonomous = true;
            _srvDriven = false;
        }
        return edgeFired;
    }

    public LocomotionResult Update(
        float dt,
        LocomotionInput feed,
        Action? hndTargeting = null)
    {
        DemandPublished();
        AdvancedObjectQuantumPreviousBeat = false;
        PresentedDiffSecs = 0f;
        if (!float.IsFinite(dt) || dt <= 0f)
            return GrabExhibitOutcome();

        _simTimer += dt;

        if (State == AvatarPhase.PortalSpace)
        {
            return new LocomotionResult(
                Position: Position,
                RenderPosition: RasterizeLocus,
                CellId: CellId,
                IsOnGround: _hull.OnWalkable,
                MotionStateChanged: false,
                ForwardCommand: null,
                SidestepCommand: null,
                TurnCommand: null,
                ForwardSpeed: null,
                SidestepSpeed: null,
                TurnSpeed: null,
                CurrentStyle: _unpacker.RawPhase.CurrentStyle);
        }

        if (!_feedSampled)
        {
            _feedSampled = true;
            _execPinnedPrior = feed.Run;
            _execGripPrior = feed.Run;
            _unpacker.set_hold_run(feed.Run, interrupt: false);
        }

        bool externallyAskedTravelSignal =
            _foreignRelocateSignalQueued;
        _foreignRelocateSignalQueued = false;
        var externalRawLocomotionPhase =
            _foreignRawLocomotionQueued;
        _foreignRawLocomotionQueued = null;
        bool travelSignalAsked = externallyAskedTravelSignal;
        bool locomotionRimFired = ApplyLocomotionInput(feed, ref travelSignalAsked);

        _aheadPinnedPrior = feed.Forward;
        _backwardPinnedPrior = feed.Backward;
        _strafeLeftPinnedPrior = feed.StrafeLeft;
        _strafeRightPinnedPrior = feed.StrafeRight;
        _pivotLeftPinnedPrior = feed.TurnLeft;
        _pivotRightPinnedPrior = feed.TurnRight;
        _execPinnedPrior = feed.Run;

        bool hasAnimTrunkLocomotion = _trunkLocomotionHop is not null;

        float? outLeapReach = null;
        Vector3? outLeapVel = null;

        if (feed.Jump && !_chargingLeap && !_leapPinnedPrior)
        {
            WeenieProblem chargeOutcome = _unpacker.ChargeJump();
            if (chargeOutcome == WeenieProblem.None)
            {
                _chargingLeap = true;
                _leapReach = 0f;
            }
            else
            {
                RefuseJump(chargeOutcome);
            }
        }

        if (feed.Jump && _chargingLeap)
        {
            float chargeRate = _unpacker.InterpretedPhase.LatestStyle
                == MacAC.Mechanics.Fighting.FightInputPlanner.DualWieldFightingStyling
                    ? DualWieldLeapChargeRate
                    : LeapChargeRate;
            _leapReach = MathF.Min(_leapReach + dt * chargeRate, 1.0f);
        }
        else if (_chargingLeap)
        {
            WeenieProblem leapOutcome = _unpacker.jump(_leapReach);
            if (leapOutcome == WeenieProblem.None)
            {
                float leapVz = _unpacker.FetchLeapVZ();
                outLeapReach = _leapReach;
                Vector3 leapVel = _unpacker.get_state_velocity();
                outLeapVel = new Vector3(leapVel.X, leapVel.Y, leapVz);

                _hull.set_local_velocity(outLeapVel.Value, autonomous: true);
            }
            else
            {
                RefuseJump(leapOutcome);
            }
            _chargingLeap = false;
            _leapReach = 0f;
        }

        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeJumpEnabled
            && (feed.Jump || _leapPinnedPrior))
        {
            Console.WriteLine(
                $"[jump-tick] input.Jump={feed.Jump} prevJumpHeld={_leapPinnedPrior} "
                + $"onWalkable={_hull.OnWalkable} jumpCharging={_chargingLeap} "
                + $"jumpExtent={_leapReach:F2}");
        }
        _leapPinnedPrior = feed.Jump;

        double queuedPriorSecs = _quantumTimer.QueuedSecs;
        bool reactivated = _quantumTimer.Engage();
        _hull.TransientState |= TransientPhaseFlagSet.Active;
        CanonQuantumBatch quantumLot = reactivated
            ? default
            : _quantumTimer.Advance(dt);
        AdvancedObjectQuantumPreviousBeat = quantumLot.Count > 0;
        PresentedDiffSecs =
            PresentedDiff(dt, queuedPriorSecs, in quantumLot);
        bool justLanded = false;
        if (quantumLot.Discarded)
        {
            _hullSpotPrior = _hull.Position;
            _hullSpotInstant = _hull.Position;
        }

        for (int qi = 0; qi < quantumLot.Count; ++qi)
        {
            float beatDt = quantumLot.FetchQuantum(qi);
            bool grabQuantum = AvatarKineticsQuantumCapture.IsTurnedOn;
            uint grabChamberPrior = CellId;
            AvatarKineticsHullTraceCapture grabQuantumBegin = grabQuantum
                ? AvatarKineticsQuantumCapture.Freeze(_hull)
                : default;

            MotionDeltaPose pmDiff = _keeperDiffTemp;
            pmDiff.Reset();
            if (_trunkLocomotionHop is { } proceedTrunkLocomotion)
            {
                _trunkLocomotionTemp.Reset();
                proceedTrunkLocomotion(beatDt, _trunkLocomotionTemp);
                pmDiff.Origin = _hull.OnWalkable
                    ? _trunkLocomotionTemp.Origin * ObjectScaling
                    : Vector3.Zero;
                pmDiff.Orientation = _trunkLocomotionTemp.Orientation;
            }
            else if (_unpacker.InterpretedPhase.PivotDirective == LocomotionDirective.TurnRight)
            {
                Yaw -= 1.5f
                     * _unpacker.InterpretedPhase.PivotPace
                     * beatDt;
            }

            if (_hull.OnWalkable && !hasAnimTrunkLocomotion)
            {
                float storedRealmVz = _hull.Velocity.Z;
                Vector3 phaseVel = _unpacker.get_state_velocity();
                _hull.set_local_velocity(
                    new Vector3(phaseVel.X, phaseVel.Y, storedRealmVz),
                    autonomous: _hull.PreviousRelocateWasAutonomous);
            }

            Vector3 preIntegrateSpot = _hull.Position;
            Vector3 formerBeatFinishSpot = _hullSpotInstant;

            PlaceKeeper?.AdjustOffset(pmDiff, beatDt);
            _hull.IsFullyConstrained = PlaceKeeper?.IsFullyConstrained() ?? false;
            if (pmDiff.Origin != Vector3.Zero)
                _hull.Position += Vector3.Transform(pmDiff.Origin, _hull.Orientation);
            if (!pmDiff.Orientation.IsIdentity)
            {
                _hull.Orientation = PoseOps.AssignSpin(
                    _hull.Position,
                    _hull.Orientation,
                    _hull.Orientation * pmDiff.Orientation);
            }

            _hull.calc_acceleration();
            AvatarKineticsHullTraceCapture grabPreIntegration = grabQuantum
                ? AvatarKineticsQuantumCapture.Freeze(_hull)
                : default;
            _hull.RefreshKineticsInternal(beatDt);
            AvatarKineticsHullTraceCapture grabPostIntegration = grabQuantum
                ? AvatarKineticsQuantumCapture.Freeze(_hull)
                : default;

            _animTaps?.Invoke();
            Vector3 postIntegrateSpot = _hull.Position;

            bool contenderMoved = postIntegrateSpot != preIntegrateSpot;

            ResolveVerdict locateOutcome = _engine.ResolveWithTransition(
                preIntegrateSpot, postIntegrateSpot, CellId,
                orbRadius: 0.48f,
                orbHeight: 1.835f,
                hopUpHeight: StepUpHeight,
                hopDownHeight: StepDownHeight,  // L.2.3a: from Setup.StepDownHeight
                isOnTerrain: _hull.OnWalkable,
                corpus: _hull,
                carrierFlagSet: MacAC.Mechanics.Kinetics.MoverState.IsPlayer
                          | MacAC.Mechanics.Kinetics.MoverState.EdgeSlide
                          | OwnPvpFlagSet,
                movingActorIdent: OwnEntityId,
                orbRoster: OrbRoster,
                orbScaling: ObjectScaling);

            if (MacAC.Mechanics.Kinetics.KineticTelemetry.DumpSteepRoofEnabled
                && locateOutcome.CollisionNormalValid)
            {
                Console.WriteLine(
                    $"[steep-roof] FRAME pre=({preIntegrateSpot.X:F2},{preIntegrateSpot.Y:F2},{preIntegrateSpot.Z:F2}) " +
                    $"post=({postIntegrateSpot.X:F2},{postIntegrateSpot.Y:F2},{postIntegrateSpot.Z:F2}) " +
                    $"resolved=({locateOutcome.Position.X:F2},{locateOutcome.Position.Y:F2},{locateOutcome.Position.Z:F2}) " +
                    $"isOnGround={locateOutcome.IsOnGround}");
            }

            _hull.StashedVel = contenderMoved
                ? (locateOutcome.Position - preIntegrateSpot) / beatDt
                : Vector3.Zero;

            bool earlierLink = _hull.InContact;
            bool earlierOnPassable = _hull.OnWalkable;

            _hull.SealChangeoverLocus(locateOutcome.CellId, locateOutcome.Position);
            _hullSpotPrior = formerBeatFinishSpot;
            _hullSpotInstant = _hull.Position;

            bool landedThisQuantum = false;
            if (locateOutcome.Ok && contenderMoved)
            {
                if (locateOutcome.InContact)
                    _hull.TransientState |= TransientPhaseFlagSet.Contact;
                else
                    _hull.TransientState &= ~TransientPhaseFlagSet.Contact;
                _hull.calc_acceleration();

                if (locateOutcome.InContact && locateOutcome.OnWalkable)
                {
                    bool wasAirborne = !_hull.OnWalkable;
                    _hull.TransientState |= TransientPhaseFlagSet.OnWalkable;
                    if (wasAirborne)
                    {
                        Movement.HitGround();
                        landedThisQuantum = true;
                    }
                }
                else
                {
                    _hull.TransientState &= ~TransientPhaseFlagSet.OnWalkable;
                }
                _hull.calc_acceleration();          // pc:283475/283490 (set_on_walkable tail)

                KineticObjUpdate.HandleAllCollisions(
                    _hull,
                    locateOutcome.CollisionNormalValid, locateOutcome.CollisionNormal,
                    earlierLink, earlierOnPassable, instantOnPassable: _hull.OnWalkable);
            }

            if (!_hull.OnWalkable && !_airbornePrior)
                _unpacker.LeaveGround();

            _airbornePrior = !_hull.OnWalkable;
            justLanded |= landedThisQuantum;
            RenewChamber(locateOutcome.CellId, "resolver");

            CanonObjectKeeperTail.Run(
                hndTargeting,
                Movement,
                _unpacker.VerifyForCompletedMotions,
                PlaceKeeper);

            if (grabQuantum)
            {
                AvatarKineticsQuantumCapture.Trace(
                    beatDt,
                    grabChamberPrior,
                    CellId,
                    feed,
                    grabPreIntegration.Position - grabQuantumBegin.Position,
                    contenderMoved,
                    grabQuantumBegin,
                    grabPreIntegration,
                    grabPostIntegration,
                    new AvatarKineticsResolveTraceCapture(
                        locateOutcome.Position,
                        locateOutcome.CellId,
                        locateOutcome.Ok,
                        locateOutcome.IsOnGround,
                        locateOutcome.InContact,
                        locateOutcome.OnWalkable,
                        locateOutcome.CollisionNormalValid,
                        locateOutcome.CollisionNormal),
                    AvatarKineticsQuantumCapture.Freeze(_hull));
            }

        }

        uint? outAheadCmd = null;
        float? outAheadPace = null;
        uint? outSidestepCmd = null;
        float? outSidestepPace = null;
        uint? outPivotCmd = null;
        float? outPivotPace = null;

        if (feed.Forward)
        {
            outAheadCmd = LocomotionDirective.WalkForward;
            outAheadPace = 1.0f;
        }
        else if (feed.Backward)
        {
            outAheadCmd = LocomotionDirective.StrollBackward;
            outAheadPace = 1.0f;
        }
        else if (_unpacker.RawPhase.ForwardCommand is (
            LocomotionDirective.Crouch
            or LocomotionDirective.Sitting
            or LocomotionDirective.Sleeping))
        {
            outAheadCmd = _unpacker.RawPhase.ForwardCommand;
            outAheadPace = _unpacker.RawPhase.ForwardSpeed;
        }

        if (_onlineSidestepCmd is { } engagedFeedSidestep)
        {
            outSidestepCmd = engagedFeedSidestep;
            outSidestepPace = _unpacker.RawPhase.SidestepPace;
        }

        if (_onlinePivotCmd is { } engagedFeedPivot)
        {
            outPivotCmd = engagedFeedPivot;
            outPivotPace = _onlinePivotPace;
        }

        bool execGrip = feed.Run;
        bool altered = outAheadCmd != _aheadCmdPrior
                    || outSidestepCmd != _sidestepCmdPrior
                    || outPivotCmd != _pivotCmdPrior
                    || !FloatsEqual(outAheadPace, _aheadPacePrior)
                    || execGrip != _execGripPrior
                    || locomotionRimFired
                    || externallyAskedTravelSignal;

        bool pointerTravelSignalDue = _pointerRelocateSignalQueued
            || (_pointerRelocateSignalLoaded
                && _simTimer > _pointerRelocateSignalAt + PointerTravelSignalInterval);
        _pointerRelocateSignalLoaded = false;
        if (pointerTravelSignalDue)
            _pointerRelocateSignalQueued = true;
        bool shouldTransmitTravelSignal = travelSignalAsked || pointerTravelSignalDue;

        _aheadCmdPrior = outAheadCmd;
        _sidestepCmdPrior = outSidestepCmd;
        _pivotCmdPrior = outPivotCmd;
        _aheadPacePrior = outAheadPace;
        _execGripPrior = execGrip;

        static bool FloatsEqual(float? a, float? b)
        {
            if (a.HasValue != b.HasValue) return false;
            return !a.HasValue || !b.HasValue ? true : System.Math.Abs(a.Value - b.Value) < 1e-4f;
        }

        return new LocomotionResult(
            Position: Position,
            RenderPosition: RasterizeLocus,
            CellId: CellId,
            IsOnGround: _hull.OnWalkable,
            MotionStateChanged: altered,
            ForwardCommand: outAheadCmd,
            SidestepCommand: outSidestepCmd,
            TurnCommand: outPivotCmd,
            ForwardSpeed: outAheadPace,
            SidestepSpeed: outSidestepPace,
            TurnSpeed: outPivotPace,
            IsRunning: feed.Run,
            JustLanded: justLanded,
            JumpExtent: outLeapReach,
            JumpVelocity: outLeapVel,
            ShouldSendMovementEvent: shouldTransmitTravelSignal,
            TurnUsesRunHold: _onlinePivotFromPointer && outPivotCmd.HasValue,
            SidestepUsesRunHold: _onlineSidestepExecGrip
                && outSidestepCmd.HasValue,
            IsMouseLookMovementEvent: pointerTravelSignalDue,
            CurrentStyle: _unpacker.RawPhase.CurrentStyle,
            RawMotionStateOverride: externalRawLocomotionPhase);
    }

    internal void SuspendObjectRefresh(float passedSecs)
    {
        DemandPublished();
        AdvancedObjectQuantumPreviousBeat = false;
        PresentedDiffSecs = 0f;
        if (float.IsFinite(passedSecs) && passedSecs > 0f)
            _simTimer += passedSecs;
        _quantumTimer.Deactivate();
        _hull.TransientState &= ~TransientPhaseFlagSet.Active;
    }

    private float PresentedDiff(
        float wallDt,
        double queuedPriorSecs,
        in CanonQuantumBatch lot)
    {
        if (lot.Discarded)
        {
            _quantumPrior = KineticBody.LowerQuantum;
            return wallDt;
        }
        float earlierInterval = _quantumPrior;
        float simulated = 0f;
        if (lot.Count > 0)
        {
            simulated = lot.FullSteps * KineticBody.MaxQuantum + lot.Remainder;
            _quantumPrior = lot.FetchQuantum(lot.Count - 1);
        }
        float interval = _quantumPrior;
        float prior = Math.Min((float)queuedPriorSecs, earlierInterval);
        float following = Math.Min((float)_quantumTimer.QueuedSecs, interval);
        float diff = simulated + (following - interval) - (prior - earlierInterval);
        return Math.Max(diff, 0f);
    }

    private static bool NearlySamePosture(CellPose pose, CellPose b)
    {
        const float Epsilon = 0.000199999995f;
        return MathF.Abs(pose.Origin.X - b.Origin.X) <= Epsilon
            && MathF.Abs(pose.Origin.Y - b.Origin.Y) <= Epsilon
            && MathF.Abs(pose.Origin.Z - b.Origin.Z) <= Epsilon
            && MathF.Abs(pose.Orientation.W - b.Orientation.W) < Epsilon
            && MathF.Abs(pose.Orientation.X - b.Orientation.X) < Epsilon
            && MathF.Abs(pose.Orientation.Y - b.Orientation.Y) < Epsilon
            && MathF.Abs(pose.Orientation.Z - b.Orientation.Z) < Epsilon;
    }

    private static bool NearlySamePlane(
        Plane a, Plane b)
    {
        const float Epsilon = 0.000199999995f;
        return MathF.Abs(a.Normal.X - b.Normal.X) <= Epsilon
            && MathF.Abs(a.Normal.Y - b.Normal.Y) <= Epsilon
            && MathF.Abs(a.Normal.Z - b.Normal.Z) <= Epsilon
            && MathF.Abs(a.D - b.D) < Epsilon;
    }
}
