using MacAC.Sim.Actors;
using MacAC.Sim.Play;
using MacAC.Sim.Presence;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

// Points during construction where a test may inject a failure to prove the rollback
internal enum SimCoreAssemblyPoint
{
    ClockCreated,
    SessionCreated,
    PlayerIdentityCreated,
    EntityObjectsCreated,
    InventoryCreated,
    CharacterCreated,
    CommunicationCreated,
    FellowshipCreated,
    AllegianceCreated,
    TradeCreated,
    ContractsCreated,
    JournalCreated,
    HouseCreated,
    MovementCreated,
    ActionsCreated,
    EnvironmentCreated,
    TransitCreated,
    EventsCreated,
}

// What has been built so far, visible to fault injection
internal sealed class SimCoreAssemblyContext
{
    public OnlineSessionDriver? Session { get; set; }
    public SimAvatarIdentityLedger? AvatarPersona { get; set; }
    public SimActorObjectLifetime? EntityObjects { get; set; }
    public SimStashLedger? Inventory { get; set; }
    public SimToonLedger? Character { get; set; }
    public SimCommsLedger? Communication { get; set; }
    public SimFellowsLedger? Fellowship { get; set; }
    public SimAllegianceLedger? Allegiance { get; set; }
    public SimBarterLedger? Trade { get; set; }
    public SimContractLedger? Contracts { get; set; }
    public SimDiaryLedger? Journal { get; set; }
    public SimDwellingLedger? House { get; set; }
    public SimAvatarLocomotionLedger? Movement { get; set; }
    public SimActionLedger? Actions { get; set; }
    public SimCoreEventExchange? Events { get; set; }
}

