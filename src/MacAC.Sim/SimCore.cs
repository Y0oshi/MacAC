using MacAC.Wire;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

public sealed record SimCoreDependencies(
    ISimFightingAttackOps CombatAttackOperations,
    ISimFightingTargetOps CombatTargetOperations,
    ISimFightingModeOps CombatModeOperations,
    ISimArcanaCastOps SpellCastOperations,
    TimeProvider? TimeProvider = null,
    Action<string>? Log = null,
    Action<string>? TimeSyncDiagnostic = null,
    IOnlineSessionOps? SessionOperations = null,
    Func<double>? CombatTime = null,
    uint FirstLocalEntityId = SimActorIndex.LeadOwnActorIdent,
    int MaximumChatEntries = 500,
    Random? Random = null);

public sealed partial class SimCore : ISimCoreLens, ISimEventFeed, IDisposable
{
    private readonly object _lifespanLatch = new();
    private readonly Dictionary<long, string> _hubTenancies = [];
    private readonly SimCoreEventExchange _signals;
    private long _upcomingHubTenancyIdent;

    public SimCore(SimCoreDependencies deps)
        : this(deps, flawInjection: null)
    {
    }

    public SimCoreClock Clock { get; }
    public OnlineSessionDriver Session { get; }
    public SimAvatarIdentityLedger AvatarIdentity { get; }
    public SimActorObjectLifetime EntityObjects { get; }
    public SimStashLedger SatchelHolder { get; }
    public SimToonLedger ToonHolder { get; }
    public SimCommsLedger CommunicationHolder { get; }
    public SimFellowsLedger FellowshipHolder { get; }
    public SimAllegianceLedger AllegianceHolder { get; }
    public SimBarterLedger BarterHolder { get; }
    public SimContractLedger ContractsHolder { get; }
    public SimDiaryLedger JournalHolder { get; }
    public SimDwellingLedger HouseOwner { get; }
    public SimActionLedger ActHolder { get; }
    public SimAvatarLocomotionLedger MovementOwner { get; }
    public SimRealmAmbienceLedger SurroundingsHolder { get; }
    public SimRealmCrossingLedger PassageHolder { get; }
    public SimEpochReset GenRestart { get; }

    internal SimAvatarKineticsPublicationLedger OwnAvatarKineticsBulletin => MovementOwner.KineticsBulletin;
    internal ISimCoreEventSink SignalDrain => _signals;

    public SimPlacementMirrorChannel Placements => EntityObjects.Placements;
    public SimEpochTicket Generation => Session.Generation;

    ISimCoreClock ISimCoreLens.Clock => Clock;
    public ISimActorLens Entities => EntityObjects.ActorLens;
    public ISimStashLens Inventory => EntityObjects.StashLens;
    public ISimStashStateLens InventoryState => SatchelHolder.View;
    public ISimToonLens Character => ToonHolder.View;
    public ISimSocialLens Social => CommunicationHolder.SocialView;
    public ISimCommsLens Chat => CommunicationHolder.View;
    public ISimToonPickLens CharacterSelection => Session.ToonSelection;
    public ISimLinkLens Connection => Session.Connection;
    public ISimToonGenesisLens CharacterCreation => Session.CharacterCreation;
    public ISimFellowsLens Fellowship => FellowshipHolder.View;
    public ISimAllegianceLens Allegiance => AllegianceHolder.View;
    public ISimBarterLens Trade => BarterHolder.View;
    public ISimContractLens Contracts => ContractsHolder.View;
    public ISimDiaryLens Journal => JournalHolder.View;
    public ISimActionLens Actions => ActHolder.View;
    public ISimLocomotionLens Movement => MovementOwner.View;
    public ISimRealmAmbienceLens Environment => SurroundingsHolder;
    public ISimPortalLens Portal => PassageHolder;

