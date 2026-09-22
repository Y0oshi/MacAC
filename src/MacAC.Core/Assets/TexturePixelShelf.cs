using System.Collections.Concurrent;

namespace MacAC.Assets;

// Identifies one decoded pixel buffer: the surface and how it was decoded
internal readonly record struct TexturePixelKey(uint RenderSurfaceId, bool IsClipMap, bool IsAdditive);

internal sealed class TexturePixelShelf
{
    private readonly LruShelf<TexturePixelKey, byte[]> _shelf;
    private readonly ConcurrentDictionary<TexturePixelKey, Lazy<byte[]>> _decoding = new();

    public TexturePixelShelf(long ceilingOctets, int ceilingListings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingListings);
        _shelf = new LruShelf<TexturePixelKey, byte[]>(ceilingListings, ceilingOctets);
    }

    public ShelfStats Stats => _shelf.Stats;

    public int Count => _shelf.Count;

    public long HousedOctets => _shelf.Bytes;

    public bool TryGet(TexturePixelKey tag, out byte[] px) => _shelf.TryGet(tag, out px);

    public byte[] KeepOrUse(TexturePixelKey tag, byte[] px, out bool isStashed)
    {
        ArgumentNullException.ThrowIfNull(px);
        return _shelf.Admit(tag, px, px.LongLength, out isStashed);
    }

    public byte[] FetchOrBuild(TexturePixelKey tag, Func<byte[]> maker, out bool isStashed)
    {
        ArgumentNullException.ThrowIfNull(maker);
        if (TryGet(tag, out byte[] pinned))
        {
            isStashed = true;
            return pinned;
        }

        Lazy<byte[]> mine = new Lazy<byte[]>(maker, LazyThreadSafetyMode.ExecutionAndPublication);
        var shared = _decoding.GetOrAdd(tag, mine);
        try
        {
            return KeepOrUse(tag, shared.Value, out isStashed);
        }
        finally
        {
            _decoding.TryRemove(new KeyValuePair<TexturePixelKey, Lazy<byte[]>>(tag, shared));
        }
    }
}
