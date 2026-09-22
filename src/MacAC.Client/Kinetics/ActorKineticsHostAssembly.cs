using System.Numerics;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Client.Kinetics;

internal static class ActorKineticsHostAssembly
{
    internal static ActorKineticsHarbor SetupOrRebind(
        OnlineActorCore core,
        OnlineActorRecord anticipatedCapture,
        ActorKineticsHarbor configuration)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        ArgumentNullException.ThrowIfNull(configuration);
        bool ProjIsLatest() =>
            core.TryFetchRecord(
                anticipatedCapture.ServerOid,
                out OnlineActorRecord latest)
            && ReferenceEquals(latest, anticipatedCapture);
        return core.Physics.SetupOrRebindKineticsHub(
            anticipatedCapture.Canonical,
            configuration,
            ProjIsLatest);
    }

    internal static ActorKineticsHarbor PickStableHubWithoutRebind(
        OnlineActorCore core,
        OnlineActorRecord anticipatedCapture,
        ActorKineticsHarbor configuration)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        ArgumentNullException.ThrowIfNull(configuration);
        bool ProjIsLatest() =>
            core.TryFetchRecord(
                anticipatedCapture.ServerOid,
                out OnlineActorRecord latest)
            && ReferenceEquals(latest, anticipatedCapture);
        return core.Physics.PickStableKineticsHubWithoutRebind(
            anticipatedCapture.Canonical,
            configuration,
            ProjIsLatest);
    }

    internal static ActorKineticsHarbor BuildMinimal(
        OnlineActorRecord capture,
        Func<uint, IKineticObjHost?> locate,
        Func<double> instant)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(locate);
        ArgumentNullException.ThrowIfNull(instant);
        return new ActorKineticsHarbor(
            capture.ServerOid,
            fetchLocus: () => new Locus(
                capture.WholeChamberIdent,
                capture.WorldEntity?.Position
                    ?? capture.KineticBody?.Position
                    ?? Vector3.Zero,
                capture.WorldEntity?.Rotation
                    ?? capture.KineticBody?.Orientation
                    ?? Quaternion.Identity),
            fetchVel: () => capture.KineticBody?.Velocity ?? Vector3.Zero,
            fetchRadius: () => 0f,
            inLink: () => capture.KineticBody?.InContact ?? true,
            minterpUpperPace: () => null,
            curMoment: instant,
            kineticsTickerMoment: instant,
            fetchObjectA: locate,
            hndRefreshMark: _ => { },
            interruptLatestTravel: () => { });
    }
}
