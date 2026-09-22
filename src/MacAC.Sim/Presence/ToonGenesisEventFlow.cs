using MacAC.Mechanics.Genesis;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

internal sealed class ToonGenesisEventFlow : IDisposable
{
    private readonly object _latch = new();
    private readonly List<SimToonGenesisDiff> _queued = [];
    private ISimToonGenesisWatcher[] _watchers = [];
    private ulong _series;
    private bool _dispatching;
    private bool _destroyed;

    public IDisposable Subscribe(ISimToonGenesisWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (Array.IndexOf(_watchers, watcher) >= 0)
            {
                throw new InvalidOperationException(
                    "The character-creation observer is by now subscribed");
            }
            ISimToonGenesisWatcher[] substitute = new ISimToonGenesisWatcher[_watchers.Length + 1];
            Array.Copy(_watchers, substitute, _watchers.Length);
            substitute[^1] = watcher;
            Volatile.Write(ref _watchers, substitute);
        }
        return new Subscription(this, watcher);
    }

    public void Publish(
        SimEpochTicket gen,
        long rev,
        SimToonGenesisDiffKind sort)
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _queued.Add(new SimToonGenesisDiff(
                gen,
                unchecked(++_series),
                rev,
                sort));
            if (_dispatching)
                return;
            _dispatching = true;
        }

        int ordinal = 0;
        while (true)
        {
            SimToonGenesisDiff diff;
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

            foreach (ISimToonGenesisWatcher watcher in Volatile.Read(ref _watchers))
            {
                try
                {
                    watcher.OnToonCreationAltered(in diff);
                }
                catch (Exception problem)
                {
                    Console.Error.WriteLine(
                        $"runtime: character-creation observer failed: {problem.Message}");
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

    private void Delist(ISimToonGenesisWatcher watcher)
    {
        lock (_latch)
        {
            int ordinal = Array.IndexOf(_watchers, watcher);
            if (ordinal < 0)
                return;
            ISimToonGenesisWatcher[] substitute = new ISimToonGenesisWatcher[_watchers.Length - 1];
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
        ToonGenesisEventFlow holder,
        ISimToonGenesisWatcher watcher)
        : IDisposable
    {
        private ToonGenesisEventFlow? _holder = holder;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Delist(watcher);
    }
}
