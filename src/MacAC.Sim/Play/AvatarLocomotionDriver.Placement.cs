using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

public sealed partial class AvatarLocomotionDriver
{
    private Action<float, MotionDeltaPose>? _trunkLocomotionHop;
    private readonly MotionDeltaPose _trunkLocomotionTemp = new();
    private readonly MotionDeltaPose _keeperDiffTemp = new();

    public void ImposeKineticsPhase(KineticStateFlags phase)
    {
        DemandMutable();
        _hull.State = phase;
        _hull.calc_acceleration();
    }

    public void FastenCycleVelAccessor(Func<Vector3> accessor)
    {
        DemandMutable();
        if (accessor is null) throw new ArgumentNullException(nameof(accessor));
        _unpacker.FetchCycleVel = accessor;
    }

    public void FastenAnimTrunkLocomotionSrc(
        Action<float, MotionDeltaPose> advance,
        Action? procTaps = null)
    {
        DemandMutable();
        _trunkLocomotionHop = advance
            ?? throw new ArgumentNullException(nameof(advance));
        _animTaps = procTaps;
        _trunkLocomotionTemp.Reset();
    }

    internal SimServerKineticsStateApplication ApplyServerPhysicsState(
        KineticStateFlags phase)
    {
        switch (_lifespan)
        {
            case AvatarLocomotionDriverPublicationLifespan.StandalonePublished:
            case AvatarLocomotionDriverPublicationLifespan.CandidatePreparing:
            case AvatarLocomotionDriverPublicationLifespan.RuntimePublished:
                _hull.State = phase;
                _hull.calc_acceleration();
                return SimServerKineticsStateApplication.AppliedLive;
            case AvatarLocomotionDriverPublicationLifespan.RuntimeOwnedDormant:
                return SimServerKineticsStateApplication
                    .DroppedDormantActivationOwned;
            default:
                return SimServerKineticsStateApplication
                    .DroppedDisplacedController;
        }
    }

    internal void SeedStanceForTest(Vector3 spot, uint chamberIdent, Vector3 chamberOwn)
    {
        DemandPublished();
        Place(
            spot,
            chamberIdent,
            chamberOwn,
            broadcastSharedPhase: true);
    }

    internal void ReadyLocusForSeal(
        Vector3 spot,
        uint chamberIdent,
        Vector3 chamberOwn)
    {
        DemandMutable();
        Place(
            spot,
            chamberIdent,
            chamberOwn,
            broadcastSharedPhase: false);
    }

    internal void ArmConstraintLeashAtSealedStance()
    {
        DemandPublished();
        RearmLeashHere();
    }

    internal void SealCanonForceLocusCycle()
    {
        DemandPublished();
        _hullSpotPrior = _hull.Position;
        _hullSpotInstant = _hull.Position;
        RenewChamber(_hull.CellPosition.ObjCellId, "force-position");
    }

    internal void SealCanonWarpCycle(
        bool zeroVel,
        bool rearmConstraintLeash,
        bool execWarpTapRear = true)
    {
        DemandPublished();
        _hullSpotPrior = _hull.Position;
        _hullSpotInstant = _hull.Position;
        RenewChamber(_hull.CellPosition.ObjCellId, "teleport");

        if (zeroVel)
            _hull.Velocity = Vector3.Zero;
        HaltCompletelyAtKineticsObjectBoundary();
        _onlinePivotCmd = null;
        _onlinePivotPace = 0f;
        _onlinePivotFromPointer = false;
        _onlineSidestepCmd = null;
        _onlineSidestepExecGrip = false;
        _pointerLooking = false;
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;
        _pointerRelocateSignalLoaded = false;
        _pointerRelocateSignalQueued = false;

        if (execWarpTapRear)
        {
            PlaceKeeper?.UnStick();
            PlaceKeeper?.UnConstrain();
            if (rearmConstraintLeash)
                RearmLeashHere();
        }

        // Reset the edge tracker: the stop wiped the motion state, so keys still physically held must
        // re-fire as press edges on the next Update (matches Place's walking-straight-out-of-a- teleport
        // behavior while W stays held).
        _aheadPinnedPrior = false;
        _backwardPinnedPrior = false;
        _strafeLeftPinnedPrior = false;
        _strafeRightPinnedPrior = false;
        _pivotLeftPinnedPrior = false;
        _pivotRightPinnedPrior = false;
        _execPinnedPrior = false;
        _feedSampled = false;

        _hull.PreviousRefreshMoment = 0.0;
        _quantumTimer.RestartForJoinRealm();
    }

