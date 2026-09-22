using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

internal sealed class SimToonPickEventFlow : IDisposable
{
    private readonly object _latch = new();
    private readonly List<SimToonPickDiff> _queued = [];
    private ISimToonPickWatcher[] _watchers = [];
    private ulong _series;
    private bool _dispatching;
    private bool _destroyed;

    public IDisposable Subscribe(ISimToonPickWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (Array.IndexOf(_watchers, watcher) >= 0)
            {
                throw new InvalidOperationException(
                    "The character-selection observer is by now subscribed");
            }
            ISimToonPickWatcher[] substitute = new ISimToonPickWatcher[
                _watchers.Length + 1];
            Array.Copy(_watchers, substitute, _watchers.Length);
            substitute[^1] = watcher;
            Volatile.Write(ref _watchers, substitute);
        }
        return new Subscription(this, watcher);
    }

    public void Publish(
        SimEpochTicket gen,
        long rev,
        SimToonPickDiffKind sort,
        uint toonIdent,
        uint problemCode)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _queued.Add(new SimToonPickDiff(
                gen,
                unchecked(++_series),
                rev,
                sort,
                toonIdent,
                problemCode));
            if (_dispatching)
                return;
            _dispatching = true;
        }

        int ordinal = 0;
        while (true)
        {
            SimToonPickDiff diff;
            lock (_latch)
            {
                if (ordinal >= _queued.Count)
                {
                    _queued.Clear();
                    _dispatching = false;
                    return;
                }
                diff = _queued[ordinal++];
            }

            foreach (ISimToonPickWatcher watcher
                     in Volatile.Read(ref _watchers))
            {
                try
                {
                    watcher.OnToonPickAltered(in diff);
                }
                catch (Exception problem)
                {
                    Console.Error.WriteLine(
                        $"runtime: character-selection observer failed: {problem.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _queued.Clear();
            _dispatching = false;
            Volatile.Write(ref _watchers, []);
        }
    }

    private void Delist(ISimToonPickWatcher watcher)
    {
        lock (_latch)
        {
            int ordinal = Array.IndexOf(_watchers, watcher);
            if (ordinal < 0)
                return;
            ISimToonPickWatcher[] substitute = new ISimToonPickWatcher[
                _watchers.Length - 1];
            if (ordinal > 0)
                Array.Copy(_watchers, 0, substitute, 0, ordinal);
            if (ordinal < _watchers.Length - 1)
            {
                Array.Copy(
                    _watchers,
                    ordinal + 1,
                    substitute,
                    ordinal,
                    _watchers.Length - ordinal - 1);
            }
            Volatile.Write(ref _watchers, substitute);
        }
    }

    private sealed class Subscription(
        SimToonPickEventFlow holder,
        ISimToonPickWatcher watcher)
        : IDisposable
    {
        private SimToonPickEventFlow? _holder = holder;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Delist(watcher);
    }
}
