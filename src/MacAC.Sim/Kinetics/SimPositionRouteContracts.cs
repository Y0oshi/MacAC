using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal enum SimPositionActorKind : byte
{
    Unknown,
    LocalPlayer,
    Remote,
    Projectile,
}

internal enum SimCreateTenancyKind : byte
{
    Unknown,
    TopLevel,
    Parented,
    PickedUp,
}

internal enum SimGrantedPositionSource : byte
{
    Unknown,
    PositionEvent,
    SameIncarnationCreate,
}

internal enum SimSovereignPositionVerdict : byte
{
    RejectedAuthority,
    RejectedData,
    AwaitFreshPosition,
    NoPositionOperation,
    Interpolate,
    SetPosition,
    SetPositionSimple,
}

internal enum SimWarpHookPhase : byte
{
    None,
    BeforePositionOperation,
    AfterPositionOperation,
    AfterEnterWorld,
}

internal enum SimPositionConstrainPhase : byte
{
    None,
    BeforePositionOperation,
    AfterPositionOperation,
}

internal readonly record struct SimSovereignPositionAuthority(
    SimEpochTicket Generation,
    SimActorKey Entity,
    ulong PositionAuthorityVersion,
    ushort AcceptedPositionSequence,
    ushort PreviousTeleportSequence,
    ushort AcceptedTeleportSequence,
    PoseStampVerdict TimestampDisposition)
{
    internal bool IsStructurallyValid
    {
        get
        {
            return Generation.Value is not 0UL
        && Entity.LocalEntityId is not 0u
        && PositionAuthorityVersion is not 0UL
        && TimestampDisposition is PoseStampVerdict.Apply
            or PoseStampVerdict.ForcePosition;
        }
    }

    internal bool WarpAdvanced
    {
        get
        {
            return TimestampDisposition is PoseStampVerdict.Apply
        && KineticStampGate.IsNewer(
            PreviousTeleportSequence,
            AcceptedTeleportSequence);
        }
    }

    internal bool WarpRegressed
    {
        get
        {
            return KineticStampGate.IsNewer(
            AcceptedTeleportSequence,
            PreviousTeleportSequence);
        }
    }
}

internal readonly record struct SimPositionPlacementFacts(
    KineticStateFlags PhysicsState,
    bool HasAuthoredMoverShape)
{
    internal bool ImpactLotEligible =>
        (PhysicsState & KineticStateFlags.Hidden) == 0;
}

internal readonly record struct SimCreatePositionRouteRequest(
    SimSovereignPositionAuthority Authority,
    SimPositionActorKind EntityKind,
    SimCreateTenancyKind Residence,
    ObjectCreation.RemotePosition? AcceptedWirePosition,
    SimPositionPlacementFacts PlacementFacts);

internal readonly record struct SimGrantedPositionRouteRequest(
    SimSovereignPositionAuthority Authority,
    SimPositionActorKind EntityKind,
    SimGrantedPositionSource Source,
    ObjectCreation.RemotePosition AcceptedWirePosition,
    uint? PlacementFrame,
    Vector3? PositionPackVelocity,
    uint? CommittedCellId,
    bool HasContact,
    float PlayerDistance,
    bool UsePositionFromServer,
    bool HasAnimations,
    SimPositionPlacementFacts PlacementFacts);

internal readonly record struct SimGrantedPositionPrePlacementFlags(
    bool UnparentBeforeRouting,
    bool ApplyPlacementFrameBeforeRouting);

internal readonly record struct SimSovereignPositionRoute(
    SimSovereignPositionAuthority Authority,
    SimSovereignPositionVerdict Disposition,
    SimSetPositionOperationKind OperationKind,
    KineticSetPositionFlags SetPositionFlags,
    uint PlacementFrame,
    bool UnparentBeforeRouting,
    bool ApplyPlacementFrameBeforeRouting,
    bool LeaveWorld,
    SimWarpHookPhase TeleportHookPhase,
    bool StopInterpolating,
    SimPositionConstrainPhase ConstrainPhase,
    bool PreserveHeading,
    bool ZeroVelocity,
    bool SendPositionImmediately,
    bool CollisionBatchEligible)
{
    internal bool Accepted
    {
        get
        {
            return Disposition is not
        SimSovereignPositionVerdict.RejectedAuthority
        and not SimSovereignPositionVerdict.RejectedData;
        }
    }

    internal bool PerformsSetLocus
    {
        get
        {
            return Disposition is
        SimSovereignPositionVerdict.SetPosition
        or SimSovereignPositionVerdict.SetPositionSimple;
        }
    }

    internal bool ExecutionsWarpTap =>
        TeleportHookPhase is not SimWarpHookPhase.None;

    internal bool ConstrainPriorRouting =>
        ConstrainPhase is SimPositionConstrainPhase.BeforePositionOperation;

    internal bool ConstrainFollowingRouting =>
        ConstrainPhase is SimPositionConstrainPhase.AfterPositionOperation;
}
