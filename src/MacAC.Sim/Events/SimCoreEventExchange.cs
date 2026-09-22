using MacAC.Sim.Actors;
using MacAC.Sim.Play;

namespace MacAC.Sim;

public readonly record struct SimCoreEventHoldingCapture(
    bool IsDisposeRequested,
    bool IsDisposed,
    bool OwnerEventsAttached,
    int ObserverCount,
    long DispatchFailureCount,
    bool HasLastDispatchFailure)
{
    public bool IsConverged => IsDisposed && !OwnerEventsAttached && ObserverCount is 0;
}

internal interface ISimCoreEventSink
{
    void WriteDirective(SimDirectiveDomain domain, int op, SimDirectiveStatus condition, uint primaryObjectIdent = 0u, string? phrase = null);

    void WriteLifecycle(SimLifespanPhase earlier, SimLifespanPhase latest);

    void WriteTravel(in SimLocomotionCapture travel);

    void WriteGateway(in SimPortalCapture gateway);
}

// Fans simulation events out to subscribed watchers
internal sealed class SimCoreEventExchange : ISimEventFeed, ISimCoreEventSink, ISimActorObjectWatcher, ISimCommsWatcher, IDisposable
{
    private delegate void Handoff<T>(ISimEventWatcher watcher, in T delta);

    private readonly SimActorObjectLifetime _entityObjects;
    private readonly SimCommsLedger _communication;
    private readonly SimActionLedger _actions;
    private readonly object _latch = new();
    private ISimEventWatcher[] _watchers = [];
    private IDisposable? _actorTap;
    private IDisposable? _commsTap;
    private bool _fightingHooked;
    private bool _teardownAsked;
    private bool _destroyed;

