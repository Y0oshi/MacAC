using MacAC.Mechanics.Realm;

namespace MacAC.Client.Realm;

public interface IOnlineActorSpatialProbe
{
    void DuplicateOnlineActorsNearbyLb(
        uint middleChamberOrLbIdent,
        int lbRadius,
        List<KeyValuePair<uint, RealmActor>> dest);
}