    private void RenewChamber(
        uint newChamberIdent,
        string cause,
        bool broadcastRasterizeTrunk = true)
    {
        if (newChamberIdent != CellId && KineticTelemetry.ProbeCellEnabled)
        {
            Vector3 spot = _hull.Position;
            Console.WriteLine(System.FormattableString.Invariant(
                $"[cell-transit] 0x{CellId:X8} -> 0x{newChamberIdent:X8} pos=({spot.X:F3},{spot.Y:F3},{spot.Z:F3}) reason={cause}"));
        }
        CellId = newChamberIdent;

        if (broadcastRasterizeTrunk)
            _engine.RefreshAvatarCurrChamber(newChamberIdent);
    }

    private void RearmLeashHere()
    {
        if (PlaceKeeper is not { } locusKeeper)
            return;
        Locus mooring = _hull.CellPosition;
        locusKeeper.ConstrainTo(
            mooring,
            TetherDistance.FetchBeginConstraintGap(mooring.ObjCellId),
            TetherDistance.FetchUpperConstraintGap(mooring.ObjCellId));
    }

    private Vector3 _hullSpotPrior;

    private Vector3 _hullSpotInstant;

    private void Place(
        Vector3 spot,
        uint chamberIdent,
        Vector3 chamberOwn,
        bool broadcastSharedPhase)
    {
        _hull.SnapToChamber(chamberIdent, spot, chamberOwn);
        _hullSpotPrior = spot;
        _hullSpotInstant = spot;
        RenewChamber(
            _hull.CellPosition.ObjCellId,
            "teleport",
            broadcastSharedPhase);

        // Treat as grounded after a server-side position snap
        _hull.TransientState = TransientPhaseFlagSet.Contact
            | TransientPhaseFlagSet.OnWalkable
            | TransientPhaseFlagSet.Active;
        _hull.Velocity = Vector3.Zero;

        HaltCompletelyAtKineticsObjectBoundary();
        _onlinePivotCmd = null;
        _onlinePivotPace = 0f;
        _onlinePivotFromPointer = false;
        _onlineSidestepCmd = null;
        _onlineSidestepExecGrip = false;
        _pointerLooking = false;
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;
        _pointerRelocateSignalLoaded = false;
        _pointerRelocateSignalQueued = false;
        if (broadcastSharedPhase)
        {
            PlaceKeeper?.UnStick();
            PlaceKeeper?.UnConstrain();
            RearmLeashHere();
        }
        // Reset the edge tracker: the stop wiped the motion state, so keys still physically held must
        // re-fire as press edges on the next Update (matches the pre-W6 level-triggered behavior of
        // walking straight out of a teleport while W stays held).
        _aheadPinnedPrior = false;
        _backwardPinnedPrior = false;
        _strafeLeftPinnedPrior = false;
        _strafeRightPinnedPrior = false;
        _pivotLeftPinnedPrior = false;
        _pivotRightPinnedPrior = false;
        _execPinnedPrior = false;
        _feedSampled = false;

        _hull.PreviousRefreshMoment = 0.0;
        _quantumTimer.RestartForJoinRealm();
    }

    private Vector3 PaintSpot()
    {
        float alpha = Math.Clamp(
            (float)(_quantumTimer.QueuedSecs / _quantumPrior),
            0f,
            1f);
        return Vector3.Lerp(_hullSpotPrior, _hullSpotInstant, alpha);
    }

    private Action? _animTaps;
}
