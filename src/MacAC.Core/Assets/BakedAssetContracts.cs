using MacAC.Assets.Pak;

namespace MacAC.Assets;

public enum BakedAssetPresence
{
    Missing,
    Available,
    Corrupt,
}

public enum BakedAssetReadStatus
{
    Missing,
    Loaded,
    Corrupt,
}

/// <summary>What an EnvCell mesh was baked from, so a live-dat source can rebuild it.</summary>
public sealed record BakedEnvCellSchema(uint EnvironmentId, ushort CellStructure, IReadOnlyList<ushort> Surfaces);

public readonly record struct BakedAssetRequest(PakAssetKind Type, uint SourceFileId, ulong RuntimeObjectId, BakedEnvCellSchema? EnvCell = null)
{
    public static BakedAssetRequest GfxObj(uint fileIdent) => new(PakAssetKind.GfxObjMesh, fileIdent, fileIdent);

    public static BakedAssetRequest Setup(uint fileIdent) => new(PakAssetKind.SetupMesh, fileIdent, fileIdent);

    public static BakedAssetRequest EnvCellGeometry(uint srcChamberIdent, ulong coreGeoIdent, uint surroundingsIdent, ushort chamberStructure, IReadOnlyList<ushort> canvases)
    {
        return new(PakAssetKind.EnvCellMesh, srcChamberIdent, coreGeoIdent, new BakedEnvCellSchema(surroundingsIdent, chamberStructure, canvases));
    }
}

public readonly record struct BakedAssetRead(BakedAssetReadStatus Status, HarvestedMesh? Data)
{
    public static BakedAssetRead Missing => new(BakedAssetReadStatus.Missing, null);

    public static BakedAssetRead Corrupt => new(BakedAssetReadStatus.Corrupt, null);

    public static BakedAssetRead Loaded(HarvestedMesh blob) => new(BakedAssetReadStatus.Loaded, blob);
}

public readonly record struct BakedAssetSourceStats(long Probes, long Reads, long Loaded, long Missing, long Corrupt)
{
    public static BakedAssetSourceStats operator +(BakedAssetSourceStats a, BakedAssetSourceStats b) =>
        new(a.Probes + b.Probes, a.Reads + b.Reads, a.Loaded + b.Loaded, a.Missing + b.Missing, a.Corrupt + b.Corrupt);
}

public readonly record struct BakedCatalogIdentity(uint PortalIteration, uint CellIteration, uint HighResIteration, uint LanguageIteration, uint BakeToolVersion)
{
    public static BakedCatalogIdentity From(IDatAccess datFiles) => From(datFiles, PakFmt.LatestBakeToolVer);

    public static BakedCatalogIdentity From(IDatAccess datFiles, uint bakeToolVersion)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        if (bakeToolVersion is 0)
            throw new ArgumentOutOfRangeException(nameof(bakeToolVersion));

        return new(
            checked((uint)datFiles.GatewayIteration),
            checked((uint)datFiles.ChamberIteration),
            checked((uint)datFiles.HiResIteration),
            checked((uint)datFiles.LanguageIteration),
            bakeToolVersion);
    }
}

/// <summary>Where baked meshes come from: a pak, the live dats, or layers of both.</summary>
public interface IBakedAssetSource : IDisposable
{
    BakedAssetPresence Probe(PakAssetKind kind, uint srcFileIdent);

    BakedAssetRead Read(in BakedAssetRequest req, CancellationToken abortTicket = default);

    BakedAssetSourceStats Stats { get; }

    ShelfStats DecodedTextureStashStats { get; }

    long MappedVirtualBytes => 0;
}

internal static class BakedAssetRequestContract
{
    public static void VetKind(PakAssetKind type)
    {
        if (type is not (PakAssetKind.GfxObjMesh or PakAssetKind.SetupMesh or PakAssetKind.EnvCellMesh))
            throw new ArgumentOutOfRangeException(nameof(type), type, "unrecognized prepared asset type");
    }

    public static void Validate(in BakedAssetRequest request)
    {
        VetKind(request.Type);
        if (request.Type == PakAssetKind.EnvCellMesh && request.EnvCell is null)
            throw new ArgumentException("EnvCell prepared requests must retain their source schema", nameof(request));
    }

    // A payload answers a request only if it carries the runtime id and setup-ness that were asked for
    public static bool Fits(in BakedAssetRequest req, HarvestedMesh blob)
    {
        return blob.ObjectId == req.RuntimeObjectId && blob.IsSetup == (req.Type == PakAssetKind.SetupMesh);
    }
}
