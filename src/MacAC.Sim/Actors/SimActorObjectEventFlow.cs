using MacAC.Mechanics.Gear;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public interface ISimActorObjectWatcher
{
    void OnEntity(in SimActorDiff diff);

    void OnInventory(in SimStashDiff diff);
}

public interface ISimActorObjectEventFeed
{
    IDisposable Enlist(ISimActorObjectWatcher watcher);
}

public interface ISimPlacementWatcher
{
    void OnStance(in SimPlacementDiff diff);
}

public sealed class SimActorObjectEventFlow : ISimActorObjectEventFeed, IDisposable
{
    private readonly SimActorIndex _actors;
    private readonly ClientThingChart _objects;
    private readonly SimEventTicker _ticker = new();
    private readonly object _latch = new();
    private readonly List<SimPending> _fifo = [];
    private ISimActorObjectWatcher[] _watchers = [];
    private ISimPlacementWatcher[] _stanceWatchers = [];
    private Func<SimEpochTicket> _gen = static () => default;
    private Func<ulong> _cycleNumber = static () => 0UL;
    private bool _ctxTied;
    private bool _draining;
    private bool _destroyed;

    internal SimActorObjectEventFlow(SimActorIndex entities, ClientThingChart objects)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _objects.ObjectAdded += OnObjectAdded;
        _objects.ObjectUpdated += OnObjectUpdated;
        _objects.ObjectMoved += OnObjectMoved;
        _objects.ObjectRemovalClassified += OnObjectRemoved;
        _objects.Cleared += OnObjectsCleared;
    }

    public ulong PreviousSeries => _ticker.PreviousSequence;
    public int SubscriberCount => Volatile.Read(ref _watchers).Length;
    public int StanceSubscriberTally => Volatile.Read(ref _stanceWatchers).Length;
    public int QueuedRelayTally => _fifo.Count;
    public bool IsDispatching => _draining;
    public long DispatchMissCount { get; private set; }
    public Exception? LastRelayFailure { get; private set; }

    public void AttachCtx(Func<SimEpochTicket> gen, Func<ulong> cycleNumber)
    {
        ArgumentNullException.ThrowIfNull(gen);
        ArgumentNullException.ThrowIfNull(cycleNumber);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (_ctxTied)
                throw new InvalidOperationException("The Runtime entity/object event context is by now bound");
            _gen = gen;
            _cycleNumber = cycleNumber;
            _ctxTied = true;
        }
    }

    public IDisposable Enlist(ISimActorObjectWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (Array.IndexOf(_watchers, watcher) >= 0)
                throw new InvalidOperationException("The Runtime entity/object observer is by now subscribed");
            Volatile.Write(ref _watchers, With(_watchers, watcher));
        }
        return new SimSubscription(() => Delist(watcher));
    }

    public IDisposable EnlistStance(ISimPlacementWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (Array.IndexOf(_stanceWatchers, watcher) >= 0)
                throw new InvalidOperationException("The Runtime placement observer is by now subscribed");
            Volatile.Write(ref _stanceWatchers, With(_stanceWatchers, watcher));
        }
        return new SimSubscription(() => DelistStance(watcher));
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            Volatile.Write(ref _watchers, []);
            Volatile.Write(ref _stanceWatchers, []);
            _objects.Cleared -= OnObjectsCleared;
            _objects.ObjectRemovalClassified -= OnObjectRemoved;
            _objects.ObjectMoved -= OnObjectMoved;
            _objects.ObjectUpdated -= OnObjectUpdated;
            _objects.ObjectAdded -= OnObjectAdded;
        }
    }

    public SimEventMark UpcomingStamp()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _destroyed), this);
        return _ticker.Next(_gen(), _cycleNumber());
    }

    internal void UnfastenWatchers()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            Volatile.Write(ref _watchers, []);
            Volatile.Write(ref _stanceWatchers, []);
        }
    }

    internal void PublishEntity(SimActorChange edit, SimActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Post(SimPending.Entity(new SimActorDiff(UpcomingStamp(), edit, SimActorObjectLenses.Freeze(capture))));
    }

    private sealed class SimSubscription(Action free) : IDisposable
    {
        private Action? _free = free;

        public void Dispose() => Interlocked.Exchange(ref _free, null)?.Invoke();
    }

    internal void BroadcastStance(in SimPlacementMirrorCapture stance) =>
        Post(SimPending.Placement(new SimPlacementDiff(UpcomingStamp(), stance)));

    private static T[] With<T>(T[] latest, T added) where T : class => [.. latest, added];

    private static T[] Without<T>(T[] latest, int ordinal) where T : class
    {
        T[] leftover = new T[latest.Length - 1];
        Array.Copy(latest, 0, leftover, 0, ordinal);
        Array.Copy(latest, ordinal + 1, leftover, ordinal, latest.Length - ordinal - 1);
        return leftover;
    }

    private void Delist(ISimActorObjectWatcher watcher)
    {
        lock (_latch)
        {
            int ordinal = Array.IndexOf(_watchers, watcher);
            if (ordinal >= 0)
                Volatile.Write(ref _watchers, Without(_watchers, ordinal));
        }
    }

    private void DelistStance(ISimPlacementWatcher watcher)
    {
        lock (_latch)
        {
            int ordinal = Array.IndexOf(_stanceWatchers, watcher);
            if (ordinal >= 0)
                Volatile.Write(ref _stanceWatchers, Without(_stanceWatchers, ordinal));
        }
    }

    private void BroadcastSatchel(SimStashChange edit, SimStashItemCapture gear) =>
        Post(SimPending.Inventory(new SimStashDiff(UpcomingStamp(), edit, gear)));

    private void OnObjectAdded(ClientThing gear)
    {
        BroadcastSatchel(SimStashChange.Added, SimActorObjectLenses.Freeze(gear, _actors));
    }

    private void OnObjectUpdated(ClientThing gear)
    {
        BroadcastSatchel(SimStashChange.Updated, SimActorObjectLenses.Freeze(gear, _actors));
    }

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        ClientThing? gear = relocate.Item ?? _objects.Get(relocate.ItemId);
        SimStashItemCapture grab = gear is null
            ? new SimStashItemCapture(
                relocate.ItemId, 0, string.Empty, relocate.Current.ContainerId, relocate.Current.ContainerSlot,
                relocate.Current.WielderId, (uint)relocate.Current.EquipLocation, 0, 0)
            : SimActorObjectLenses.Freeze(gear, _actors);
        BroadcastSatchel(SimStashChange.Moved, grab);
    }

    private void OnObjectRemoved(ObjectRemoval deletion)
    {
        BroadcastSatchel(SimStashChange.Removed, SimActorObjectLenses.Freeze(deletion.Object, _actors, deletion.Generation));
    }

    private void OnObjectsCleared() => BroadcastSatchel(SimStashChange.Cleared, default);

    // Queues an event; the outermost caller drains the queue in order
    private void Post(SimPending queued)
    {
        _fifo.Add(queued);
        if (_draining)
            return;

        _draining = true;
        try
        {
            for (int idx = 0; idx < _fifo.Count; ++idx)
                Deliver(_fifo[idx]);
        }
        finally
        {
            _fifo.Clear();
            _draining = false;
        }
    }

    private void Deliver(SimPending queued)
    {
        if (queued.Kind is SimPending.Of.Placement)
        {
            foreach (ISimPlacementWatcher watcher in Volatile.Read(ref _stanceWatchers))
            {
                try
                {
                    watcher.OnStance(in queued.StanceDiff);
                }
                catch (Exception problem)
                {
                    Fail(problem);
                }
            }
            return;
        }

        foreach (ISimActorObjectWatcher watcher in Volatile.Read(ref _watchers))
        {
            try
            {
                if (queued.Kind is SimPending.Of.Entity)
                    watcher.OnEntity(in queued.ActorDiff);
                else
                    watcher.OnInventory(in queued.SatchelDiff);
            }
            catch (Exception problem)
            {
                Fail(problem);
            }
        }
    }

    private void Fail(Exception problem)
    {
        ++DispatchMissCount;
        LastRelayFailure = problem;
    }

    private readonly struct SimPending
    {
        public enum Of : byte { Entity, Inventory, Placement }

        public readonly Of Kind;
        public readonly SimActorDiff ActorDiff;
        public readonly SimStashDiff SatchelDiff;
        public readonly SimPlacementDiff StanceDiff;

        private SimPending(Of sort, SimActorDiff actor, SimStashDiff satchel, SimPlacementDiff stance)
        {
            Kind = sort;
            ActorDiff = actor;
            SatchelDiff = satchel;
            StanceDiff = stance;
        }

        public static SimPending Entity(SimActorDiff diff) => new(Of.Entity, diff, default, default);
        public static SimPending Inventory(SimStashDiff diff) => new(Of.Inventory, default, diff, default);
        public static SimPending Placement(SimPlacementDiff diff) => new(Of.Placement, default, default, diff);
    }
}
