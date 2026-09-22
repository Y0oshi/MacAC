namespace MacAC.Assets.Pak;

// Raw texture payloads read from the pak, LRU-bounded by bytes and entries
internal sealed class PakTextureShelf
{
    internal const long DefaultCeilingOctets = 64L * 1024 * 1024;
    internal const int DefaultCeilingListings = 1_024;

    private readonly LruShelf<ulong, byte[]> _shelf;

    public PakTextureShelf(long ceilingOctets = DefaultCeilingOctets, int ceilingListings = DefaultCeilingListings)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingOctets, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingListings, 1);
        _shelf = new LruShelf<ulong, byte[]>(ceilingListings, ceilingOctets);
    }

    internal int Count => _shelf.Count;

    internal long Octets => _shelf.Bytes;

    public bool TryGet(ulong tag, out byte[] octets) => _shelf.TryGet(tag, out octets);

    public byte[] AppendOrFetch(ulong tag, byte[] octets)
    {
        ArgumentNullException.ThrowIfNull(octets);
        return _shelf.Admit(tag, octets, octets.LongLength, out _);
    }
}
