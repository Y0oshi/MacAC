namespace MacAC.Sim.Presence;

// The subscriptions a session route holds, disposed in reverse order
internal sealed class OnlineSessionSubscriptionSet : IDisposable
{
    private readonly object _latch = new();
    private readonly List<Retryable> _pinned = [];
    private bool _disposing;

    public void Add(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        Keep(new Retryable(subscription.Dispose));
    }

    public void Add(Action delist)
    {
        ArgumentNullException.ThrowIfNull(delist);
        Keep(new Retryable(delist));
    }

    public void Dispose()
    {
        Retryable[] pinned;
        lock (_latch)
        {
            _disposing = true;
            pinned = [.. _pinned];
        }

        List<Exception>? problems = null;
        for (int idx = pinned.Length - 1; idx >= 0; --idx)
        {
            try
            {
                pinned[idx].Dispose();
            }
            catch (Exception problem)
            {
                (problems ??= []).Add(problem);
            }
        }
        if (problems is not null)
            throw new AggregateException("one or more live-session subscriptions could not detach", problems);
    }

    private void Keep(Retryable kept)
    {
        bool late;
        lock (_latch)
        {
            late = _disposing;
            _pinned.Add(kept);
        }
        if (!late)
            return;
        kept.Dispose();
        throw new ObjectDisposedException(nameof(OnlineSessionSubscriptionSet));
    }

    // Runs the detach once; a failed attempt can be retried, a concurrent attempt waits, a re-entrant
    // one is refused
    private sealed class Retryable(Action teardown) : IDisposable
    {
        private readonly object _latch = new();
        private Action? _unfasten = teardown;
        private bool _running;
        private int _runningThread;

        public void Dispose()
        {
            Action? unfasten;
            int thread = Environment.CurrentManagedThreadId;
            lock (_latch)
            {
                while (_running)
                {
                    if (_runningThread == thread)
                        throw new InvalidOperationException("Live-session subscription cleanup can't complete reentrantly");
                    Monitor.Wait(_latch);
                }
                unfasten = _unfasten;
                if (unfasten is null)
                    return;
                _running = true;
                _runningThread = thread;
            }

            try
            {
                unfasten();
            }
            catch
            {
                DisposeRest(succeeded: false);
                throw;
            }
            DisposeRest(succeeded: true);
        }

        private void DisposeRest(bool succeeded)
        {
            lock (_latch)
            {
                if (succeeded)
                    _unfasten = null;
                _running = false;
                _runningThread = 0;
                Monitor.PulseAll(_latch);
            }
        }
    }
}
