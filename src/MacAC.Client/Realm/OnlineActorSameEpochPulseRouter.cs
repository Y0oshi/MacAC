using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal interface IOnlineActorSameEpochPulseSink
{
    void OnDescription(uint holderOid, KineticSpawnData blurb);
    void OnAppearance(ObjDescNotice.Parsed looks);
    void OnParent(CreateAnchorUpdate ancestor);
    void OnPosition(RealmSession.MoverPositionUpdate locus);
    void OnPickup(PickupNotice.Parsed lift);
    void OnMovement(RealmSession.MoverMotionUpdate travel);
    void OnState(GroupPhase.Parsed phase);
    void OnVector(VelocityUpdate.Parsed vector);
}

internal static class OnlineActorSameEpochPulseRouter
{
    public static void Apply(
        SameEpochCreateObjectEvents renew,
        IOnlineActorSameEpochPulseSink drain)
    {
        ArgumentNullException.ThrowIfNull(drain);
        drain.OnDescription(renew.Appearance.Guid, renew.Description);
        drain.OnAppearance(renew.Appearance);

        if (renew.Parent is { } ancestor)
            drain.OnParent(ancestor);
        else if (renew.Position is { } locus)
            drain.OnPosition(locus);
        else if (renew.Pickup is { } lift)
            drain.OnPickup(lift);

        ApplyRest(drain, renew);
    }

    private static void ApplyRest(IOnlineActorSameEpochPulseSink drain, SameEpochCreateObjectEvents renew)
    {
        if (renew.Movement is { } travel)
            drain.OnMovement(travel);
        drain.OnState(renew.State);
        drain.OnVector(renew.Vector);
    }
}
