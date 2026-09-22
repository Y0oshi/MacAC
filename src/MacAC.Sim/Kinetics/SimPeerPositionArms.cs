using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Kinetics;

// How a granted (server-accepted) remote position is applied
internal enum SimPeerGrantedPositionArm : byte
{
    AirborneNoOperation,

    NearInterpolate,

    FarSnapPlacement,

    TeleportPlacement,

    UnroutedCatchUp,
}

// Teleports: a full set-position on a remote entity with the teleport flag
internal static class SimPeerWarpPosition
{
    internal static bool OwnsTeleportPlacement(SimSovereignPositionRoute? course)
    {
        return course is { Disposition: SimSovereignPositionVerdict.SetPosition, OperationKind: SimSetPositionOperationKind.RemoteAuthoritative }
        && (course.Value.SetPositionFlags & KineticSetPositionFlags.Teleport) != 0;
    }
}

// Far snaps: the simple set-position variant with the teleport flag, and the arm picked for any
// granted route
internal static class SimPeerFarSnapPosition
{
    internal static bool OwnsFarawaySnap(SimSovereignPositionRoute? course)
    {
        return course is { Disposition: SimSovereignPositionVerdict.SetPositionSimple, OperationKind: SimSetPositionOperationKind.RemoteAuthoritative }
        && (course.Value.SetPositionFlags & KineticSetPositionFlags.Teleport) != 0;
    }

    internal static SimPeerGrantedPositionArm ResolveArm(SimSovereignPositionRoute? course)
    {
        if (SimPeerSettledStatePosition.IsAirborneNoOp(course))
            return SimPeerGrantedPositionArm.AirborneNoOperation;
        if (SimPeerSettledStatePosition.IsNearbyLerp(course))
            return SimPeerGrantedPositionArm.NearInterpolate;
        return OwnsFarawaySnap(course) ? SimPeerGrantedPositionArm.FarSnapPlacement : SimPeerGrantedPositionArm.UnroutedCatchUp;
    }
}
