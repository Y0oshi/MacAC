using System.Collections.Concurrent;
using MacAC.Assets.Pak;

namespace MacAC.Assets;

public sealed partial class PakBakedAssetSource : IBakedAssetSource, IBakedContactSource
{
    private readonly PakScanner _reader;
    private readonly Action<string>? _telemetry;
    private readonly ConcurrentDictionary<ulong, byte> _reportedPersonaFlaws = new();
    private long _sensors;
    private long _reads;
    private long _fetched;
    private long _absent;
    private long _corrupt;

    public PakBakedAssetSource(string trail, BakedCatalogIdentity anticipated, Action<string>? probeDrain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trail);
        _telemetry = probeDrain;
        _reader = new PakScanner(trail);
        try
        {
            DemandMatchingPreamble(trail, _reader.Header, anticipated);
        }
        catch
        {
            _reader.Dispose();
            throw;
        }
    }

    public PakBakedAssetSource(string trail, IDatAccess datFiles, Action<string>? probeDrain = null)
        : this(trail, BakedCatalogIdentity.From(datFiles), probeDrain)
    {
    }

    public BakedAssetSourceStats Stats
    {
        get
        {
            return new(
        Volatile.Read(ref _sensors),
        Volatile.Read(ref _reads),
        Volatile.Read(ref _fetched),
        Volatile.Read(ref _absent),
        Volatile.Read(ref _corrupt));
        }
    }

    public ShelfStats DecodedTextureStashStats => default;

    public long MappedVirtualBytes => _reader.FileLen;

    public BakedAssetPresence Probe(PakAssetKind kind, uint srcFileIdent)
    {
        BakedAssetRequestContract.VetKind(kind);
        Interlocked.Increment(ref _sensors);
        return _reader.InspectListing(PakTag.Compose(kind, srcFileIdent)) switch
        {
            PakListingPhase.Available => BakedAssetPresence.Available,
            PakListingPhase.Corrupt => BakedAssetPresence.Corrupt,
            _ => BakedAssetPresence.Missing,
        };
    }

    public BakedAssetRead Read(in BakedAssetRequest req, CancellationToken abortTicket = default)
    {
        BakedAssetRequestContract.Validate(req);
        abortTicket.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);

        ulong tag = PakTag.Compose(req.Type, req.SourceFileId);
        var verdict = _reader.ScanObjectTriMeshBlob(tag, out HarvestedMesh? blob);
        abortTicket.ThrowIfCancellationRequested();

        if (verdict == PakReadOutcome.Missing)
        {
            Interlocked.Increment(ref _absent);
            return BakedAssetRead.Missing;
        }
        if (verdict == PakReadOutcome.Corrupt || blob is null)
        {
            Interlocked.Increment(ref _corrupt);
            return BakedAssetRead.Corrupt;
        }
        if (!BakedAssetRequestContract.Fits(req, blob))
        {
            Interlocked.Increment(ref _corrupt);
            AnnouncePersonaFlawOnce(
                tag,
                $"prepared payload identity mismatch for key 0x{tag:X16}: " +
                $"expected runtime=0x{req.RuntimeObjectId:X16}, " +
                $"type={req.Type}; payload runtime=0x{blob.ObjectId:X16}, " +
                $"isSetup={blob.IsSetup}");
            return BakedAssetRead.Corrupt;
        }

        Interlocked.Increment(ref _fetched);
        return BakedAssetRead.Loaded(blob);
    }

    public void Dispose() => _reader.Dispose();

    private static void DemandMatchingPreamble(string trail, PakPreamble actual, BakedCatalogIdentity anticipated)
    {
        List<string> mismatches = new List<string>();
        void Verify(string what, uint have, uint want)
        {
            if (have != want)
                mismatches.Add($"{what} {have} != {want}");
        }

        Verify("bake tool", actual.BakeToolVer, anticipated.BakeToolVersion);
        Verify("portal iteration", actual.PortalIteration, anticipated.PortalIteration);
        Verify("cell iteration", actual.CellIteration, anticipated.CellIteration);
        Verify("high-res iteration", actual.HighResIteration, anticipated.HighResIteration);
        Verify("language iteration", actual.LanguageIteration, anticipated.LanguageIteration);

        if (mismatches.Count is not 0)
        {
            throw new InvalidDataException(
                $"prepared asset package '{trail}' doesn't match the installed DAT set: "
                + string.Join("; ", mismatches)
                + ". Re-bake macac.pak with this client build and DAT install");
        }
    }

    private void AnnouncePersonaFlawOnce(ulong tag, string msg)
    {
        if (_reportedPersonaFlaws.TryAdd(tag, 0))
            (_telemetry ?? Console.Error.WriteLine)(msg);
    }
}
