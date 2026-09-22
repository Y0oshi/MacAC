using MacAC.Extensibility.World;

namespace MacAC.Mechanics.PluginHosting;

public sealed class RealmSignals : IWorldPulse
{
    private sealed class Listener(Action<EntityFrame> handler)
    {
        public Action<EntityFrame> Handler { get; } = handler;

        public Queue<EntityFrame> Backlog { get; } = new();

        // Still draining replay and backlog; not yet in the multicast set
        public bool CatchingUp { get; set; } = true;

        public bool Attached { get; set; } = true;
    }

    private readonly Lock _synchronize = new();
    private readonly Dictionary<uint, EntityFrame> _world = [];
    private readonly List<Listener> _listeners = [];
    private Listener[] _multicast = [];
    private Action<double>? _beat;

    public event Action<double> Tick
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_synchronize)
                _beat += value;
        }
        remove
        {
            if (value is null)
                return;
            lock (_synchronize)
                _beat -= value;
        }
    }

    public event Action<EntityFrame> EntitySpawned
    {
        add
        {
            ArgumentNullException.ThrowIfNull(value);
            Listener listener = new Listener(value);
            EntityFrame[] rerun;
            lock (_synchronize)
            {
                _listeners.Add(listener);
                rerun = _world.Values.ToArray();
            }
            CatchUp(listener, rerun);
        }
        remove
        {
            if (value is null)
                return;
            lock (_synchronize)
            {
                for (int idx = _listeners.Count - 1; idx >= 0; --idx)
                {
                    Listener listener = _listeners[idx];
                    if (listener.Handler != value)
                        continue;
                    listener.Attached = false;
                    listener.Backlog.Clear();
                    _listeners.RemoveAt(idx);
                    if (!listener.CatchingUp)
                        ReassembleMulticastBolted();
                    break;
                }
            }
        }
    }

    public void TriggerActorSpawned(EntityFrame capture)
    {
        Listener[] straight;
        lock (_synchronize)
        {
            _world[capture.Id] = capture;
            foreach (Listener listener in _listeners)
            {
                if (listener.Attached && listener.CatchingUp)
                    listener.Backlog.Enqueue(capture);
            }
            straight = _multicast;
        }

        foreach (Listener listener in straight)
            Deliver(listener, capture);
    }

    public void UpsertLatest(EntityFrame capture)
    {
        lock (_synchronize)
            _world[capture.Id] = capture;
    }

    public bool DropActor(uint ident)
    {
        lock (_synchronize)
            return _world.Remove(ident);
    }

    public void WipeLatest()
    {
        lock (_synchronize)
        {
            _world.Clear();
            foreach (Listener listener in _listeners)
            {
                if (listener.CatchingUp)
                    listener.Backlog.Clear();
            }
        }
    }

    public void TriggerBeat(double passedSecs)
    {
        Action<double>? handlers;
        lock (_synchronize)
            handlers = _beat;
        if (handlers is null)
            return;
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<double>)handler)(passedSecs);
            }
            catch
            {
                // A plugin's failure never escapes event dispatch.
            }
        }
    }

    // Replays the world, drains the backlog, then promotes the listener to multicast
    private void CatchUp(Listener listener, EntityFrame[] rerun)
    {
        foreach (EntityFrame stale in rerun)
        {
            lock (_synchronize)
            {
                if (!listener.Attached)
                    return;
                // Skip entities that changed or left since the snapshot was taken.
                if (!_world.TryGetValue(stale.Id, out EntityFrame instant) || instant != stale)
                    continue;
            }
            Deliver(listener, stale);
        }

        while (true)
        {
            EntityFrame queued;
            lock (_synchronize)
            {
                if (!listener.Attached)
                    return;
                if (!listener.Backlog.TryDequeue(out queued))
                {
                    listener.CatchingUp = false;
                    ReassembleMulticastBolted();
                    return;
                }
            }
            Deliver(listener, queued);
        }
    }

    private static void Deliver(Listener listener, EntityFrame cycle)
    {
        try
        {
            listener.Handler(cycle);
        }
        catch
        {
            // A plugin's failure never escapes event dispatch.
        }
    }

    private void ReassembleMulticastBolted()
    {
        List<Listener> online = new List<Listener>(_listeners.Count);
        foreach (Listener listener in _listeners)
        {
            if (listener.Attached && !listener.CatchingUp)
                online.Add(listener);
        }
        _multicast = online.Count is 0 ? [] : [.. online];
    }
}
