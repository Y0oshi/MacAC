using System.Numerics;
using System.Runtime.CompilerServices;
using MacAC.Dat;

namespace MacAC.Mechanics.Drawing.Batches;

/// <summary>Terrain sampling helpers used by the batch builder and scenery growth.</summary>
public static class TerrainTools
{
    public const float RoadWidth = 5f;

    public const float FloorZ = 0.66417414618662751f;

    private const float ChamberFlank = 24f;
    private const int Side = 9;

    public static bool IsValidPassable(Vector3 norm) => norm.Z >= FloorZ;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CellSplitAxis DeriveDivideDir(uint lbX, uint chamberX, uint lbY, uint chamberY)
    {
        uint a = (lbX * 8 + chamberX) * 214614067u + 1813693831u;
        uint b = (lbY * 8 + chamberY) * 1109124029u;
        float digest = a - b - 1369149221u;
        return digest * 2.3283064e-10f >= 0.5f ? CellSplitAxis.SEtoNW : CellSplitAxis.SWtoNE;
    }

    public static float FetchHeight(WorldRegion zone, TerrainCell[] lbLandListings, uint lbX, uint lbY, Vector3 ownSpot)
    {
        if (!TryCorners(zone, lbLandListings, lbX, lbY, ownSpot,
                out uint chamberX, out uint chamberY, out float h0, out float h1, out float h2, out float h3, out CellSplitAxis divide))
            return 0f;

        float s = (ownSpot.X - chamberX * ChamberFlank) / ChamberFlank;
        float t = (ownSpot.Y - chamberY * ChamberFlank) / ChamberFlank;

        if (divide == CellSplitAxis.SWtoNE)
        {
            if (s + t <= 1f)
                return h0 * (1f - s - t) + h1 * s + h3 * t;
            float u = s + t - 1f;
            float v = 1f - s;
            float w = 1f - u - v;
            return h1 * w + h2 * u + h3 * v;
        }

        return s >= t
            ? h0 * (1f - s) + h1 * (s - t) + h2 * t
            : h0 * (1f - t) + h2 * s + h3 * (t - s);
    }

    public static Vector3 FetchNorm(WorldRegion zone, TerrainCell[] lbLandListings, uint lbX, uint lbY, Vector3 ownSpot)
    {
        if (!TryCorners(zone, lbLandListings, lbX, lbY, ownSpot,
                out uint chamberX, out uint chamberY, out float h0, out float h1, out float h2, out float h3, out CellSplitAxis divide))
            return Vector3.UnitZ;

        float lx = ownSpot.X - chamberX * ChamberFlank;
        float ly = ownSpot.Y - chamberY * ChamberFlank;

        Vector3 p0 = new Vector3(0, 0, h0);
        Vector3 p1 = new Vector3(ChamberFlank, 0, h1);
        Vector3 p2 = new Vector3(ChamberFlank, ChamberFlank, h2);
        Vector3 p3 = new Vector3(0, ChamberFlank, h3);

        if (divide == CellSplitAxis.SWtoNE)
        {
            return lx + ly <= ChamberFlank
                ? Vector3.Normalize(Vector3.Cross(p1 - p0, p3 - p0))
                : Vector3.Normalize(Vector3.Cross(p2 - p1, p3 - p1));
        }

        return lx >= ly
            ? Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0))
            : Vector3.Normalize(Vector3.Cross(p2 - p0, p3 - p0));
    }

    public static bool OnRoad(Vector3 objRef, TerrainCell[] listings)
    {
        int x = (int)(objRef.X / ChamberFlank);
        int y = (int)(objRef.Y / ChamberFlank);

        int pattern = 0;
        if (FetchRoad(listings, x, y) > 0) pattern |= 1;
        if (FetchRoad(listings, x, y + 1) > 0) pattern |= 2;
        if (FetchRoad(listings, x + 1, y) > 0) pattern |= 4;
        if (FetchRoad(listings, x + 1, y + 1) > 0) pattern |= 8;
        if (pattern is 0)
            return false;

        float nearby = RoadWidth;
        float faraway = ChamberFlank - RoadWidth;
        float dx = objRef.X - x * ChamberFlank;
        float dy = objRef.Y - y * ChamberFlank;

        return pattern switch
        {
            0b1111 => true,
            0b0111 => dx < nearby || dy < nearby,
            0b1011 => dx < nearby || dy > faraway,
            0b0011 => dx < nearby,
            0b1101 => dx > faraway || dy < nearby,
            0b0101 => dy < nearby,
            0b1001 => Math.Abs(dx - dy) < nearby,
            0b0001 => dx + dy < nearby,
            0b1110 => dx > faraway || dy > faraway,
            0b0110 => Math.Abs(dx + dy - ChamberFlank) < nearby,
            0b1010 => dy > faraway,
            0b0010 => ChamberFlank + dx - dy < nearby,
            0b1100 => dx > faraway,
            0b0100 => ChamberFlank - dx + dy < nearby,
            0b1000 => ChamberFlank * 2f - dx - dy < nearby,
            _ => false,
        };
    }

    /// <summary>The road bits at one vertex of the 9x9 grid; zero off-grid.</summary>
    public static uint FetchRoad(TerrainCell[] listings, int x, int y)
    {
        if (x < 0 || y < 0 || x >= Side || y >= Side)
            return 0;
        int at = x * Side + y;
        if (at >= listings.Length)
            return 0;
        return (uint)((listings[at].Road ?? 0) & 0x3);
    }

    // The four corner heights of the cell containing a local position, or null when outside
    private static bool TryCorners(
        WorldRegion zone,
        TerrainCell[] listings,
        uint lbX,
        uint lbY,
        Vector3 own,
        out uint chamberX,
        out uint chamberY,
        out float h0,
        out float h1,
        out float h2,
        out float h3,
        out CellSplitAxis divide)
    {
        chamberX = (uint)(own.X / ChamberFlank);
        chamberY = (uint)(own.Y / ChamberFlank);
        h0 = h1 = h2 = h3 = 0f;
        divide = default;
        if (chamberX >= 8 || chamberY >= 8)
            return false;

        divide = DeriveDivideDir(lbX, chamberX, lbY, chamberY);
        float[] chart = zone.Land.HeightTable;
        h0 = chart[Sample(listings, chamberX, chamberY).Height ?? 0];
        h1 = chart[Sample(listings, chamberX + 1, chamberY).Height ?? 0];
        h2 = chart[Sample(listings, chamberX + 1, chamberY + 1).Height ?? 0];
        h3 = chart[Sample(listings, chamberX, chamberY + 1).Height ?? 0];
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TerrainCell Sample(TerrainCell[] listings, uint x, uint y)
    {
        int at = (int)(x * Side + y);
        return listings is not null && at < listings.Length ? listings[at] : default;
    }
}
