using MacAC.Dat;
using MacAC.Mechanics.Data;

namespace MacAC.Mechanics.Geometry;

public static class CellTessellation
{
    private const uint CanvasIdentBase = 0x08000000u;

    /// <summary>True when at least one polygon face uses a textured surface.</summary>
    public static bool HasDrawableGeo(RoomCell environChamber, ShellCell chamberStruct, IDatRecordSource datFiles)
    {
        var textured = new Dictionary<int, bool>();

        bool SocketIsTextured(int socket)
        {
            if (textured.TryGetValue(socket, out bool recognized))
                return recognized;
            bool outcome = socket >= 0
                && socket < environChamber.SkinIds.Count
                && datFiles.Get<Skin>(CanvasIdentBase | environChamber.SkinIds[socket]) is { } canvas
                && !CanonBareSurfacePolicy.IsUntextured(canvas.Bits);
            textured[socket] = outcome;
            return outcome;
        }

        foreach (Facet poly in chamberStruct.Facets.Values)
        {
            // The same degenerate-fan gate the mesh extractor applies.
            if (poly.VertexIds.Count < 3)
                continue;

            foreach (CellStructSideOption flank in CellStructSideOptions.FetchContenders((int)poly.Culling))
            {
                short slot = flank.SurfaceSlot == CellStructFacetSide.Positive ? poly.FrontSurface : poly.BackSurface;
                if (slot >= 0 && SocketIsTextured(slot))
                    return true;
            }
        }
        return false;
    }
}
