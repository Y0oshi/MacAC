using MacAC.Mechanics.Realm;

namespace MacAC.Client.Realm;

public interface IOnlineActorRadarSource
{
    bool TryFetchMaterialized(uint srvOid, out RealmActor actor);
    bool TryFetchShown(uint srvOid, out RealmActor actor);
    void DuplicateShownTo(List<KeyValuePair<uint, RealmActor>> dest);
}
