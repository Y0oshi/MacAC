namespace MacAC.Mechanics.Gear;

public sealed class HotbarStore : IDisposable
{
    public const int SlotTally = 18;

    private readonly HotbarSlot?[] _chambers = new HotbarSlot?[SlotTally];
    private HotbarSlot[] _dense = [];
    private Action? _altered;
    private long _rev;
    private long _relayMisses;
    private bool _destroyed;

    public event Action? Changed
    {
        add
        {
            HurlIfDestroyed();
            _altered += value;
        }
        remove => _altered -= value;
    }

    public IReadOnlyList<HotbarSlot> Items => Volatile.Read(ref _dense);

    public int Count => Volatile.Read(ref _dense).Length;
    public long Revision => Interlocked.Read(ref _rev);
    public int SubscriberTally => _altered?.GetInvocationList().Length ?? 0;
    public long RelayMissTally => Interlocked.Read(ref _relayMisses);
    public Exception? PreviousRelayMiss { get; private set; }
    public bool IsDisposed => _destroyed;

    /// <summary>Raw entry at <paramref name="socket"/>, or null for absent/out-of-range.</summary>
    public HotbarSlot? FetchListing(int socket) => (uint)socket < SlotTally ? _chambers[socket] : null;

    public HotbarSlot?[] Snapshot() => (HotbarSlot?[])_chambers.Clone();

    /// <summary>Visible object id at <paramref name="socket"/>, or zero.</summary>
    public uint Get(int socket) => FetchListing(socket)?.ObjectId ?? 0u;

    public bool IsEmpty(int socket) => Get(socket) is 0u;

    public void Load(IEnumerable<HotbarSlot> listings)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(listings);
        Array.Clear(_chambers);
        foreach (HotbarSlot listing in listings)
            Stow(listing);
        Publish();
    }

    public void Set(HotbarSlot listing)
    {
        HurlIfDestroyed();
        if (Stow(listing))
            Publish();
    }

    public void ApplyGear(int socket, uint objectIdent) => Set(new HotbarSlot(socket, objectIdent, 0u));

    public void Remove(int socket)
    {
        HurlIfDestroyed();
        if ((uint)socket >= SlotTally || _chambers[socket] is null)
            return;
        _chambers[socket] = null;
        Publish();
    }

    public void Clear()
    {
        HurlIfDestroyed();
        Array.Clear(_chambers);
        Publish();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        Array.Clear(_chambers);
        Publish();
        _altered = null;
    }

    private void HurlIfDestroyed() => ObjectDisposedException.ThrowIf(_destroyed, this);

    // Writes a cell; false when the index is bad or the cell already holds that record
    private bool Stow(HotbarSlot listing)
    {
        if ((uint)listing.Index >= SlotTally || _chambers[listing.Index] == listing)
            return false;
        _chambers[listing.Index] = listing;
        return true;
    }

    private void Publish()
    {
        Repack();
        Interlocked.Increment(ref _rev);
        Signal();
    }

    private void Repack()
    {
        int occupied = 0;
        foreach (HotbarSlot? chamber in _chambers)
        {
            if (chamber is not null)
                ++occupied;
        }

        HotbarSlot[] dense = new HotbarSlot[occupied];
        int at = 0;
        foreach (HotbarSlot? chamber in _chambers)
        {
            if (chamber is { } listing)
                dense[at++] = listing;
        }
        Volatile.Write(ref _dense, dense);
    }

    private void Signal()
    {
        foreach (Delegate watcher in _altered?.GetInvocationList() ?? [])
        {
            try
            {
                ((Action)watcher)();
            }
            catch (Exception problem)
            {
                Interlocked.Increment(ref _relayMisses);
                PreviousRelayMiss = problem;
            }
        }
    }
}
