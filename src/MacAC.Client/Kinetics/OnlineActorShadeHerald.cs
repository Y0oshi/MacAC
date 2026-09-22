using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Kinetics;

internal static class OnlineActorShadeHerald
{
    internal static bool TryBroadcastDistant(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        ISimPeerMotion distant,
        ulong locusArbiterVer,
        Action broadcast)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(distant);
        ArgumentNullException.ThrowIfNull(broadcast);

        if (!CanBroadcast(
                core,
                capture,
                actor,
                distant,
                locusArbiterVer))

            return false;

        broadcast();
        return CanBroadcast(
            core,
            capture,
            actor,
            distant,
            locusArbiterVer);
    }

    private static bool CanBroadcast(
        OnlineActorCore core,
        OnlineActorRecord capture,
        RealmActor actor,
        ISimPeerMotion distant,
        ulong locusArbiterVer)
    {
        return (capture.FinalKineticsPhase & KineticStateFlags.Hidden) == 0
        && ReferenceEquals(capture.WorldEntity, actor)
        && core.IsLatestLocusArbiter(capture, locusArbiterVer)
        && core.IsLatestSpatialDistantLocomotion(capture, distant);
    }
}
