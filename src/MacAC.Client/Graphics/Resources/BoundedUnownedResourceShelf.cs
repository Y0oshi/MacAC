namespace MacAC.Client.Graphics;

internal sealed class BoundedUnownedResourceShelf<TKey> where TKey : notnull
{
    private readonly LinkedList<TKey> _lru = new();
    private readonly Dictionary<TKey, EntryDef> _listings = [];

    private readonly record struct EntryDef(long Bytes, LinkedListNode<TKey> Node);

    public BoundedUnownedResourceShelf(long allowanceOctets, int ceilingTally = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(allowanceOctets);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingTally, 1);
        _allowanceOctets = allowanceOctets;
        _ceilingTally = ceilingTally;
    }

    public int Count => _listings.Count;
    public long HousedBytes { get; private set; }
    private readonly long _allowanceOctets;

    public long AllowanceOctets => _allowanceOctets;
    private readonly int _ceilingTally;

    public int CeilingCount => _ceilingTally;
    public bool Contains(TKey tag) => _listings.ContainsKey(tag);

    public void FlagUnowned(TKey tag, long octets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octets);

        if (_listings.TryGetValue(tag, out EntryDef extant))
        {
            if (extant.Bytes != octets)
                throw new InvalidOperationException(
                    $"Cached resource {tag} changed size from {extant.Bytes} to {octets} bytes.");

            _lru.Remove(extant.Node);
            _lru.AddLast(extant.Node);
            return;
        }

        var joint = _lru.AddLast(tag);
        _listings.Add(tag, new EntryDef(octets, joint));
        HousedBytes = checked(HousedBytes + octets);
    }

    public bool FlagPossessed(TKey tag)
    {
        if (!_listings.Remove(tag, out EntryDef listing))
            return false;

        _lru.Remove(listing.Node);
        HousedBytes -= listing.Bytes;
        return true;
    }

    public bool TryGrabOldestOverAllowance(out TKey tag)
    {
        if ((HousedBytes <= _allowanceOctets && _listings.Count <= _ceilingTally)
            || _lru.First is null)
        {
            tag = default!;
            return false;
        }

        var joint = _lru.First;
        tag = joint.Value;
        EntryDef listing = _listings[tag];
        _lru.RemoveFirst();
        _listings.Remove(tag);
        HousedBytes -= listing.Bytes;
        return true;
    }

    public bool TryGrabOldest(out TKey tag)
    {
        if (_lru.First is null)
        {
            tag = default!;
            return false;
        }

        var joint = _lru.First;
        tag = joint.Value;
        EntryDef listing = _listings[tag];
        _lru.RemoveFirst();
        _listings.Remove(tag);
        HousedBytes -= listing.Bytes;
        return true;
    }

    public bool TryTake(TKey tag)
    {
        if (!_listings.Remove(tag, out EntryDef listing))
            return false;

        _lru.Remove(listing.Node);
        HousedBytes -= listing.Bytes;
        return true;
    }

    public void Clear()
    {
        _lru.Clear();
        _listings.Clear();
        HousedBytes = 0;
    }
}
