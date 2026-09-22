using System.Collections.Concurrent;
using MacAC.Mechanics.Kinetics;
using MacAC.Dat;

namespace MacAC.Assets.Vfx;

public readonly record struct AnimationCacheTelemetry(int Count, long EstimatedBytes, long BudgetBytes, int EntryLimit, ShelfStats Stats);

public sealed class CanonAnimationLoader : IAnimReader
{
    public const long DefaultCeilingEstimatedOctets = 64L * 1024L * 1024L;
    public const int DefaultCeilingListings = 512;
    private const long AbsentEstimate = 128;
    private const uint AnimDidStem = 0x03000000u;

    // What a parse produced: possibly no animation, and what keeping it costs
    private sealed record Decoded(MotionClip? Animation, long EstimatedBytes);

    private readonly Func<uint, byte[]?> _rawOctets;
    private readonly DatArchive? _database;
    private readonly ConcurrentDictionary<uint, Lazy<Decoded>> _parsing = new();
    private readonly LruShelf<uint, Decoded> _shelf;

    public CanonAnimationLoader(IDatAccess datFiles) : this(datFiles, DefaultCeilingEstimatedOctets, DefaultCeilingListings)
    {
    }

    public CanonAnimationLoader(IDatAccess dats, long ceilingEstimatedOctets, int ceilingListings)
        : this((dats ?? throw new ArgumentNullException(nameof(dats))).Portal, ceilingEstimatedOctets, ceilingListings)
    {
    }

    public CanonAnimationLoader(IDatDatabase gateway) : this(gateway, DefaultCeilingEstimatedOctets, DefaultCeilingListings)
    {
    }

    public CanonAnimationLoader(IDatDatabase gateway, long ceilingEstimatedOctets, int ceilingListings)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingEstimatedOctets, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingListings, 1);
        _database = gateway.Db;
        _rawOctets = ident => gateway.TryGetFileBytes(ident, out byte[]? octets) ? octets : null;
        _shelf = new LruShelf<uint, Decoded>(ceilingListings, ceilingEstimatedOctets);
    }

    public AnimationCacheTelemetry Diagnostics
    {
        get
        {
            return new(_shelf.Count, _shelf.Bytes, _shelf.ByteAllowance, _shelf.ListingAllowance, _shelf.Stats);
        }
    }

    public MotionClip? PullAnim(uint ident)
    {
        if (!IsAnimDid(ident))
            return null;
        if (_shelf.TryGet(ident, out Decoded pinned))
            return pinned.Animation;

        Lazy<Decoded> mine = new Lazy<Decoded>(() => DecodeFromDat(ident), LazyThreadSafetyMode.ExecutionAndPublication);
        var shared = _parsing.GetOrAdd(ident, mine);
        try
        {
            Decoded decoded = shared.Value;
            return _shelf.Admit(ident, decoded, Math.Max(1, decoded.EstimatedBytes), out _).Animation;
        }
        finally
        {
            _parsing.TryRemove(new KeyValuePair<uint, Lazy<Decoded>>(ident, shared));
        }
    }

    // Decodes one Animation record and insists the whole file was consumed.
    public static MotionClip Decode(ReadOnlyMemory<byte> octets, DatArchive? database = null)
    {
        var span = octets.Span;
        uint ident = span.Length >= 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span) : 0u;
        var cursor = new DatCursor(span, ident);
        MotionClip anim = MotionClip.Read(ref cursor);
        if (!cursor.AtEnd)
            throw new InvalidDataException($"Animation 0x{anim.Id:X8} consumed {cursor.Offset} of {span.Length} bytes.");
        return anim;
    }

    /// <summary>AC data IDs reserve only the high byte for DBObj type.</summary>
    public static bool IsAnimDid(uint did) => (did & 0xFF000000u) == AnimDidStem;

    // A rough retained-size estimate: never less than the raw record, plus per-frame and per-hook
    // overheads
    internal static long GuessKeptOctets(MotionClip anim, long rawOctets)
    {
        ArgumentNullException.ThrowIfNull(anim);
        ArgumentOutOfRangeException.ThrowIfNegative(rawOctets);

        long estimate = checked(512 + anim.RootPoses.Count * 64L);
        foreach (MotionFrame cycle in anim.Frames)
            estimate = checked(estimate + 96L + cycle.Poses.Count * 64L + cycle.Cues.Count * 192L);
        return Math.Max(rawOctets, estimate);
    }

    private Decoded DecodeFromDat(uint ident)
    {
        byte[]? octets = _rawOctets(ident);
        if (octets is null)
            return new Decoded(null, AbsentEstimate);

        MotionClip anim = Decode(octets, _database);
        if (anim.Id != ident)
            throw new InvalidDataException($"Animation entry 0x{ident:X8} contained id 0x{anim.Id:X8}.");
        return new Decoded(anim, GuessKeptOctets(anim, octets.LongLength));
    }
}
