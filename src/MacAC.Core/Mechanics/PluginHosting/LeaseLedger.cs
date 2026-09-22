namespace MacAC.Mechanics.PluginHosting;

internal sealed class LeaseLedger<T>(string holderLabel) where T : class
{
    private readonly Lock _synchronize = new();
    private readonly List<T> _tenancies = [];
    private bool _closed;

    public bool IsClosed
    {
        get
        {
            lock (_synchronize)
                return _closed;
        }
    }

    public void HurlIfClosed()
    {
        if (IsClosed)
            throw new ObjectDisposedException(holderLabel);
    }

    // Keeps the lease, or hands it back to free and throws when closed
    public void Keep(T tenancy, Action<T> free)
    {
        lock (_synchronize)
        {
            if (!_closed)
            {
                _tenancies.Add(tenancy);
                return;
            }
        }
        free(tenancy);
        throw new ObjectDisposedException(holderLabel);
    }

    public bool Forget(T tenancy)
    {
        lock (_synchronize)
            return _tenancies.Remove(tenancy);
    }

    // Removes the most recently kept matching lease (delegates compare by value)
    public void DropPrevious(T tenancy)
    {
        lock (_synchronize)
        {
            for (int idx = _tenancies.Count - 1; idx >= 0; --idx)
            {
                if (Equals(_tenancies[idx], tenancy))
                {
                    _tenancies.RemoveAt(idx);
                    return;
                }
            }
        }
    }

    // Closes the ledger and releases every lease newest-first
    public void Close(Action<T> free) => FreeAll(Close(), free);

    // Closes the ledger and hands back what it held, oldest-first; empty if already closed
    public T[] Close()
    {
        lock (_synchronize)
        {
            if (_closed)
                return [];
            _closed = true;
            T[] pinned = [.. _tenancies];
            _tenancies.Clear();
            return pinned;
        }
    }

    public static void FreeAll(T[] pinned, Action<T> free)
    {
        for (int idx = pinned.Length - 1; idx >= 0; --idx)
            free(pinned[idx]);
    }

    public static void FreeQuietly(Action free)
    {
        try
        {
            free();
        }
        catch
        {
            // A misbehaving host must not stop the rest of the teardown
        }
    }
}
