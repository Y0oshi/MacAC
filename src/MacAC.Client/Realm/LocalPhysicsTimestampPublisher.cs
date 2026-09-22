using MacAC.Client.Controls;
using MacAC.Client.Link;
using MacAC.Sim.Actors;

namespace MacAC.Client.Realm;

internal interface IAcceptedLocalKineticsTimestampHerald
{
    void Publish(uint srvOid, GrantedKineticsTimestamps timestamps);
}

internal sealed class OnlineSessionLocalKineticsTimestampHerald(
    IAvatarIdentitySource identity,
    IOnlineRealmSessionSource session)
        : IAcceptedLocalKineticsTimestampHerald
{
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly IOnlineRealmSessionSource _session = session ?? throw new ArgumentNullException(nameof(session));

    public void Publish(uint srvOid, GrantedKineticsTimestamps timestamps)
    {
        if (srvOid != _identity.SrvOid
            || _session.LatestSess is not { } sess)

            return;

        sess.BroadcastApprovedOwnKineticsTimestamps(
            timestamps.Instance,
            timestamps.ServerControlledMove,
            timestamps.Teleport,
            timestamps.ForcePosition);
    }
}
