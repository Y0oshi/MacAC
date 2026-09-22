using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Which outdoor land cells a sphere or a set of part boxes overlaps.</summary>
public static partial class CellHop
{

    /// <summary>The land cell under the sphere plus every neighbour its radius reaches into.</summary>
    public static bool AddAllOutsideCells(
        Vector3 realmOrbMiddle,
        float orbRadius,
        uint latestChamberIdent,
        Vector3 latestChunkOrigin,
        ICollection<uint> contenders)
    {
        Vector3 middle = realmOrbMiddle - latestChunkOrigin;
        uint chamberIdent = latestChamberIdent;
        if (!MechLandDefs.TuneToBeyond(ref chamberIdent, ref middle))
            return false;
        if (!MechLandDefs.GidToLcoord(chamberIdent, out int lx, out int ly))
            return false;

        AppendBeyondChamber(contenders, lx, ly);

        float inChamberX = middle.X - MathF.Floor(middle.X / MechLandDefs.ChamberLength) * MechLandDefs.ChamberLength;
        float inChamberY = middle.Y - MathF.Floor(middle.Y / MechLandDefs.ChamberLength) * MechLandDefs.ChamberLength;
        float nearbyRim = orbRadius;
        float farawayRim = MechLandDefs.ChamberLength - orbRadius;
        bool east = inChamberX > farawayRim;
        bool west = inChamberX < nearbyRim;
        bool north = inChamberY > farawayRim;
        bool south = inChamberY < nearbyRim;

        if (east)
        {
            AddAllOutsideCellsBranch2(contenders, lx, ly, north, south);
        }
        if (west)
        {
            AddAllOutsideCellsBranch(contenders, lx, ly, north, south);
        }
        if (north) AppendBeyondChamber(contenders, lx, ly + 1);
        if (south) AppendBeyondChamber(contenders, lx, ly - 1);
        return true;
    }

    private static void AddAllOutsideCellsBranch(ICollection<uint> contenders, int lx, int ly, bool north, bool south)
    {
        AppendBeyondChamber(contenders, lx - 1, ly);
        if (north) AppendBeyondChamber(contenders, lx - 1, ly + 1);
        if (south) AppendBeyondChamber(contenders, lx - 1, ly - 1);
    }

    private static void AddAllOutsideCellsBranch2(ICollection<uint> contenders, int lx, int ly, bool north, bool south)
    {
        AppendBeyondChamber(contenders, lx + 1, ly);
        if (north) AppendBeyondChamber(contenders, lx + 1, ly + 1);
        if (south) AppendBeyondChamber(contenders, lx + 1, ly - 1);
    }

    public static void AddAllOutsideCells(
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        uint latestChamberIdent,
        Vector3 latestChunkOrigin,
        ICollection<uint> contenders)
    {
        int orbTally = NetOrbTally(realmOrbs, countOrbs);
        for (int idx = 0; idx < orbTally; ++idx)
        {
            Orb orb = realmOrbs[idx];
            if (!AddAllOutsideCells(orb.Center, orb.Radius, latestChamberIdent, latestChunkOrigin, contenders))
                break;
        }
    }

    /// <summary>The rectangle of land cells spanned by every part box, anchored on the first part's cell.</summary>
    public static bool AppendAllBeyondChambersFromPieces(
        IReadOnlyList<ProxyPartBox> realmPieces,
        uint latestChamberIdent,
        Vector3 latestChunkOrigin,
        ICollection<uint> contenders)
    {
        if (realmPieces is null)
            return false;

        Vector3 seedCycleSpot = realmPieces[0].WorldLocus - latestChunkOrigin;
        Vector3 baseCycleSpot = seedCycleSpot;
        uint baseChamberIdent = latestChamberIdent;
        if (!MechLandDefs.TuneToBeyond(ref baseChamberIdent, ref baseCycleSpot))
            return false;                                   // gid 0 → get_landcell null → return
        if (!MechLandDefs.GidToLcoord(baseChamberIdent, out int gx, out int gy))
            return false;

        int baseX = (int)(((baseChamberIdent & LoBitmask) - 1u) >> 3);
        int baseY = (int)((baseChamberIdent - 1u) & 7u);
        Vector3 cycleOrigin = latestChunkOrigin - (baseCycleSpot - seedCycleSpot);

        int lowerDX = 0, lowerDY = 0, upperDX = 0, upperDY = 0;
        for (int idx = 0; idx < realmPieces.Count; ++idx)
        {
            realmPieces[idx].RefitTo(cycleOrigin, out Vector3 bboxLower, out Vector3 bboxUpper);

            int a = (int)MathF.Floor(bboxLower.X / MechLandDefs.ChamberLength);
            int b = (int)MathF.Floor(bboxLower.Y / MechLandDefs.ChamberLength);
            int c = (int)MathF.Floor(bboxUpper.X / MechLandDefs.ChamberLength);
            int d = (int)MathF.Floor(bboxUpper.Y / MechLandDefs.ChamberLength);

            lowerDX = Math.Min(lowerDX, a - baseX);
            lowerDY = Math.Min(lowerDY, b - baseY);
            upperDX = Math.Max(upperDX, c - baseX);
            upperDY = Math.Max(upperDY, d - baseY);
        }

        AppendChamberChunk(gx + lowerDX, gy + lowerDY, gx + upperDX, gy + upperDY, contenders);
        return true;
    }
    private static void AppendBeyondChamber(ICollection<uint> contenders, int lx, int ly)
    {
        uint gid = MechLandDefs.LcoordToGid(lx, ly);
        if (gid is not 0u)
            contenders.Add(gid);
    }

    private static void AppendChamberChunk(int x0, int y0, int x1, int y1, ICollection<uint> contenders)
    {
        for (int x = x0; x <= x1; ++x)
        {
            for (int y = y0; y <= y1; ++y)
                AppendBeyondChamber(contenders, x, y);
        }
    }
}
