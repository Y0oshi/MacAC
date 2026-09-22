using System.Numerics;
using MacAC.Assets.Pak;
using MacAC.Dat;
using Microsoft.Extensions.Logging;
using DatEnvironment =  MacAC.Dat.InteriorShell;

namespace MacAC.Assets;

public sealed class DatBakedAssetSource : IBakedAssetSource
{
    private const uint SurroundingsDidStem = 0x0D00_0000u;

    private readonly IDatAccess _datFiles;
    private readonly MeshHarvester _harvester;
    private long _sensors;
    private long _reads;
    private long _fetched;
    private long _absent;

    public DatBakedAssetSource(IDatAccess datFiles, ILogger logger, Action<HarvestedMesh>? flankLinedDrain = null)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(logger);
        _datFiles = datFiles;
        _harvester = new MeshHarvester(datFiles, logger, flankLinedDrain);
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
        0);
        }
    }

    public ShelfStats DecodedTextureStashStats => _harvester.DecodedTextureCacheStats;

    public BakedAssetPresence Probe(PakAssetKind kind, uint srcFileIdent)
    {
        BakedAssetRequestContract.VetKind(kind);
        Interlocked.Increment(ref _sensors);

        RecordKind wanted = DatKindFor(kind);
        if (wanted == RecordKind.Unknown)
            return BakedAssetPresence.Missing;
        bool present = _datFiles.TryLocatePreferred(srcFileIdent, out _, out RecordKind actual) && actual == wanted;
        return present ? BakedAssetPresence.Available : BakedAssetPresence.Missing;
    }

    public BakedAssetRead Read(in BakedAssetRequest req, CancellationToken abortTicket = default)
    {
        BakedAssetRequestContract.Validate(req);
        abortTicket.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);

        HarvestedMesh? blob = req.Type == PakAssetKind.EnvCellMesh
            ? HarvestEnvironChamber(req, abortTicket)
            : _harvester.Harvest(req.SourceFileId, req.Type == PakAssetKind.SetupMesh, abortTicket);

        abortTicket.ThrowIfCancellationRequested();
        if (blob is null)
        {
            Interlocked.Increment(ref _absent);
            return BakedAssetRead.Missing;
        }
        if (!BakedAssetRequestContract.Fits(req, blob))
        {
            throw new InvalidDataException(
                $"live-DAT prepared payload identity mismatch: wanted " +
                $"runtime=0x{req.RuntimeObjectId:X16}, type={req.Type}; " +
                $"payload runtime=0x{blob.ObjectId:X16}, isSetup={blob.IsSetup}");
        }

        Interlocked.Increment(ref _fetched);
        return BakedAssetRead.Loaded(blob);
    }

    /// <summary>Nothing to release: the dats are owned by the caller.</summary>
    public void Dispose()
    {
    }

    private static RecordKind DatKindFor(PakAssetKind kind)
    {
        return kind switch
        {
            PakAssetKind.GfxObjMesh => RecordKind.GfxObj,
            PakAssetKind.SetupMesh => RecordKind.Setup,
            PakAssetKind.EnvCellMesh => RecordKind.EnvCell,
            _ => RecordKind.Unknown,
        };
    }

    private HarvestedMesh? HarvestEnvironChamber(in BakedAssetRequest request, CancellationToken abortTicket)
    {
        BakedEnvCellSchema schema = request.EnvCell
            ?? throw new ArgumentException("EnvCell prepared requests must retain their source schema", nameof(request));

        uint surroundingsIdent = SurroundingsDidStem | schema.EnvironmentId;
        if (!_datFiles.Portal.TryGet<DatEnvironment>(surroundingsIdent, out DatEnvironment? surroundings)
            || surroundings is null
            || !surroundings.Cells.TryGetValue(schema.CellStructure, out ShellCell? chamberStruct)
            || chamberStruct is null)

            return null;

        return _harvester.HarvestChamberStruct(request.RuntimeObjectId, chamberStruct, schema.Surfaces, Matrix4x4.Identity, abortTicket);
    }
}
