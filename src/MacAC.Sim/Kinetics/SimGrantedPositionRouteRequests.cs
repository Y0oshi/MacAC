using MacAC.Wire;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// Builds the route request for a server-granted position from the entity's record and the accepted
// update
internal static class SimGrantedPositionRouteRequests
{
    // The facts the classifier needs beyond the record itself
    internal readonly record struct Context(
        SimEpochTicket Generation,
        SimPositionActorKind EntityKind,
        SimGrantedPositionSource Source,
        PoseStampVerdict Disposition,
        ushort PreviousTeleportSequence,
        ushort AcceptedTeleportSequence,
        bool UsePositionFromServer);

    internal static SimGrantedPositionRouteRequest Build(
        SimEpochTicket gen,
        SimActorRecord canon,
        SimActorKey tag,
        in RealmSession.MoverPositionUpdate refresh,
        SimPositionActorKind actorSort,
        SimGrantedPositionSource src,
        PoseStampVerdict disposition,
        ushort earlierWarpSeries,
        ushort approvedWarpSeries,
        float avatarGap,
        bool useLocusFromSrv)
    {
        return Build(gen, canon, tag, refresh, actorSort, src, disposition, earlierWarpSeries, approvedWarpSeries,
            avatarGap, useLocusFromSrv, sealedChamberIdent: canon.WholeChamberTag);
    }

    internal static SimGrantedPositionRouteRequest Build(
        SimEpochTicket gen,
        SimActorRecord canon,
        SimActorKey tag,
        in RealmSession.MoverPositionUpdate refresh,
        SimPositionActorKind actorSort,
        SimGrantedPositionSource src,
        PoseStampVerdict disposition,
        ushort earlierWarpSeries,
        ushort approvedWarpSeries,
        float avatarGap,
        bool useLocusFromSrv,
        uint? sealedChamberIdent)
    {
        ArgumentNullException.ThrowIfNull(canon);
        var ctx = new Context(gen, actorSort, src, disposition, earlierWarpSeries, approvedWarpSeries, useLocusFromSrv);
        return Compose(canon, tag, in refresh, in ctx, avatarGap, sealedChamberIdent);
    }

    // False when the entity has no local key yet or the player distance is unknown
    internal static bool TryAssemble(
        SimEpochTicket gen,
        SimActorRecord canon,
        in RealmSession.MoverPositionUpdate refresh,
        SimPositionActorKind actorSort,
        SimGrantedPositionSource src,
        PoseStampVerdict disposition,
        ushort earlierWarpSeries,
        ushort approvedWarpSeries,
        float? avatarGap,
        bool useLocusFromSrv,
        out SimGrantedPositionRouteRequest req)
    {
        ArgumentNullException.ThrowIfNull(canon);
        if (canon.Key is not { } tag || avatarGap is not { } gap)
        {
            req = default;
            return false;
        }
        req = Build(gen, canon, tag, refresh, actorSort, src, disposition, earlierWarpSeries, approvedWarpSeries, gap, useLocusFromSrv);
        return true;
    }

    internal static bool TryAssemble(
        SimEpochTicket gen,
        SimActorRecord canon,
        in RealmSession.MoverPositionUpdate refresh,
        SimPositionActorKind actorSort,
        SimGrantedPositionSource src,
        PoseStampVerdict disposition,
        ushort earlierWarpSeries,
        ushort approvedWarpSeries,
        float? avatarGap,
        bool useLocusFromSrv,
        uint? sealedChamberIdent,
        out SimGrantedPositionRouteRequest req)
    {
        ArgumentNullException.ThrowIfNull(canon);
        if (canon.Key is not { } tag || avatarGap is not { } gap || sealedChamberIdent is null)
        {
            req = default;
            return false;
        }
        req = Build(gen, canon, tag, refresh, actorSort, src, disposition, earlierWarpSeries, approvedWarpSeries,
            gap, useLocusFromSrv, sealedChamberIdent);
        return true;
    }

    private static SimGrantedPositionRouteRequest Compose(
        SimActorRecord canon,
        SimActorKey tag,
        in RealmSession.MoverPositionUpdate refresh,
        in Context context,
        float avatarGap,
        uint? sealedChamberIdent)
    {
        var arbiter = new SimSovereignPositionAuthority(
            context.Generation, tag, canon.PositionAuthorityVersion, refresh.PositionSequence,
            context.PreviousTeleportSequence, context.AcceptedTeleportSequence, context.Disposition);

        uint? locomotionChart = canon.Snapshot.MotionTableId ?? canon.Snapshot.Physics?.MotionTableId;
        bool moving = locomotionChart is { } ident && ident is not 0u;

        return new SimGrantedPositionRouteRequest(
            arbiter,
            context.EntityKind,
            context.Source,
            refresh.Position,
            refresh.PlacementId,
            refresh.Velocity,
            sealedChamberIdent,
            refresh.IsGrounded,
            avatarGap,
            context.UsePositionFromServer,
            moving,
            new SimPositionPlacementFacts(canon.FinalKineticsCondition, canon.Snapshot.SetupTableId is not null));
    }
}
