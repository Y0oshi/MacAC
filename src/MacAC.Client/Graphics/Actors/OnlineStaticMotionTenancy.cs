using MacAC.Client.Realm;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed class OnlineStaticMotionTenancy(IOnlineActorEngineSource runtime)
{
    private readonly IOnlineActorEngineSource _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public bool IsHoused(RealmActor actor)
    {
        return actor.ServerGuid is 0
        || (_runtime.Current?.TryFetchRecord(actor.ServerGuid, out OnlineActorRecord housed) == true
            && housed.ProjSort is OnlineActorMirrorKind.World
            && ReferenceEquals(housed.WorldEntity, actor)
            && housed.IsSpatiallyProjected
            && housed.IsSpatiallyVisible
            && housed.WholeChamberIdent is not 0);
    }

    public ulong ProjVer(RealmActor actor)
    {
        return actor.ServerGuid is 0
            ? 0UL
            : _runtime.Current?.TryFetchRecord(actor.ServerGuid, out OnlineActorRecord housed) == true
              && ReferenceEquals(housed.WorldEntity, actor)
                ? housed.ProjAlterationVer
                : ulong.MaxValue;
    }
}