public sealed partial class SimCore
{
    internal SimCore(SimCoreDependencies dependencies, Action<SimCoreAssemblyPoint, SimCoreAssemblyContext>? flawInjection)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.CombatAttackOperations);
        ArgumentNullException.ThrowIfNull(dependencies.CombatTargetOperations);
        ArgumentNullException.ThrowIfNull(dependencies.CombatModeOperations);
        ArgumentNullException.ThrowIfNull(dependencies.SpellCastOperations);
        if (dependencies.MaximumChatEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(dependencies.MaximumChatEntries));

        SimCoreAssemblyContext ctx = new SimCoreAssemblyContext();
        AssemblyTransaction transaction = new AssemblyTransaction();
        void Reached(SimCoreAssemblyPoint pt) => flawInjection?.Invoke(pt, ctx);
        T Own<T>(T piece, SimCoreAssemblyPoint pt) where T : IDisposable
        {
            transaction.Own(piece);
            Reached(pt);
            return piece;
        }

        try
        {
            SimCoreClock timer = new SimCoreClock();
            Reached(SimCoreAssemblyPoint.ClockCreated);

            ctx.Session = Own(
                new OnlineSessionDriver(
                    dependencies.SessionOperations ?? ProductionOnlineSessionOps.Instance,
                    dependencies.TimeProvider,
                    random: dependencies.Random),
                SimCoreAssemblyPoint.SessionCreated);
            ctx.AvatarPersona = Own(new SimAvatarIdentityLedger(), SimCoreAssemblyPoint.PlayerIdentityCreated);
            ctx.EntityObjects = Own(
                new SimActorObjectLifetime(dependencies.FirstLocalEntityId, dependencies.TimeProvider, timer),
                SimCoreAssemblyPoint.EntityObjectsCreated);
            ctx.Inventory = Own(new SimStashLedger(ctx.EntityObjects), SimCoreAssemblyPoint.InventoryCreated);
            ctx.Character = Own(new SimToonLedger(momentSupplier: dependencies.TimeProvider), SimCoreAssemblyPoint.CharacterCreated);
            ctx.Communication = Own(new SimCommsLedger(dependencies.MaximumChatEntries), SimCoreAssemblyPoint.CommunicationCreated);
            ctx.Fellowship = Own(new SimFellowsLedger(momentSupplier: dependencies.TimeProvider), SimCoreAssemblyPoint.FellowshipCreated);
            ctx.Allegiance = Own(new SimAllegianceLedger(), SimCoreAssemblyPoint.AllegianceCreated);
            ctx.Trade = Own(new SimBarterLedger(ctx.EntityObjects!.Objects), SimCoreAssemblyPoint.TradeCreated);
            ctx.Contracts = Own(new SimContractLedger(), SimCoreAssemblyPoint.ContractsCreated);
            ctx.Journal = Own(new SimDiaryLedger(), SimCoreAssemblyPoint.JournalCreated);

            // The house ledger has nothing to dispose
            ctx.House = new SimDwellingLedger(ctx.EntityObjects.Objects);
            Reached(SimCoreAssemblyPoint.HouseCreated);

            var travel = new SimAvatarLocomotionLedger();
            travel.OnInterfaceText = (phrase, kind) => ctx.Communication.AddText(phrase, kind);
            ctx.Movement = Own(travel, SimCoreAssemblyPoint.MovementCreated);

            ctx.Actions = Own(
                new SimActionLedger(
                    ctx.Inventory.Transactions,
                    ctx.Character.Spellbook,
                    dependencies.CombatAttackOperations,
                    dependencies.CombatTargetOperations,
                    dependencies.CombatModeOperations,
                    dependencies.SpellCastOperations,
                    dependencies.CombatTime),
                SimCoreAssemblyPoint.ActionsCreated);

            SimRealmAmbienceLedger surroundings = new SimRealmAmbienceLedger(dependencies.TimeProvider, dependencies.Log, dependencies.TimeSyncDiagnostic);
            Reached(SimCoreAssemblyPoint.EnvironmentCreated);
            SimRealmCrossingLedger passage = new SimRealmCrossingLedger(dependencies.Log);
            Reached(SimCoreAssemblyPoint.TransitCreated);

            SimEpochReset genRestart = new SimEpochReset(
                passage,
                ctx.Communication,
                ctx.Inventory,
                ctx.Actions,
                ctx.Movement,
                ctx.EntityObjects,
                ctx.Character,
                ctx.AvatarPersona,
                ctx.Fellowship,
                ctx.Allegiance,
                ctx.Trade,
                ctx.House,
                ctx.Contracts,
                ctx.Journal);

            // Cross-wiring between parts that exist now
            travel.FastenKineticsBulletin(new SimAvatarKineticsPublicationLedger(
                ctx.EntityObjects.Entities,
                ctx.EntityObjects.Physics,
                travel,
                ctx.AvatarPersona));
            ctx.EntityObjects.OwnAvatarLeadListing.AttachBulletin(travel.KineticsBulletin);
            ctx.EntityObjects.AttachSignalCtx(
                () => genRestart.EngagedSunsettingGen ?? ctx.Session.Generation,
                () => timer.FrameNumber);
            ctx.EntityObjects.AttachOnlineFeeds(
                () => ctx.Character.UseLocusFromSrv,
                () => travel.Controller?.Position);

            ctx.Events = Own(
                new SimCoreEventExchange(ctx.EntityObjects, ctx.Communication, ctx.Actions),
                SimCoreAssemblyPoint.EventsCreated);

            Clock = timer;
            Session = ctx.Session;
            AvatarIdentity = ctx.AvatarPersona;
            EntityObjects = ctx.EntityObjects;
            SatchelHolder = ctx.Inventory;
            ToonHolder = ctx.Character;
            CommunicationHolder = ctx.Communication;
            FellowshipHolder = ctx.Fellowship;
            AllegianceHolder = ctx.Allegiance;
            BarterHolder = ctx.Trade;
            ContractsHolder = ctx.Contracts;
            JournalHolder = ctx.Journal;
            HouseOwner = ctx.House;
            MovementOwner = travel;
            ActHolder = ctx.Actions;
            SurroundingsHolder = surroundings;
            PassageHolder = passage;
            GenRestart = genRestart;
            _signals = ctx.Events;

            ctx.Session.ConfigureAutoPersistBeat(sess =>
            {
                if (ToonHolder.Options.IsDirty)
                    DrainToonKnobs(sess, ifAutoPersistDue: true);
            });
            ctx.Session.ConfigurePreLogoffDrain(sess =>
            {
                if (ToonHolder.Options.IsDirty)
                    DrainToonKnobs(sess, ifAutoPersistDue: false);
            });

            transaction.Complete();
        }
        catch (Exception miss)
        {
            transaction.UnwindAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    // Disposables built so far; cleared on completion, disposed in reverse on failure
    private sealed class AssemblyTransaction
    {
        private readonly List<IDisposable> _possessed = [];
        private bool _done;

        public void Own(IDisposable piece)
        {
            ArgumentNullException.ThrowIfNull(piece);
            if (_done)
                throw new InvalidOperationException("Runtime construction by now completed");
            _possessed.Add(piece);
        }

        public void Complete()
        {
            _done = true;
            _possessed.Clear();
        }

        public void UnwindAndThrow(Exception miss)
        {
            List<Exception> misses = new List<Exception> { miss };
            for (int idx = _possessed.Count - 1; idx >= 0; --idx)
            {
                try { _possessed[idx].Dispose(); }
                catch (Exception tidy) { misses.Add(tidy); }
            }
            _possessed.Clear();
            if (misses.Count is 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(miss).Throw();
            throw new AggregateException("SimCore construction and rollback both failed", misses);
        }
    }
}
