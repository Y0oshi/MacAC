using MacAC.Mechanics.Genesis;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineBridge
{
    private bool IsEngaged
    {
        get
        {
            return !Volatile.Read(ref _destroyed)
        && !_runtime.Session.IsDisposalComplete;
        }
    }

    public SimEpochTicket Generation => _runtime.Generation;

    public SimLifespanCapture Lifecycle
    {
        get
        {
            var latest = _runtime.Lifecycle;
            return IsEngaged
                ? latest
                : latest with
                {
                    State = SimLifespanPhase.Disposed,
                    HasTransport = false,
                };
        }
    }

    public ISimCoreClock Clock => _runtime.Clock;

    public ISimActorLens Entities => _runtime.Entities;

    public ISimStashLens Inventory => _runtime.Inventory;

    public ISimStashStateLens InventoryState =>
        _runtime.InventoryState;

    public ISimStashStateDirectives SatchelDirectives => _commands;

    ISimStashStateDirectives ISimCoreDirectives.InventoryState =>
        _commands;

    public ISimToonLens Character => _runtime.Character;

    public ISimToonPickLens CharacterSelection =>
        _toonPick;

    public ISimToonGenesisLens CharacterCreation =>
        _toonCreation;

    public ISimToonPickDirectives ToonPickDirectives =>
        _toonPick;

    ISimToonPickDirectives ISimCoreDirectives.CharacterSelection =>
        _toonPick;

    public ISimToonGenesisDirectives ToonCreationDirectives =>
        _toonCreation;

    ISimToonGenesisDirectives ISimCoreDirectives.CharacterCreation =>
        _toonCreation;

    public ISimToonDirectives ToonDirectives => _commands;

    ISimToonDirectives ISimCoreDirectives.Character => _commands;

    public SimStateWaypoint GrabCheckpoint()
    {
        var checkpoint = _runtime.GrabCheckpoint();
        return checkpoint with { Lifecycle = Lifecycle.State };
    }

    public IDisposable Subscribe(ISimEventWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_subscriptionLatch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            IDisposable coreSubscription = _runtime.Subscribe(watcher);
            BridgeSubscription subscription = new BridgeSubscription(
                this,
                coreSubscription);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public void Dispose()
    {
        lock (_subscriptionLatch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            while (_subscriptions.Count is not 0)
                _subscriptions.First().TeardownCore();
        }
        _hubTenancy.Dispose();
    }

    internal bool SubmitCommsPhrase(string phrase)
    {
        if (!IsEngaged || string.IsNullOrWhiteSpace(phrase))
            return false;
        var verdict = CommsDirectiveRouter.Submit(
            phrase,
            new SimCommsDirectiveFeedback(_runtime.CommunicationHolder),
            _directiveBus,
            CommsChannelKind.Say);
        return verdict is not (SubmitUpshot.Empty
            or SubmitUpshot.UnknownCommand
            or SubmitUpshot.Dropped);
    }

    public ISimSocialLens Social => _runtime.Social;

    public ISimSocialDirectives SocialDirectives => _commands;

    ISimSocialDirectives ISimCoreDirectives.Social => _commands;

    public ISimCommsLens Chat => _runtime.Chat;

    public ISimCommsDirectives ChatCommands => _commands;

    ISimCommsDirectives ISimCoreDirectives.Chat => _commands;

    public ISimLinkLens Connection => _runtime.Connection;

    public ISimFellowsLens Fellowship => _runtime.Fellowship;

    public ISimFellowsDirectives FellowshipDirectives => _commands;

    ISimFellowsDirectives ISimCoreDirectives.Fellowship => _commands;

    public ISimAllegianceLens Allegiance => _runtime.Allegiance;

    public ISimAllegianceDirectives AllegianceDirectives => _commands;

    ISimAllegianceDirectives ISimCoreDirectives.Allegiance => _commands;

    public ISimActionLens Actions => _runtime.Actions;

    public ISimLocomotionLens Movement => _runtime.Movement;

    public ISimLocomotionDirectives MovementCommands => _commands;

    ISimLocomotionDirectives ISimCoreDirectives.Movement => _commands;

    public ISimRealmAmbienceLens Environment => _runtime.Environment;

    public ISimPortalLens Portal => _runtime.Portal;

    public ISimPortalDirectives GatewayDirectives => _commands;

    ISimPortalDirectives ISimCoreDirectives.Portal => _commands;

    public ISimSessionDirectives Session => _commands;

    public ISimSelectionDirectives Selection => _commands;

    public ISimFightingDirectives Combat => _commands;

    public ISimArcanaDirectives Magic => _commands;

    public ISimArcanabookDirectives GrimoireDirectives => _commands;

    ISimArcanabookDirectives ISimCoreDirectives.Spellbook => _commands;

    private SimToonPickCapture ToonPickCapture()
    {
        lock (_subscriptionLatch)
        {
            return IsEngaged
                ? _runtime.CharacterSelection.Snapshot
                : new SimToonPickCapture(
                _runtime.Generation,
                SimToonPickLifespan.Inactive,
                Revision: 0,
                AccountName: string.Empty,
                SlotCount: 0,
                RosterCount: 0,
                WorldName: string.Empty,
                HighlightedCharacterId: 0u,
                HighlightedDisplayIndex: -1,
                PendingDeleteCharacterId: 0u,
                LastRestoreRequestedCharacterId: 0u,
                Operation: SimToonPickOperation.None,
                Error: null,
                Buttons: SimToonPickButtons.None);
        }
    }

    private SimToonGenesisCapture ToonCreationCapture()
    {
        lock (_subscriptionLatch)
        {
            return IsEngaged ? _runtime.CharacterCreation.Snapshot : default;
        }
    }

    private GenesisSkillTrack ToonCreationAptitudeTier(uint aptitudeIdent)
    {
        lock (_subscriptionLatch)
        {
            return IsEngaged
                ? _runtime.CharacterCreation.GetSkillLevel(aptitudeIdent)
                : GenesisSkillTrack.Inactive;
        }
    }

    private GenesisOptions ToonCreationKnobs()
    {
        lock (_subscriptionLatch)
        {
            return IsEngaged
                ? _runtime.CharacterCreation.Options
                : GenesisOptions.Empty;
        }
    }

    private IDisposable EnlistToonPick(
        ISimToonPickWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_subscriptionLatch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            BridgeToonPickingWatcher gated = new BridgeToonPickingWatcher(this, watcher);
            IDisposable coreSubscription =
                _runtime.CharacterSelection.Subscribe(gated);
            BridgeSubscription subscription = new BridgeSubscription(
                this,
                coreSubscription);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    private IDisposable EnlistToonCreation(
        ISimToonGenesisWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_subscriptionLatch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            var gated = new BridgeToonCreationWatcher(this, watcher);
            IDisposable coreSubscription =
                _runtime.CharacterCreation.Subscribe(gated);
            BridgeSubscription subscription = new BridgeSubscription(
                this,
                coreSubscription);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    private void Drop(BridgeSubscription subscription)
    {
        lock (_subscriptionLatch)
            _subscriptions.Remove(subscription);
    }

    private bool TryFetchToonPickAt(
        int readoutOrdinal,
        out SimToonPickEntry toon)
    {
        lock (_subscriptionLatch)
        {
            if (IsEngaged)
            {
                return _runtime.CharacterSelection.TryFetchAt(
                    readoutOrdinal,
                    out toon);
            }
            toon = default;
            return false;
        }
    }

    private bool TryFetchToonPick(
        uint toonIdent,
        out SimToonPickEntry toon)
    {
        lock (_subscriptionLatch)
        {
            if (IsEngaged)
            {
                return _runtime.CharacterSelection.TryGet(
                    toonIdent,
                    out toon);
            }
            toon = default;
            return false;
        }
    }

    private void TourToonPick(
        ISimToonPickVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        lock (_subscriptionLatch)
        {
            if (IsEngaged)
                _runtime.CharacterSelection.Call(visitor);
        }
    }

    private SimDirectiveResult PerformToonPick(
        Func<ISimToonPickDirectives, SimDirectiveResult> perform)
    {
        lock (_subscriptionLatch)
        {
            return !IsEngaged
                ? new SimDirectiveResult(
                    SimDirectiveStatus.Inactive,
                    _runtime.Generation)
                : perform(_runtime.Session);
        }
    }

    private SimDirectiveResult PerformToonCreation(
        Func<ISimToonGenesisDirectives, SimDirectiveResult> perform)
    {
        lock (_subscriptionLatch)
        {
            return !IsEngaged
                ? new SimDirectiveResult(
                    SimDirectiveStatus.Inactive,
                    _runtime.Generation)
                : perform(_runtime.Session);
        }
    }

    private void AheadToonPick(
        ISimToonPickWatcher watcher,
        in SimToonPickDiff diff)
    {
        lock (_subscriptionLatch)
        {
            if (IsEngaged)
                watcher.OnToonPickAltered(in diff);
        }
    }

    private void AheadToonCreation(
        ISimToonGenesisWatcher watcher,
        in SimToonGenesisDiff diff)
    {
        lock (_subscriptionLatch)
        {
            if (IsEngaged)
                watcher.OnToonCreationAltered(in diff);
        }
    }
}
