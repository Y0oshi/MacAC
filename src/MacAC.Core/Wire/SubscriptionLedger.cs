namespace MacAC.Wire;

// Collects subscriptions as they are made and releases them in reverse order on dispose
internal sealed class SubscriptionLedger : IDisposable
{
    // One release action that runs at most once successfully and never re-enters itself
    private sealed class WireEntry(Action free)
    {
        private readonly object _latch = new(); // plain object: Monitor.Wait/PulseAll need it
        private Action? _queued = free;
        private int _runningOn; // managed thread id while running, else 0

        public void Release()
        {
            int me = Environment.CurrentManagedThreadId;
            Action? act;
            lock (_latch)
            {
                while (_runningOn is not 0)
                {
                    if (_runningOn == me)
                        throw new InvalidOperationException("Subscription cleanup can't complete reentrantly");
                    Monitor.Wait(_latch);
                }

                act = _queued;
                if (act is null)
                    return;
                _runningOn = me;
            }

            bool ok = false;
            try
            {
                act();
                ok = true;
            }
            finally
            {
                lock (_latch)
                {
                    if (ok)
                        _queued = null;
                    _runningOn = 0;
                    Monitor.PulseAll(_latch);
                }
            }
        }
    }

    private readonly Lock _latch = new();
    private readonly List<WireEntry> _listings = [];
    private bool _closing;

    public void Add(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        Keep(new WireEntry(subscription.Dispose));
    }

    public void Add(Action delist)
    {
        ArgumentNullException.ThrowIfNull(delist);
        Keep(new WireEntry(delist));
    }

    public void Dispose()
    {
        WireEntry[] listings;
        lock (_latch)
        {
            _closing = true;
            listings = [.. _listings];
        }

        List<Exception>? misses = null;
        for (int idx = listings.Length - 1; idx >= 0; --idx)
        {
            try
            {
                listings[idx].Release();
            }
            catch (Exception miss)
            {
                (misses ??= []).Add(miss);
            }
        }

        if (misses is not null)
            throw new AggregateException("one or more subscriptions could not detach", misses);
    }

    private void Keep(WireEntry listing)
    {
        bool late;
        lock (_latch)
        {
            late = _closing;
            _listings.Add(listing);
        }
        if (!late)
            return;

        listing.Release();
        throw new ObjectDisposedException(nameof(SubscriptionLedger));
    }
}
