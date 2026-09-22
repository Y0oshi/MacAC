using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Geometry;

public static class GfxObjLodResolver
{
    public static bool TryResolveCloseGfxObj(IDatRecordSource datFiles, uint gfxObjRefIdent, out uint settledIdent, out PartMesh? settledGfxObjRef)
    {
        return TryResolveCloseGfxObj(datFiles.Get<PartMesh>, datFiles.Get<LodTable>, gfxObjRefIdent, out settledIdent, out settledGfxObjRef);
    }

    public static bool TryResolveCloseGfxObj(
        Func<uint, PartMesh?> fetchGfxObjRef,
        Func<uint, LodTable?> fetchDowngradeDetails,
        uint gfxObjRefIdent,
        out uint settledIdent,
        out PartMesh? settledGfxObjRef)
    {
        settledIdent = gfxObjRefIdent;
        settledGfxObjRef = fetchGfxObjRef(gfxObjRefIdent);
        if (settledGfxObjRef is null)
            return false;

        if (DowngradeChain(settledGfxObjRef, fetchDowngradeDetails) is not { Levels.Count: > 0 } chain)
            return true;

        uint shutIdent = (uint)chain.Levels[0].PartMeshId;
        if (shutIdent is 0 || fetchGfxObjRef(shutIdent) is not { } shut)
            return true;

        settledIdent = shutIdent;
        settledGfxObjRef = shut;
        return true;
    }

    public static bool IsRuntimeHiddenMarker(IDatRecordSource datFiles, uint gfxObjRefIdent)
    {
        return IsRuntimeHiddenMarker(datFiles.Get<PartMesh>, datFiles.Get<LodTable>, gfxObjRefIdent);
    }

    public static bool IsRuntimeHiddenMarker(
        Func<uint, PartMesh?> fetchGfxObjRef,
        Func<uint, LodTable?> fetchDowngradeDetails,
        uint gfxObjRefIdent)
    {
        if (fetchGfxObjRef(gfxObjRefIdent) is not { } gfxObjRef)
            return false;
        if (DowngradeChain(gfxObjRef, fetchDowngradeDetails) is not { Levels.Count: > 0 } chain)
            return false;
        if (chain.Levels[0].MaxDist != 0f)
            return false;
        foreach (LodLevel socket in chain.Levels)
        {
            if ((uint)socket.PartMeshId is 0u)
                return true;
        }
        return false;
    }

    private static LodTable? DowngradeChain(PartMesh gfxObjRef, Func<uint, LodTable?> fetchDowngradeDetails)
    {
        return gfxObjRef.Bits.HasFlag(PartMeshBits.HasDIDDegrade) && gfxObjRef.LodTableId is not 0
            ? fetchDowngradeDetails(gfxObjRef.LodTableId)
            : null;
    }
}