    public SimCoreEventExchange(SimActorObjectLifetime entityObjects, SimCommsLedger communication, SimActionLedger actions)
    {
        _entityObjects = entityObjects ?? throw new ArgumentNullException(nameof(entityObjects));
        _communication = communication ?? throw new ArgumentNullException(nameof(communication));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public long DispatchFailureTally { get; private set; }
    public Exception? LastDispatchMiss { get; private set; }

    private bool HolderSignalsAffixed => _actorTap is not null || _commsTap is not null || _fightingHooked;

    public SimCoreEventHoldingCapture SnapOwnership()
    {
        lock (_latch)
        {
            return new SimCoreEventHoldingCapture(
                _teardownAsked, _destroyed, HolderSignalsAffixed, _watchers.Length, DispatchFailureTally, LastDispatchMiss is not null);
        }
    }

    public IDisposable Subscribe(ISimEventWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_teardownAsked || _destroyed, this);
            var latest = _watchers;
            if (Array.IndexOf(latest, watcher) >= 0)
                throw new InvalidOperationException("The runtime observer is by now subscribed");

            if (!HolderSignalsAffixed)
                TapSrcs();

            Volatile.Write(ref _watchers, [.. latest, watcher]);
        }
        return new WatcherSubscription(this, watcher);
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _teardownAsked = true;
            Volatile.Write(ref _watchers, []);
            try
            {
                UnhookSrcs("Runtime event subscriptions did not converge.");
            }
            finally
            {
                _destroyed = !HolderSignalsAffixed;
            }
        }
    }

    private sealed class WatcherSubscription(SimCoreEventExchange holder, ISimEventWatcher watcher) : IDisposable
    {
        private SimCoreEventExchange? _holder = holder;

        public void Dispose() => Interlocked.Exchange(ref _holder, null)?.Delist(watcher);
    }

    public void WriteDirective(SimDirectiveDomain domain, int op, SimDirectiveStatus condition, uint primaryObjectIdent = 0u, string? phrase = null)
    {
        if (!Watched)
            return;
        var diff = new SimDirectiveDiff(UpcomingStamp(), domain, op, condition, primaryObjectIdent, phrase);
        Fan(in diff, static (ISimEventWatcher watcher, in SimDirectiveDiff d) => watcher.OnDirective(in d));
    }

    public void WriteLifecycle(SimLifespanPhase earlier, SimLifespanPhase latest)
    {
        if (latest == earlier || !Watched)
            return;
        var diff = new SimLifespanDiff(UpcomingStamp(), earlier, latest);
        Fan(in diff, static (ISimEventWatcher watcher, in SimLifespanDiff d) => watcher.OnLifecycle(in d));
    }

    public void WriteTravel(in SimLocomotionCapture travel)
    {
        if (!Watched)
            return;
        var diff = new SimLocomotionDiff(UpcomingStamp(), travel);
        Fan(in diff, static (ISimEventWatcher watcher, in SimLocomotionDiff d) => watcher.OnTravel(in d));
    }

    public void WriteGateway(in SimPortalCapture gateway)
    {
        if (!Watched)
            return;
        var diff = new SimPortalDiff(UpcomingStamp(), gateway);
        Fan(in diff, static (ISimEventWatcher watcher, in SimPortalDiff d) => watcher.OnGateway(in d));
    }

    public void OnEntity(in SimActorDiff diff)
    {
        if (Watched)
            Fan(in diff, static (ISimEventWatcher watcher, in SimActorDiff d) => watcher.OnActor(in d));
    }

    public void OnInventory(in SimStashDiff diff)
    {
        if (Watched)
            Fan(in diff, static (ISimEventWatcher watcher, in SimStashDiff d) => watcher.OnSatchel(in d));
    }

    public void OnComms(in SimCommsEvent committed)
    {
        if (!Watched)
            return;
        var diff = new SimCommsDiff(UpcomingStamp(), committed.Entry);
        Fan(in diff, static (ISimEventWatcher watcher, in SimCommsDiff d) => watcher.OnChat(in d));
    }

    private bool Watched => Volatile.Read(ref _watchers).Length is not 0;

    private void Delist(ISimEventWatcher watcher)
    {
        lock (_latch)
        {
            var latest = _watchers;
            int ordinal = Array.IndexOf(latest, watcher);
            if (ordinal < 0)
                return;
            if (latest.Length is 1)
            {
                Volatile.Write(ref _watchers, []);
                UnhookSrcs("Runtime event subscriptions did not detach.");
                return;
            }

            ISimEventWatcher[] leftover = new ISimEventWatcher[latest.Length - 1];
            Array.Copy(latest, 0, leftover, 0, ordinal);
            Array.Copy(latest, ordinal + 1, leftover, ordinal, latest.Length - ordinal - 1);
            Volatile.Write(ref _watchers, leftover);
        }
    }

    private void TapSrcs()
    {
        IDisposable? actor = null;
        IDisposable? comms = null;
        try
        {
            actor = _entityObjects.Events.Enlist(this);
            comms = _communication.Events.Subscribe(this);
            _actions.CombatChanged += OnFightingAltered;
            _fightingHooked = true;
            _actorTap = actor;
            _commsTap = comms;
        }
        catch
        {
            UnhookFighting();
            comms?.Dispose();
            actor?.Dispose();
            throw;
        }
    }

    // Releases every hook, collecting failures so one bad detach does not leave the others attached
    private void UnhookSrcs(string missMsg)
    {
        List<Exception>? misses = null;
        Release(ref _commsTap, "communication event subscription", ref misses);
        Release(ref _actorTap, "entity/object event subscription", ref misses);
        UnhookFighting();
        if (misses is not null)
            throw new AggregateException(missMsg, misses);
    }

    private void UnhookFighting()
    {
        if (!_fightingHooked)
            return;
        _actions.CombatChanged -= OnFightingAltered;
        _fightingHooked = false;
    }

    private static void Release(ref IDisposable? tap, string label, ref List<Exception>? misses)
    {
        if (tap is null)
            return;
        try
        {
            tap.Dispose();
            tap = null;
        }
        catch (Exception problem)
        {
            (misses ??= []).Add(new InvalidOperationException($"{label} didn't detach", problem));
        }
    }

    private SimEventMark UpcomingStamp() => _entityObjects.Events.UpcomingStamp();

    // Delivers one delta to every watcher; a throwing watcher is recorded and skipped
    private void Fan<T>(in T diff, Handoff<T> deliver)
    {
        var watchers = Volatile.Read(ref _watchers);
        foreach (ISimEventWatcher watcher in watchers)
        {
            try
            {
                deliver(watcher, in diff);
            }
            catch (Exception problem)
            {
                ++DispatchFailureTally;
                LastDispatchMiss = problem;
            }
        }
    }

    private void OnFightingAltered()
    {
        if (!Watched)
            return;
        var acts = _actions.View.Snapshot;
        var diff = new SimFightingDiff(UpcomingStamp(), acts.CombatMode, acts.TrackedTargetHealthCount, acts.CombatAttack);
        Fan(in diff, static (ISimEventWatcher watcher, in SimFightingDiff d) => watcher.OnFighting(in d));
    }
}
