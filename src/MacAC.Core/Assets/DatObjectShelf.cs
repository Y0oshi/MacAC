using System.Diagnostics.CodeAnalysis;
using MacAC.Dat;

namespace MacAC.Assets;

internal sealed class DatObjectShelf
{
    internal const int DefaultListingThreshold = 256;
    internal const long DefaultEstimatedByteThreshold = 64L * 1024L * 1024L;
    private const long PlanarEstimate = 128L * 1024L;

    private readonly record struct ShelfKey(Type ObjectType, uint FileId);

    private readonly LruShelf<ShelfKey, IDatRecord> _shelf;
    private readonly Func<IDatRecord, long> _estimate;

    internal DatObjectShelf(
        int listingThreshold = DefaultListingThreshold,
        long estimatedByteThreshold = DefaultEstimatedByteThreshold,
        Func<IDatRecord, long>? estimateKeptOctets = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(listingThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(estimatedByteThreshold, 1);
        _shelf = new LruShelf<ShelfKey, IDatRecord>(listingThreshold, estimatedByteThreshold);
        _estimate = estimateKeptOctets ?? Guess;
    }

    internal ShelfStats Stats => _shelf.Stats;

    internal int Count => _shelf.Count;

    internal long EstimatedOctets => _shelf.Bytes;

    internal bool TryGet<T>(uint fileIdent, [MaybeNullWhen(false)] out T val) where T : IDatRecord
    {
        if (_shelf.TryGet(new ShelfKey(typeof(T), fileIdent), out IDatRecord pinned))
        {
            val = (T)pinned;
            return true;
        }
        val = default;
        return false;
    }

    internal T FetchOrAppend<T>(uint fileIdent, T contender) where T : IDatRecord
    {
        ArgumentNullException.ThrowIfNull(contender);
        long octets = Math.Max(1L, _estimate(contender));
        return (T)_shelf.Admit(new ShelfKey(typeof(T), fileIdent), contender, octets, out _);
    }

    private static long Guess(IDatRecord val)
    {
        return val is Bitmap canvas ? Math.Max(PlanarEstimate, canvas.Pixels.LongLength + 256L) : PlanarEstimate;
    }
}
