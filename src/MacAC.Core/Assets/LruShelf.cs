namespace MacAC.Assets;

// A least-recently-used shelf bounded by an entry count and a byte budget
internal sealed class LruShelf<TKey, TValue>(int listingAllowance, long byteAllowance)
    where TKey : notnull
    where TValue : class
{
    private sealed record SlotDef(TKey Key, TValue Value, long Bytes);

    private readonly Dictionary<TKey, LinkedListNode<SlotDef>> _ordinal = new();
    private readonly LinkedList<SlotDef> _recency = new();   // oldest first
    private readonly Lock _latch = new();
    private long _octets;
    private long _strikes;
    private long _misses;
    private long _evictions;

    public int ListingAllowance { get; } = listingAllowance;

    public long ByteAllowance { get; } = byteAllowance;

    public ShelfStats Stats
    {
        get
        {
            return new(Interlocked.Read(ref _strikes), Interlocked.Read(ref _misses), Interlocked.Read(ref _evictions));
        }
    }

    public int Count
    {
        get { lock (_latch) return _ordinal.Count; }
    }

    public long Bytes
    {
        get { lock (_latch) return _octets; }
    }

    public bool TryGet(TKey tag, out TValue val)
    {
        lock (_latch)
        {
            if (!_ordinal.TryGetValue(tag, out LinkedListNode<SlotDef>? joint))
            {
                Interlocked.Increment(ref _misses);
                val = null!;
                return false;
            }

            Interlocked.Increment(ref _strikes);
            Touch(joint);
            val = joint.Value.Value;
            return true;
        }
    }

    // Returns the shelved value for tag, shelving contender when the key is new
    public TValue Admit(TKey tag, TValue contender, long octets, out bool shelved)
    {
        lock (_latch)
        {
            if (_ordinal.TryGetValue(tag, out LinkedListNode<SlotDef>? pinned))
            {
                Touch(pinned);
                shelved = true;
                return pinned.Value.Value;
            }

            if (ListingAllowance is 0 || octets > ByteAllowance)
            {
                shelved = false;
                return contender;
            }

            while (_recency.First is { } oldest && (_ordinal.Count >= ListingAllowance || _octets + octets > ByteAllowance))
                Evict(oldest);

            _ordinal.Add(tag, _recency.AddLast(new SlotDef(tag, contender, octets)));
            _octets += octets;
            shelved = true;
            return contender;
        }
    }

    private void Touch(LinkedListNode<SlotDef> joint)
    {
        if (!ReferenceEquals(joint, _recency.Last))
        {
            _recency.Remove(joint);
            _recency.AddLast(joint);
        }
    }

    private void Evict(LinkedListNode<SlotDef> joint)
    {
        _recency.Remove(joint);
        _ordinal.Remove(joint.Value.Key);
        _octets -= joint.Value.Bytes;
        Interlocked.Increment(ref _evictions);
    }
}
