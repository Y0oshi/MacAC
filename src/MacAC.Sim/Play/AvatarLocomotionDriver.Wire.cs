using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

public sealed partial class AvatarLocomotionDriver
{
    public const float HeartbeatInterval = 1.0f;

    private Locus _sentLocus;

    private Plane _sentLinkPlane;

    private float _sentAt;

    private bool _sentOnce;

    private bool _foreignRelocateSignalQueued;

    private CrudeLocomotionPhase? _foreignRawLocomotionQueued;

    private uint _actStamp;

    public void NoteTravelSent(float instantSecs, bool pointerGazeSignal = false)
    {
        DemandPublished();
        _sentAt = instantSecs;
        if (pointerGazeSignal)
        {
            _pointerRelocateSignalQueued = false;
            _pointerRelocateSignalAt = instantSecs;
        }
    }

    public LocomotionResult GrabTravelOutcome(bool pointerGazeSignal)
    {
        DemandPublished();
        CrudeLocomotionPhase raw = _unpacker.RawPhase;
        uint? ahead = raw.ForwardCommand == CrudeLocomotionPhase.Default.ForwardCommand
            ? null : raw.ForwardCommand;
        uint? sidestep = raw.SidestepDirective == CrudeLocomotionPhase.Default.SidestepDirective
            ? null : raw.SidestepDirective;
        uint? pivot = raw.TurnDirective == CrudeLocomotionPhase.Default.TurnDirective
            ? null : raw.TurnDirective;
        return new LocomotionResult(
            Position: Position,
            RenderPosition: RasterizeLocus,
            CellId: CellId,
            IsOnGround: CanTransmitLocusSignal,
            MotionStateChanged: true,
            ForwardCommand: ahead,
            SidestepCommand: sidestep,
            TurnCommand: pivot,
            ForwardSpeed: ahead.HasValue ? raw.ForwardSpeed : null,
            SidestepSpeed: sidestep.HasValue ? raw.SidestepPace : null,
            TurnSpeed: pivot.HasValue ? raw.TurnPace : null,
            IsRunning: raw.CurrentHoldKey == HeldKey.Run,
            ShouldSendMovementEvent: true,
            TurnUsesRunHold: pivot.HasValue && raw.PivotGripTag == HeldKey.Run,
            SidestepUsesRunHold: sidestep.HasValue
                && raw.SidestepGripTag == HeldKey.Run,
            IsMouseLookMovementEvent: pointerGazeSignal,
            CurrentStyle: raw.CurrentStyle);
    }

    public LocomotionResult GrabExhibitOutcome()
    {
        var latest = GrabTravelOutcome(pointerGazeSignal: false);
        return latest with
        {
            MotionStateChanged = false,
            ShouldSendMovementEvent = false,
            IsMouseLookMovementEvent = false,
        };
    }

    public void NoteLocusSent(Locus locus,
                                 Plane linkPlane,
                                 float instantSecs)
    {
        DemandPublished();
        _sentLocus = locus;
        _sentLinkPlane = linkPlane;
        _sentAt = instantSecs;
        _sentOnce = true;
    }

    public void AssignCorpusFacing(Quaternion facing)
    {
        DemandMutable();
        _hull.Orientation = PoseOps.AssignSpin(
            _hull.Position,
            _hull.Orientation,
            facing);
    }

    internal bool TryFetchOutgoingLocus(
        out Locus outgoingLocus)
    {
        DemandPublished();
        outgoingLocus = LatestChamberLocus;
        return PoseValidation.IsValid(
            outgoingLocus.ObjCellId,
            outgoingLocus.Frame.Origin,
            outgoingLocus.Frame.Orientation);
    }

    internal bool CorpusInLink => _hull.InContact;

    internal bool CanTransmitLocusSignal => _hull.InContact && _hull.OnWalkable;

    internal Quaternion CorpusFacing => _hull.Orientation;

    internal bool ShouldTransmitLocusSignal(
        Locus latestLocus,
        Plane latestLinkPlane,
        float instantSecs)
    {
        DemandPublished();
        if (!_sentOnce)
            return true;

        bool chamberAltered = _sentLocus.ObjCellId != latestLocus.ObjCellId;
        if ((_sentAt + HeartbeatInterval) >= instantSecs)
        {
            return chamberAltered
                || !NearlySamePlane(_sentLinkPlane, latestLinkPlane);
        }

        return chamberAltered
            || !NearlySamePosture(_sentLocus.Frame, latestLocus.Frame);
    }

    internal void AssignPreviousRelocateWasAutonomous(bool autonomous)
    {
        DemandMutable();
        _hull.PreviousRelocateWasAutonomous = autonomous;
        _srvDriven = !autonomous;
    }
}
