using MacAC.Wire;

namespace MacAC.Sim.Presence;

/// <summary>The callbacks a host supplies for each moment in a session's life.</summary>
public sealed record OnlineSessionLifespanWiring(
    Func<RealmSession, OnlineSessionBinding> Bind,
    Action<SimEpochTicket> Reset,
    Action<string, int, string> Connecting,
    Action Connected,
    Action<OnlineSessionRosterNotice> Roster,
    Action<OnlineSessionToonPick> Selected,
    Action<OnlineSessionToonPick> Entered,
    Action<SimToonGenesisIdentity>? CharacterCreated = null,
    Action<SimToonGenesisRejection>? CreationFailed = null);

public sealed class OnlineSessionLifespanHarbor : IOnlineSessionLifespanHarbor
{
    private readonly OnlineSessionLifespanWiring _wiring;
    private RealmSession? _tied;

    public OnlineSessionLifespanHarbor(OnlineSessionLifespanWiring bindings)
    {
        _wiring = bindings ?? throw new ArgumentNullException(nameof(bindings));
        ArgumentNullException.ThrowIfNull(bindings.Bind);
        ArgumentNullException.ThrowIfNull(bindings.Reset);
        ArgumentNullException.ThrowIfNull(bindings.Connecting);
        ArgumentNullException.ThrowIfNull(bindings.Connected);
        ArgumentNullException.ThrowIfNull(bindings.Roster);
        ArgumentNullException.ThrowIfNull(bindings.Selected);
        ArgumentNullException.ThrowIfNull(bindings.Entered);
    }

    public OnlineSessionBinding AttachSess(RealmSession sess)
    {
        ArgumentNullException.ThrowIfNull(sess);
        if (_tied is not null)
            throw new InvalidOperationException("A live session is by now attached to this host");
        var mapping = _wiring.Bind(sess);
        _tied = sess;
        return mapping;
    }

    public void UnfastenSess(RealmSession sess)
    {
        if (!ReferenceEquals(_tied, sess))
            throw new InvalidOperationException("The live-session controller attempted to detach a session that isn't bound");
        _tied = null;
    }

    public void RewindSessPhase(SimEpochTicket sunsettingGen) => _wiring.Reset(sunsettingGen);

    public void AnnounceConnecting(string hub, int port, string user) => _wiring.Connecting(hub, port, user);

    public void AnnounceConnected() => _wiring.Connected();

    public void AnnounceLineup(OnlineSessionRosterNotice lineup) => _wiring.Roster(lineup);

    public void ImposeChosenToon(OnlineSessionToonPick pick) => _wiring.Selected(pick);

    public void ImposeEnteredRealm(OnlineSessionToonPick pick) => _wiring.Entered(pick);

    public void ImposeToonBuilt(SimToonGenesisIdentity persona) => _wiring.CharacterCreated?.Invoke(persona);

    public void ImposeCreationFailed(SimToonGenesisRejection rejection) => _wiring.CreationFailed?.Invoke(rejection);
}
