using System.Runtime.ExceptionServices;
using MacAC.Wire;
using MacAC.Sim.Comms;

namespace MacAC.Sim.Presence;

public sealed record OnlineSessionRoutingFactories(
    Func<RealmSession, IOnlineSessionEventRouting> CreateEvents,
    Func<RealmSession, IOnlineSessionDirectiveRouting> CreateCommands);

public sealed record OnlineSessionSelectionWiring(
    Action<uint> SetPlayerIdentity,
    Action<uint> SetVitalsIdentity,
    Action<uint> SetChatIdentity,
    Action<uint> MarkPersistent,
    Action<uint> SetVanishProbeIdentity,
    Action ClearCombat,
    Action? ArmLoginTunnel = null);

public sealed record OnlineSessionEnteredRealmWiring(
    Action<string> SetActiveCharacter,
    Action RestoreLayout,
    Action SyncToolbar,
    Action<string> LoadCharacterSettings,
    Action ArmPlayerModeAutoEntry,
    Action? ResumeWorldAudio = null);

public sealed record OnlineSessionHarborWiring(
    OnlineSessionRoutingFactories Routing,
    Action<SimEpochTicket> Reset,
    OnlineSessionSelectionWiring Selection,
    OnlineSessionEnteredRealmWiring EnteredWorld,
    Action<string, int, string> Connecting,
    Action Connected,
    Action<OnlineSessionRosterNotice> Roster,
    Action<OnlineSessionToonPick> CharacterEntered,
    LoginDirectiveSequence? LoginCommands = null,
    Action<SimToonGenesisIdentity>? CharacterCreated = null,
    Action<SimToonGenesisRejection>? CreationFailed = null);

public sealed class OnlineSessionHarbor : ISimSessionDirectives, ISimOnlineSessionFramePhase
{
    // Routes from a failed bind that still need disposing; retried on the next bind or reset
    private sealed class RouteRollback(IOnlineSessionDirectiveRouting? directives, IOnlineSessionEventRouting? signals)
    {
        private IOnlineSessionDirectiveRouting? _commands = directives;
        private IOnlineSessionEventRouting? _signals = signals;

        public bool IsComplete => _commands is null && _signals is null;

        public void Drain()
        {
            List<Exception>? misses = null;
            Release(ref _commands, ref misses);
            Release(ref _signals, ref misses);
            if (misses is not null)
                throw new AggregateException("Live-session route rollback didn't converge", misses);
        }

