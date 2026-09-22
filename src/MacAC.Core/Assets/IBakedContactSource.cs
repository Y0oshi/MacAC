using MacAC.Assets.Pak;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

public readonly record struct BakedContactSourceStats(long Probes, long Reads, long Loaded, long Missing, long Corrupt)
{
    public static BakedContactSourceStats operator +(BakedContactSourceStats a, BakedContactSourceStats b) =>
        new(a.Probes + b.Probes, a.Reads + b.Reads, a.Loaded + b.Loaded, a.Missing + b.Missing, a.Corrupt + b.Corrupt);
}

/// <summary>The outcome of reading one packed collision payload: loaded data, or why there is none.</summary>
public readonly record struct BakedContactRead<T>(BakedAssetReadStatus Status, T? Data) where T : class
{
    public static BakedContactRead<T> Missing => new(BakedAssetReadStatus.Missing, null);

    public static BakedContactRead<T> Corrupt => new(BakedAssetReadStatus.Corrupt, null);

    public static BakedContactRead<T> Loaded(T blob) => new(BakedAssetReadStatus.Loaded, blob);
}

/// <summary>Where packed (baked) collision assets come from.</summary>
public interface IBakedContactSource : IDisposable
{
    BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent);

    BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default);

    BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default);

    BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default);

    BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default);

    BakedContactSourceStats ImpactStats { get; }
}

internal static class BakedContactTypeContract
{
    public static void Validate(PakAssetKind type)
    {
        bool impact = type is PakAssetKind.GfxObjCollision or PakAssetKind.SetupCollision
            or PakAssetKind.CellStructureCollision or PakAssetKind.EnvCellTopology;
        if (!impact)
            throw new ArgumentOutOfRangeException(nameof(type), type, "unrecognized prepared collision asset type");
    }
}

public sealed partial class PakBakedAssetSource
{
    private long _linkSensors;
    private long _linkReads;
    private long _linkFetched;
    private long _linkAbsent;
    private long _linkCorrupt;

    public BakedContactSourceStats ImpactStats
    {
        get
        {
            return new(
        Volatile.Read(ref _linkSensors),
        Volatile.Read(ref _linkReads),
        Volatile.Read(ref _linkFetched),
        Volatile.Read(ref _linkAbsent),
        Volatile.Read(ref _linkCorrupt));
        }
    }

    public BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent)
    {
        BakedContactTypeContract.Validate(kind);
        Interlocked.Increment(ref _linkSensors);
        return _reader.InspectListing(PakTag.Compose(kind, srcFileIdent)) switch
        {
            PakListingPhase.Available => BakedAssetPresence.Available,
            PakListingPhase.Corrupt => BakedAssetPresence.Corrupt,
            _ => BakedAssetPresence.Missing,
        };
    }

    public BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return ScanLink(PakAssetKind.GfxObjCollision, srcFileIdent, static (octets, ticket) => PackedContactCodec.DeserializeGfxObjRef(octets, ticket), abortTicket);
    }

    public BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return ScanLink(PakAssetKind.SetupCollision, srcFileIdent, static (octets, ticket) => PackedContactCodec.DeserializeRig(octets, ticket), abortTicket);
    }

    public BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return ScanLink(PakAssetKind.CellStructureCollision, srcFileIdent, static (octets, ticket) => PackedContactCodec.DeserializeChamberStructure(octets, ticket), abortTicket);
    }

    public BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return ScanLink(PakAssetKind.EnvCellTopology, srcFileIdent, static (octets, ticket) => PackedContactCodec.DeserializeEnvironChamberWiring(octets, ticket), abortTicket);
    }

    private BakedContactRead<T> ScanLink<T>(PakAssetKind kind, uint srcFileIdent, Func<byte[], CancellationToken, T> unpack, CancellationToken abortTicket)
        where T : class
    {
        BakedContactTypeContract.Validate(kind);
        abortTicket.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _linkReads);

        ulong tag = PakTag.Compose(kind, srcFileIdent);
        var verdict = _reader.ScanBlobOctets(tag, out byte[]? octets);
        abortTicket.ThrowIfCancellationRequested();
        if (verdict == PakReadOutcome.Missing)
        {
            Interlocked.Increment(ref _linkAbsent);
            return BakedContactRead<T>.Missing;
        }
        if (verdict == PakReadOutcome.Corrupt || octets is null)
        {
            Interlocked.Increment(ref _linkCorrupt);
            return BakedContactRead<T>.Corrupt;
        }

        try
        {
            T blob = unpack(octets, abortTicket);
            abortTicket.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _linkFetched);
            return BakedContactRead<T>.Loaded(blob);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The CRC matched, so the bytes are what was baked; the codec itself rejected them.
            _reader.FlagCargoCorrupt(tag, $"collision deserialization failed despite matching CRC: {exception.GetType().Name}: {exception.Message}");
            Interlocked.Increment(ref _linkCorrupt);
            return BakedContactRead<T>.Corrupt;
        }
    }
}
