namespace MacAC.Assets.Pak;

/// <summary>The kinds of payload a pak holds; the top byte of every key.</summary>
public enum PakAssetKind : byte
{
    GfxObjMesh = 1,
    SetupMesh = 2,
    EnvCellMesh = 3,
    GfxObjCollision = 4,
    SetupCollision = 5,
    CellStructureCollision = 6,
    EnvCellTopology = 7,
    TexturePayload = 8,
}

/// <summary>64-bit pak keys: type in the top byte, the file id (or an opaque payload id) below it.</summary>
public static class PakTag
{
    private const int KindShift = 56;
    private const int FileIdentShift = 24;
    private const ulong KindBitmask = 0xFF00_0000_0000_0000ul;

    public static ulong Compose(PakAssetKind kind, uint fileIdent) => ((ulong)kind << KindShift) | ((ulong)fileIdent << FileIdentShift);

    public static (PakAssetKind Type, uint FileId) Decompose(ulong tag)
    {
        return ((PakAssetKind)(byte)(tag >> KindShift), (uint)((tag >> FileIdentShift) & 0xFFFFFFFFu));
    }

    public static ulong ConstructSolid(PakAssetKind kind, ulong payloadId)
    {
        if ((payloadId & KindBitmask) is not 0)
            throw new ArgumentOutOfRangeException(nameof(payloadId));
        return ((ulong)kind << KindShift) | payloadId;
    }
}