        private static void Release<T>(ref T? course, ref List<Exception>? misses) where T : class, IDisposable
        {
            if (course is null)
                return;
            try
            {
                course.Dispose();
                course = null;
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
    }

    private readonly OnlineSessionDriver _driver;
    private readonly OnlineSessionConnectOptions? _linkKnobs;
    private readonly OnlineSessionRoutingFactories _routing;
    private readonly OnlineSessionSelectionWiring _pick;
    private readonly OnlineSessionEnteredRealmWiring _entered;
    private readonly Action<OnlineSessionToonPick> _toonEntered;
    private readonly Action<SimEpochTicket> _restart;
    private readonly OnlineSessionLifespanHarbor _lifespan;
    private readonly LoginDirectiveSequence? _signinDirectives;
    private RouteRollback? _undo;

    public OnlineSessionHarbor(OnlineSessionDriver controller, OnlineSessionHarborWiring bindings, OnlineSessionConnectOptions? linkKnobs = null)
    {
        _driver = controller ?? throw new ArgumentNullException(nameof(controller));
        _linkKnobs = linkKnobs;
        ArgumentNullException.ThrowIfNull(bindings);
        _routing = bindings.Routing ?? throw new ArgumentNullException(nameof(bindings.Routing));
        _pick = bindings.Selection ?? throw new ArgumentNullException(nameof(bindings.Selection));
        _entered = bindings.EnteredWorld ?? throw new ArgumentNullException(nameof(bindings.EnteredWorld));
        _toonEntered = bindings.CharacterEntered ?? throw new ArgumentNullException(nameof(bindings.CharacterEntered));
        _signinDirectives = bindings.LoginCommands;
        ArgumentNullException.ThrowIfNull(_routing.CreateEvents);
        ArgumentNullException.ThrowIfNull(_routing.CreateCommands);
        ArgumentNullException.ThrowIfNull(bindings.Reset);
        ArgumentNullException.ThrowIfNull(bindings.Connecting);
        ArgumentNullException.ThrowIfNull(bindings.Connected);
        ArgumentNullException.ThrowIfNull(bindings.Roster);
        DemandDone(_pick, _entered);

        _restart = bindings.Reset;
        _lifespan = new OnlineSessionLifespanHarbor(new OnlineSessionLifespanWiring(
            Bind: AttachSess,
            Reset: RestartSessPhase,
            Connecting: bindings.Connecting,
            Connected: bindings.Connected,
            Roster: bindings.Roster,
            Selected: ApplySelection,
            Entered: ImposeEnteredRealm,
            CharacterCreated: bindings.CharacterCreated,
            CreationFailed: bindings.CreationFailed));
    }

    public RealmSession? CurrentSess => _driver.LatestSession;
    public ISimLinkLens Connection => _driver.Connection;
    public bool IsInWorld => _driver.IsInWorld;

    public OnlineSessionStartResult Start(OnlineSessionConnectOptions knobs) => _driver.Start(knobs, _lifespan);

    public OnlineSessionStartResult Reconnect(OnlineSessionConnectOptions knobs) => _driver.Reconnect(knobs, _lifespan);

    public SimSessionStartResult Start(SimEpochTicket anticipatedGen) => Gated(anticipatedGen, _driver.Start);

    public SimSessionStartResult Reconnect(SimEpochTicket anticipatedGen) => Gated(anticipatedGen, _driver.Reconnect);

    public SimTeardownAck Stop(SimEpochTicket anticipatedGen) => _driver.Stop(anticipatedGen);

    public void Tick()
    {
        _driver.Tick();
        _signinDirectives?.Tick(_driver.Generation, _driver.IsInWorld);
    }

    // Runs a driver start against the configured options once the caller's generation is confirmed
    // live
    private SimSessionStartResult Gated(SimEpochTicket anticipated, Func<OnlineSessionConnectOptions, IOnlineSessionLifespanHarbor, OnlineSessionStartResult> begin)
    {
        var latest = _driver.Generation;
        if (anticipated != latest)
            return new SimSessionStartResult(SimSessionStartStatus.StaleGeneration, latest);
        if (_linkKnobs is null)
            return new SimSessionStartResult(SimSessionStartStatus.Inactive, latest);
        return Adapt(begin(_linkKnobs, _lifespan));
    }

    private OnlineSessionBinding AttachSess(RealmSession sess)
    {
        EmptyUndo();

        IOnlineSessionEventRouting? signals = null;
        IOnlineSessionDirectiveRouting? directives = null;
        try
        {
            signals = _routing.CreateEvents(sess) ?? throw new InvalidOperationException("The live-session event factory returned null");
            signals.Attach();
            directives = _routing.CreateCommands(sess) ?? throw new InvalidOperationException("The live-session command factory returned null");
            return new OnlineSessionBinding(sess, activateCommands: directives.Arm, deactivateCommands: directives.Dispose, detachEvents: signals.Dispose);
        }
        catch (Exception creationProblem)
        {
            RollBackThenRethrow(creationProblem, directives, signals);
            throw;
        }
    }

    private void RestartSessPhase(SimEpochTicket sunsettingGen)
    {
        EmptyUndo();
        _signinDirectives?.Cancel(sunsettingGen);
        _restart(sunsettingGen);
    }

    private void ApplySelection(OnlineSessionToonPick pick)
    {
        uint ident = pick.CharacterId;
        _pick.SetPlayerIdentity(ident);
        _pick.SetVitalsIdentity(ident);
        _pick.SetChatIdentity(ident);
        _pick.MarkPersistent(ident);
        _pick.SetVanishProbeIdentity(ident);
        _pick.ClearCombat();
        _pick.ArmLoginTunnel?.Invoke();
    }

    private void ImposeEnteredRealm(OnlineSessionToonPick pick)
    {
        string label = pick.CharacterName;
        _entered.ResumeWorldAudio?.Invoke();
        _entered.SetActiveCharacter(label);
        _entered.RestoreLayout();
        _entered.SyncToolbar();
        _entered.LoadCharacterSettings(label);
        _entered.ArmPlayerModeAutoEntry();
        _toonEntered(pick);
        _signinDirectives?.EnteredRealm(_driver.Generation);
    }

    private void RollBackThenRethrow(Exception creationProblem, IOnlineSessionDirectiveRouting? directives, IOnlineSessionEventRouting? signals)
    {
        _undo = new RouteRollback(directives, signals);
        try
        {
            EmptyUndo();
        }
        catch (AggregateException tidyProblem)
        {
            throw new AggregateException("Live-session route construction and rollback both failed", [creationProblem, .. tidyProblem.InnerExceptions]);
        }
        ExceptionDispatchInfo.Capture(creationProblem).Throw();
    }

    private void EmptyUndo()
    {
        if (_undo is not { } undo)
            return;
        undo.Drain();
        if (undo.IsComplete)
            _undo = null;
    }

    private static void DemandDone(OnlineSessionSelectionWiring pick, OnlineSessionEnteredRealmWiring entered)
    {
        ArgumentNullException.ThrowIfNull(pick.SetPlayerIdentity);
        ArgumentNullException.ThrowIfNull(pick.SetVitalsIdentity);
        ArgumentNullException.ThrowIfNull(pick.SetChatIdentity);
        ArgumentNullException.ThrowIfNull(pick.MarkPersistent);
        DemandDoneRest(pick, entered);
    }

    private static void DemandDoneRest(OnlineSessionSelectionWiring pick, OnlineSessionEnteredRealmWiring entered)
    {
        ArgumentNullException.ThrowIfNull(pick.SetVanishProbeIdentity);
        ArgumentNullException.ThrowIfNull(pick.ClearCombat);
        DemandDoneChecks(entered);
    }

    private static void DemandDoneChecks(OnlineSessionEnteredRealmWiring entered)
    {
        ArgumentNullException.ThrowIfNull(entered.SetActiveCharacter);
        ArgumentNullException.ThrowIfNull(entered.RestoreLayout);
        ArgumentNullException.ThrowIfNull(entered.SyncToolbar);
        ArgumentNullException.ThrowIfNull(entered.LoadCharacterSettings);
        ArgumentNullException.ThrowIfNull(entered.ArmPlayerModeAutoEntry);
    }

    private SimSessionStartResult Adapt(OnlineSessionStartResult result)
    {
        SimSessionStartStatus condition = result.Status switch
        {
            OnlineSessionStartStatus.Disabled => SimSessionStartStatus.Disabled,
            OnlineSessionStartStatus.MissingCredentials => SimSessionStartStatus.MissingCredentials,
            OnlineSessionStartStatus.NoCharacters => SimSessionStartStatus.NoCharacters,
            OnlineSessionStartStatus.Connected => SimSessionStartStatus.Connected,
            OnlineSessionStartStatus.Deferred => SimSessionStartStatus.Deferred,
            OnlineSessionStartStatus.Failed => SimSessionStartStatus.Failed,
            OnlineSessionStartStatus.ProbeComplete => SimSessionStartStatus.ProbeComplete,
            OnlineSessionStartStatus.AwaitingCharacterSelection => SimSessionStartStatus.AwaitingCharacterSelection,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unrecognized live-session start result"),
        };
        return new SimSessionStartResult(
            condition, _driver.Generation, result.Selection?.CharacterId ?? 0u, result.Selection?.CharacterName ?? string.Empty, result.Error);
    }
}
