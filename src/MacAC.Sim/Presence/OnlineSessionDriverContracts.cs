using System.Net;
using System.Net.Sockets;
using MacAC.Mechanics.Genesis;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public enum OnlineSessionStartStatus
{
    Disabled,
    MissingCredentials,
    NoCharacters,
    Connected,
    Deferred,
    Failed,
    AwaitingCharacterSelection,
    ProbeComplete,
}

public readonly record struct OnlineSessionHoldingCapture(
    bool IsDisposed,
    bool IsDisposeRequested,
    bool IsInWorld,
    bool HasActiveSession,
    bool HasRetiredSession,
    bool HasPendingInitialReset,
    bool HasPendingOperation,
    int OperationDepth,
    ulong Generation,
    SimTeardownStage LastTeardownStages)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed
        && IsDisposeRequested
        && !IsInWorld
        && !HasActiveSession
        && !HasRetiredSession
        && !HasPendingInitialReset
        && !HasPendingOperation
        && OperationDepth is 0
        && LastTeardownStages == SimTeardownStage.Complete;
        }
    }
}

public sealed record OnlineSessionToonPick(
    int ActiveIndex,
    uint CharacterId,
    string CharacterName,
    string AccountName);

public sealed record OnlineSessionStartResult(
    OnlineSessionStartStatus Status,
    OnlineSessionToonPick? Selection = null,
    Exception? Error = null);

public readonly record struct OnlineSessionRosterEntry(
    uint Id,
    string Name,
    uint SecondsGreyedOut);

public sealed record OnlineSessionRosterNotice(
    string AccountName,
    int SlotCount,
    IReadOnlyList<OnlineSessionRosterEntry> Entries);

public interface IOnlineSessionLifespanHarbor
{
    OnlineSessionBinding AttachSess(RealmSession sess);
    void RewindSessPhase(SimEpochTicket sunsettingGen);
    void AnnounceConnecting(string hub, int port, string user);
    void AnnounceConnected();
    void AnnounceLineup(OnlineSessionRosterNotice lineup);
    void ImposeChosenToon(OnlineSessionToonPick pick);
    void ImposeEnteredRealm(OnlineSessionToonPick pick);
    void UnfastenSess(RealmSession sess);

    void ImposeToonBuilt(SimToonGenesisIdentity persona) { }

    void ImposeCreationFailed(SimToonGenesisRejection rejection) { }
}

public sealed class OnlineSessionBinding : IDisposable
{
    private readonly Action _engageDirectives;
    private readonly Action _disengageDirectives;
    private readonly Action _unfastenSignals;
    private bool _directivesDeactivated;
    private bool _signalsDetached;
    private bool _directivesActivated;

    public OnlineSessionBinding(
        RealmSession session,
        Action activateCommands,
        Action deactivateCommands,
        Action detachEvents)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _engageDirectives = activateCommands ?? throw new ArgumentNullException(nameof(activateCommands));
        _disengageDirectives = deactivateCommands ?? throw new ArgumentNullException(nameof(deactivateCommands));
        _unfastenSignals = detachEvents ?? throw new ArgumentNullException(nameof(detachEvents));
    }

    public RealmSession Session { get; }
    public bool DirectivesDeactivated => _directivesDeactivated;
    public bool SignalsDetached => _signalsDetached;

    public void EngageDirectives()
    {
        if (_directivesDeactivated || _signalsDetached)
            throw new ObjectDisposedException(nameof(OnlineSessionBinding));
        if (_directivesActivated)
            return;
        _engageDirectives();
        if (_directivesDeactivated || _signalsDetached)
            return;
        _directivesActivated = true;
    }

    public void Dispose()
    {
        if (!_directivesDeactivated)
        {
            _disengageDirectives();
            _directivesDeactivated = true;
        }
        if (!_signalsDetached)
        {
            _unfastenSignals();
            _signalsDetached = true;
        }
    }
}

public interface IOnlineSessionOps
{
    IPEndPoint LocateEndpoint(string hub, int port);
    RealmSession BuildSess(IPEndPoint endpoint);
    void Connect(RealmSession sess, string user, string password);
    void CommenceLink(RealmSession sess, string user, string password) =>
        sess.CommenceLink(user, password);
    bool SampleLink(RealmSession sess) => sess.SampleLink();
    ToonRoster.ParsedUnit? FetchToons(RealmSession sess);
    RealmName.Parsed? FetchSrvDetails(RealmSession sess) => sess.SrvDetails;
    void BeginToonPickTake(RealmSession sess) =>
        sess.LaunchToonPickTake();
    void EnterWorld(RealmSession sess, int engagedToonOrdinal);

    void JoinRealmByOid(
        RealmSession sess,
        uint toonOid,
        string acctLabel) =>
        sess.EnterWorld(toonOid, acctLabel);

    void EraseToon(
        RealmSession sess,
        string acctLabel,
        int engagedToonOrdinal) =>
        sess.TransmitEraseToon(acctLabel, engagedToonOrdinal);
    void ReinstateToon(RealmSession sess, uint toonIdent) =>
        sess.TransmitRevertToon(toonIdent);
    void BuildToon(
        RealmSession sess,
        string acctLabel,
        CharacterForge.WireRequest req,
        ReadOnlySpan<uint> aptitudeAdvancementClasses) =>
        sess.TransmitToonCreation(acctLabel, req, aptitudeAdvancementClasses);
    void Tick(RealmSession sess);
    void TeardownSess(RealmSession sess);

    void ReqToonLogOff(RealmSession sess) =>
        sess.RequestCharacterLogOff();

    void YieldToCharacterSelect(RealmSession sess) =>
        sess.YieldToCharacterSelect();
}

internal sealed class ProductionOnlineSessionOps : IOnlineSessionOps
{
    public static ProductionOnlineSessionOps Instance { get; } = new();

    private ProductionOnlineSessionOps() { }

    public IPEndPoint LocateEndpoint(string hub, int port)
    {
        IPAddress ip;
        if (!IPAddress.TryParse(hub, out ip!))
        {
            IPAddress[] addresses = Dns.GetHostAddresses(hub);
            ip = Array.Find(
                    addresses,
                    static address => address.AddressFamily == AddressFamily.InterNetwork)
                ?? (addresses.Length is not 0
                    ? addresses[0]
                    : throw new InvalidOperationException(
                        $"DNS resolved no addresses for '{hub}'"));
            Console.WriteLine($"live: resolved {hub} → {ip}");
        }
        return new IPEndPoint(ip, port);
    }

    public RealmSession BuildSess(IPEndPoint endpoint) => new(endpoint);

    public void Connect(RealmSession sess, string user, string password) =>
        sess.Connect(user, password);

    public ToonRoster.ParsedUnit? FetchToons(RealmSession sess) => sess.Characters;

    public void BeginToonPickTake(RealmSession sess) =>
        sess.LaunchToonPickTake();

    public void EnterWorld(RealmSession sess, int engagedToonOrdinal) =>
        sess.EnterWorld(engagedToonOrdinal);

    public void Tick(RealmSession sess) => sess.Tick();

    public void TeardownSess(RealmSession sess) => sess.Dispose();
}