    /// <summary>Where the session is in its life, derived rather than tracked.</summary>
    public SimLifespanCapture Lifecycle
    {
        get
        {
            SimLifespanPhase stage =
                _destroyed ? SimLifespanPhase.Disposed
                : Session.IsInWorld ? SimLifespanPhase.InWorld
                : Session.LatestSession is not null ? SimLifespanPhase.Starting
                : Session.SessGen == 0UL ? SimLifespanPhase.Constructed
                : SimLifespanPhase.Stopped;
            return new SimLifespanCapture(Generation, stage, AvatarIdentity.ServerGuid, Session.LatestSession is not null);
        }
    }

    public SimStateWaypoint GrabCheckpoint() => new(
        Generation,
        Lifecycle.State,
        Clock.FrameNumber,
        Entities.Count,
        Entities.MaterializedCount,
        Inventory.ObjectTally,
        Inventory.VesselCount,
        InventoryState.Snapshot,
        Character.Snapshot,
        Social.Snapshot,
        Chat.Revision,
        Chat.Count,
        Actions.Snapshot,
        Movement.Snapshot,
        Environment.Snapshot,
        Environment.Ownership,
        Portal.Snapshot,
        Portal.Ownership,
        Fellowship.Snapshot,
        Allegiance.Snapshot);

    public SimAvatarFrameDriver BuildOwnAvatarCycleDriver(ISimAvatarFrameHarbor hub, ISimLocomotionInputSource feed)
    {
        ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);
        return new SimAvatarFrameDriver(hub, feed, () =>
        {
            _signals.WriteTravel(MovementOwner.Snapshot);
            SimMerchantRangeProbe.EnforceSpan(this);
        });
    }

    public void RestartGen(SimEpochTicket sunsettingGen, ISimEpochResetHarbor hub) =>
        GenRestart.Reset(sunsettingGen, hub);

    public IDisposable Subscribe(ISimEventWatcher watcher)
    {
        lock (_lifespanLatch)
        {
            ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);
            return _signals.Subscribe(watcher);
        }
    }

    public IDisposable ObtainHubTenancy(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        lock (_lifespanLatch)
        {
            ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);
            long ident = checked(++_upcomingHubTenancyIdent);
            _hubTenancies.Add(ident, label);
            return new HarborLease(this, ident);
        }
    }

    private int HubTenancyTally
    {
        get
        {
            lock (_lifespanLatch)
                return _hubTenancies.Count;
        }
    }

    private void FreeHubTenancy(long ident)
    {
        lock (_lifespanLatch)
            _hubTenancies.Remove(ident);
    }

    private sealed class HarborLease(SimCore holder, long ident) : IDisposable
    {
        private SimCore? _holder = holder;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.FreeHubTenancy(ident);
    }

    public SimCoreHoldingCapture CaptureOwnership()
    {
        lock (_lifespanLatch)
        {
            return new SimCoreHoldingCapture(
                _teardownAsked,
                _teardownEmptyEngaged,
                _destroyed,
                _hubTenancies.Count,
                CompletedTeardownStages,
                Session.CaptureOwnership(),
                AvatarIdentity.CaptureOwnership(),
                SimSimulationHolding.Capture(
                    EntityObjects, SatchelHolder, ToonHolder, CommunicationHolder,
                    ActHolder, MovementOwner, FellowshipHolder, AllegianceHolder, BarterHolder),
                SurroundingsHolder.CaptureOwnership(),
                PassageHolder.CaptureOwnership(),
                GenRestart.CaptureSnapshot(),
                _signals.SnapOwnership());
        }
    }

    private void DrainToonKnobs(RealmSession sess, bool ifAutoPersistDue)
    {
        SimToonOptionsLedger knobs = ToonHolder.Options;
        if (!knobs.IsDirty)
            return;

        void TransmitBlob()
        {
            ToonOptionsBlobEcho echo = ToonOptionsBlobSource.Capture(ToonHolder, SatchelHolder.Shortcuts);
            sess.TransmitSetToonKnobs(
                echo.Options1, echo.Options2, echo.Shortcuts, echo.FavoriteSpells, echo.DesiredComponents, echo.SpellbookFilters);
        }

        if (ifAutoPersistDue)
            knobs.TryDrainIfAutoPersistDue(TransmitBlob);
        else
            knobs.TryDrain(TransmitBlob);
    }
}
