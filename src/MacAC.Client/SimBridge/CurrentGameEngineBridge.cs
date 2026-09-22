using MacAC.Client.Dealing;
using MacAC.Mechanics.Genesis;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineBridge
    : ISimCoreLens,
      ISimCoreDirectives,
      ISimEventFeed,
      IDisposable
{
    private readonly SimCore _runtime;

    private readonly CurrentGameEngineDirectiveBridge _commands;

    private readonly IDirectiveBus _directiveBus;

    private readonly ToonPickingMirror _toonPick;

    private readonly ToonCreationMirror _toonCreation;

    private readonly IDisposable _hubTenancy;

    private readonly object _subscriptionLatch = new();

    private readonly HashSet<BridgeSubscription> _subscriptions = [];

    private bool _destroyed;

    public CurrentGameEngineBridge(
        SimCore runtime,
        OnlineSessionHarbor sessHub,
        IDirectiveBus directives,
        PickingDealingDriver pick)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        ArgumentNullException.ThrowIfNull(sessHub);
        ArgumentNullException.ThrowIfNull(directives);
        ArgumentNullException.ThrowIfNull(pick);

        _directiveBus = directives;
        _hubTenancy = runtime.ObtainHubTenancy(
            "graphical game-runtime command adapter");
        try
        {
            _toonPick = new ToonPickingMirror(this);
            _toonCreation = new ToonCreationMirror(this);
            _commands = new CurrentGameEngineDirectiveBridge(
                runtime.Session,
                sessHub,
                directives,
                this,
                runtime.SatchelHolder,
                runtime.ToonHolder,
                runtime.ActHolder,
                runtime.MovementOwner,
                runtime.FellowshipHolder,
                pick,
                runtime.SignalDrain);
        }
        catch
        {
            _hubTenancy.Dispose();
            throw;
        }
    }

    private sealed class ToonPickingMirror(
        CurrentGameEngineBridge holder)
        : ISimToonPickLens,
          ISimToonPickDirectives
    {
        public SimToonPickCapture Snapshot =>
            holder.ToonPickCapture();

        public bool TryFetchAt(
            int readoutOrdinal,
            out SimToonPickEntry toon) =>
            holder.TryFetchToonPickAt(readoutOrdinal, out toon);

        public bool TryGet(
            uint toonIdent,
            out SimToonPickEntry toon) =>
            holder.TryFetchToonPick(toonIdent, out toon);

        public void Call(ISimToonPickVisitor visitor) =>
            holder.TourToonPick(visitor);

        public IDisposable Subscribe(
            ISimToonPickWatcher watcher) =>
            holder.EnlistToonPick(watcher);

        public SimDirectiveResult Highlight(
            SimEpochTicket anticipatedGen,
            uint toonIdent)
        {
            return holder.PerformToonPick(
                directives => directives.Highlight(
                    anticipatedGen,
                    toonIdent));
        }

        public SimDirectiveResult Enter(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonPick(
                directives => directives.Enter(anticipatedGen));
        }

        public SimDirectiveResult ReqErase(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonPick(
                directives => directives.ReqErase(anticipatedGen));
        }

        public SimDirectiveResult ConfirmErase(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonPick(
                directives => directives.ConfirmErase(anticipatedGen));
        }

        public SimDirectiveResult Restore(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonPick(
                directives => directives.Restore(anticipatedGen));
        }

        public SimDirectiveResult Cancel(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonPick(
                directives => directives.Cancel(anticipatedGen));
        }
    }

    private sealed class BridgeToonPickingWatcher(
        CurrentGameEngineBridge holder,
        ISimToonPickWatcher watcher)
        : ISimToonPickWatcher
    {
        public void OnToonPickAltered(
            in SimToonPickDiff diff) =>
            holder.AheadToonPick(watcher, in diff);
    }

    private sealed class ToonCreationMirror(
        CurrentGameEngineBridge holder)
        : ISimToonGenesisLens,
          ISimToonGenesisDirectives
    {
        public SimToonGenesisCapture Snapshot =>
            holder.ToonCreationCapture();

        public GenesisSkillTrack GetSkillLevel(uint aptitudeIdent) =>
            holder.ToonCreationAptitudeTier(aptitudeIdent);

        public GenesisOptions Options => holder.ToonCreationKnobs();

        public IDisposable Subscribe(ISimToonGenesisWatcher watcher) =>
            holder.EnlistToonCreation(watcher);

        public SimDirectiveResult PickLineage(
            SimEpochTicket anticipatedGen,
            uint lineageIdent)
        {
            return holder.PerformToonCreation(
                directives => directives.PickLineage(anticipatedGen, lineageIdent));
        }

        public SimDirectiveResult PickGender(
            SimEpochTicket anticipatedGen,
            uint genderTag)
        {
            return holder.PerformToonCreation(
                directives => directives.PickGender(anticipatedGen, genderTag));
        }

        public SimDirectiveResult PickBlueprint(
            SimEpochTicket anticipatedGen,
            uint blueprintOrdinal)
        {
            return holder.PerformToonCreation(
                directives => directives.PickBlueprint(anticipatedGen, blueprintOrdinal));
        }

        public SimDirectiveResult AssignAttr(
            SimEpochTicket anticipatedGen,
            GenesisTraitId attrIdent,
            int val)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignAttr(anticipatedGen, attrIdent, val));
        }

        public SimDirectiveResult AssignAttrLock(
            SimEpochTicket anticipatedGen,
            GenesisTraitId attrIdent,
            bool bolted)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignAttrLock(anticipatedGen, attrIdent, bolted));
        }

        public SimDirectiveResult TrainSkill(
            SimEpochTicket anticipatedGen,
            uint aptitudeIdent)
        {
            return holder.PerformToonCreation(
                directives => directives.TrainSkill(anticipatedGen, aptitudeIdent));
        }

        public SimDirectiveResult SpecializeAptitude(
            SimEpochTicket anticipatedGen,
            uint aptitudeIdent)
        {
            return holder.PerformToonCreation(
                directives => directives.SpecializeAptitude(anticipatedGen, aptitudeIdent));
        }

        public SimDirectiveResult UntrainAptitude(
            SimEpochTicket anticipatedGen,
            uint aptitudeIdent)
        {
            return holder.PerformToonCreation(
                directives => directives.UntrainAptitude(anticipatedGen, aptitudeIdent));
        }

        public SimDirectiveResult AssignLooksOrdinal(
            SimEpochTicket anticipatedGen,
            GenesisAppearanceSlot socket,
            uint ordinal)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignLooksOrdinal(anticipatedGen, socket, ordinal));
        }

        public SimDirectiveResult AssignShade(
            SimEpochTicket anticipatedGen,
            GenesisShadeSlot socket,
            double val)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignShade(anticipatedGen, socket, val));
        }

        public SimDirectiveResult PickBeginArea(
            SimEpochTicket anticipatedGen,
            int beginAreaOrdinal)
        {
            return holder.PerformToonCreation(
                directives => directives.PickBeginArea(anticipatedGen, beginAreaOrdinal));
        }

        public SimDirectiveResult AssignLabel(
            SimEpochTicket anticipatedGen,
            string label)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignLabel(anticipatedGen, label));
        }

        public SimDirectiveResult AssignSocket(
            SimEpochTicket anticipatedGen,
            uint socket)
        {
            return holder.PerformToonCreation(
                directives => directives.AssignSocket(anticipatedGen, socket));
        }

        public SimDirectiveResult Finish(
            SimEpochTicket anticipatedGen,
            bool confirmUnspentCredits = false)
        {
            return holder.PerformToonCreation(
                directives => directives.Finish(anticipatedGen, confirmUnspentCredits));
        }

        public SimDirectiveResult AcknowledgeRejection(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonCreation(
                directives => directives.AcknowledgeRejection(anticipatedGen));
        }

        public SimDirectiveResult RandomizeToon(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonCreation(
                directives => directives.RandomizeToon(anticipatedGen));
        }

        public SimDirectiveResult RandomizeLooks(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonCreation(
                directives => directives.RandomizeLooks(anticipatedGen));
        }

        public SimDirectiveResult RandomizeClothing(
            SimEpochTicket anticipatedGen)
        {
            return holder.PerformToonCreation(
                directives => directives.RandomizeClothing(anticipatedGen));
        }
    }

    private sealed class BridgeToonCreationWatcher(
        CurrentGameEngineBridge holder,
        ISimToonGenesisWatcher watcher)
        : ISimToonGenesisWatcher
    {
        public void OnToonCreationAltered(
            in SimToonGenesisDiff diff) =>
            holder.AheadToonCreation(watcher, in diff);
    }

    private sealed class BridgeSubscription(
        CurrentGameEngineBridge holder,
        IDisposable coreSubscription) : IDisposable
    {
        private CurrentGameEngineBridge? _holder = holder;
        private IDisposable? _coreSubscription = coreSubscription;

        public void Dispose() => TeardownCore();

        public void TeardownCore()
        {
            var core = Interlocked.Exchange(
                ref _coreSubscription,
                null);
            if (core is null)
                return;
            core.Dispose();
            Interlocked.Exchange(ref _holder, null)?.Drop(this);
        }
    }
}
