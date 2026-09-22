using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Whole cell sets: the shadow footprint of an object, and the cell that contains a mover.</summary>
public static partial class CellHop
{
    public static IReadOnlyList<uint> AssembleShadeChamberSet(
        KineticAssetCache stash,
        uint seedChamberIdent,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        bool isStatic)
    {
        ChamberArray contenders = new ChamberArray();
        int orbTally = NetOrbTally(realmOrbs, countOrbs);
        if (seedChamberIdent is 0 || orbTally is 0)
            return contenders.SequencedIdents;

        bool insideSeed = IsInside(seedChamberIdent);
        stash.ChamberGraph.TryFetchLandOrigin(seedChamberIdent, out Vector3 chunkOrigin);

        bool seedFetched;
        if (insideSeed)
        {
            contenders.Add(seedChamberIdent);
            seedFetched = stash.FetchChamberStruct(seedChamberIdent) is not null;
        }
        else
        {
            AddAllOutsideCells(realmOrbs, orbTally, seedChamberIdent, chunkOrigin, contenders);
            seedFetched = stash.ChamberGraph.ObtainShown(seedChamberIdent) is not null;
        }
        if (!seedFetched)
            return contenders.SequencedIdents;

        // Breadth-first over the growing candidate list.
        bool exteriorAdded = !insideSeed;
        for (int idx = 0; idx < contenders.Count; ++idx)
        {
            uint chamberIdent = contenders.SequencedIdents[idx];
            if (IsInside(chamberIdent))
            {
                var chamber = stash.FetchChamberStruct(chamberIdent);
                if (chamber is null)
                    continue;

                FindTransitCellsSphere(stash, chamber, chamberIdent, realmOrbs, orbTally, contenders, out bool quitStraddle);
                if (quitStraddle && !exteriorAdded)
                {
                    AddAllOutsideCells(realmOrbs, orbTally, seedChamberIdent, chunkOrigin, contenders);
                    exteriorAdded = true;
                }
            }
            else
            {
                if (stash.ChamberGraph.ObtainShown(chamberIdent) is null)
                    continue;

                if (!exteriorAdded)
                {
                    AddAllOutsideCells(realmOrbs, orbTally, seedChamberIdent, chunkOrigin, contenders);
                    exteriorAdded = true;
                }

                var structure = stash.GetBuilding(chamberIdent);
                if (structure is not null)
                    CheckBuildingTransit(stash, structure, realmOrbs, orbTally, contenders, out _);
            }
        }

        if (isStatic && insideSeed && stash.FetchChamberStruct(seedChamberIdent) is { } seedChamber)
            PruneToShownFromSeed(contenders, seedChamberIdent, seedChamber);

        return contenders.SequencedIdents;
    }

    public static IReadOnlyList<uint> AssembleShadeChamberSetFromPieces(
        KineticAssetCache stash,
        uint seedChamberIdent,
        IReadOnlyList<ProxyPartBox> realmPieces,
        IReadOnlyList<Orb> realmPieceOrbs,
        bool isStatic)
    {
        ChamberArray contenders = new ChamberArray();
        if (seedChamberIdent is 0u || realmPieces is null || realmPieces.Count is 0)
            return contenders.SequencedIdents;

        int orbTally = NetOrbTally(realmPieceOrbs, realmPieceOrbs?.Count ?? 0);
        bool insideSeed = IsInside(seedChamberIdent);
        stash.ChamberGraph.TryFetchLandOrigin(seedChamberIdent, out Vector3 chunkOrigin);

        bool exteriorAdded = false;
        bool seedFetched;
        contenders.Add(seedChamberIdent);
        if (insideSeed)
        {
            seedFetched = stash.FetchChamberStruct(seedChamberIdent) is not null;
        }
        else
        {
            exteriorAdded = AppendAllBeyondChambersFromPieces(realmPieces, seedChamberIdent, chunkOrigin, contenders);
            seedFetched = stash.ChamberGraph.ObtainShown(seedChamberIdent) is not null;
        }
        if (!seedFetched)
            return contenders.SequencedIdents;

        for (int idx = 0; idx < contenders.Count; ++idx)
        {
            uint chamberIdent = contenders.SequencedIdents[idx];
            if (IsInside(chamberIdent))
            {
                var chamber = stash.FetchChamberStruct(chamberIdent);
                if (chamber is null || orbTally is 0 || realmPieces.Count is 0)
                    continue;

                SeekPassageChambersBbox(stash, chamber, chamberIdent, realmPieces, realmPieceOrbs!, contenders, out bool quitStraddle);
                if (quitStraddle && !exteriorAdded)
                    exteriorAdded = AppendAllBeyondChambersFromPieces(realmPieces, seedChamberIdent, chunkOrigin, contenders);
            }
            else
            {
                if (stash.ChamberGraph.ObtainShown(chamberIdent) is null)
                    continue;

                if (!exteriorAdded)
                    exteriorAdded = AppendAllBeyondChambersFromPieces(realmPieces, seedChamberIdent, chunkOrigin, contenders);

                var structure = stash.GetBuilding(chamberIdent);
                if (structure is not null && orbTally > 0)
                    CheckBuildingTransitFromParts(stash, structure, realmPieces, realmPieceOrbs!, contenders, seedChamberIdent, chunkOrigin, ref exteriorAdded);
            }
        }

        return contenders.SequencedIdents;
    }

    public static uint FindCellList(KineticAssetCache stash, Vector3 realmOrbMiddle, float orbRadius, uint latestChamberIdent)
    {
        return FindCellSet(stash, realmOrbMiddle, orbRadius, latestChamberIdent, out _);
    }

    public static uint FindCellSet(
        KineticAssetCache stash,
        Vector3 realmOrbMiddle,
        float orbRadius,
        uint latestChamberIdent,
        out IReadOnlyCollection<uint> chamberSet,
        Vector3? carriedChunkOrigin = null)
    {
        Orb[] orbs = One(realmOrbMiddle, orbRadius);
        return FindCellSet(stash, orbs, orbs.Length, latestChamberIdent, out chamberSet, carriedChunkOrigin);
    }

    public static uint FindCellSet(
        KineticAssetCache stash,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        uint latestChamberIdent,
        out IReadOnlyCollection<uint> chamberSet,
        Vector3? carriedChunkOrigin = null)
    {
        ChamberArray contenders = new ChamberArray();
        uint containing = Locate(stash, realmOrbs, countOrbs, latestChamberIdent, carriedChunkOrigin, contenders, out _);
        chamberSet = contenders;
        return containing;
    }

    internal static uint FindCellSet(
        KineticAssetCache stash,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        uint latestChamberIdent,
        ChamberArray contenders,
        Vector3? carriedChunkOrigin = null)
    {
        return Locate(stash, realmOrbs, countOrbs, latestChamberIdent, carriedChunkOrigin, contenders, out _);
    }

    internal static uint FindCellSet(
        KineticAssetCache stash,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        uint latestChamberIdent,
        ChamberArray contenders,
        Vector3? carriedChunkOrigin,
        out bool containingChamberLocated)
    {
        return Locate(stash, realmOrbs, countOrbs, latestChamberIdent, carriedChunkOrigin, contenders, out containingChamberLocated);
    }

    private static void PruneToShownFromSeed(ChamberArray contenders, uint seedChamberIdent, CellKinetics seedChamber)
    {
        List<uint> keep = new List<uint>(contenders.Count);
        foreach (uint ident in contenders.SequencedIdents)
        {
            if (ident == seedChamberIdent || seedChamber.VisibleCellIds.Contains(ident))
                keep.Add(ident);
        }
        if (keep.Count == contenders.Count)
            return;

        contenders.Clear();
        foreach (uint ident in keep)
            contenders.Add(ident);
    }

    // Builds the reachable cell set for the spheres, then picks the cell containing the first sphere's
    // centre
    private static uint Locate(
        KineticAssetCache stash,
        IReadOnlyList<Orb> realmOrbs,
        int countOrbs,
        uint latestChamberIdent,
        Vector3? carriedChunkOrigin,
        ChamberArray contenders,
        out bool containingChamberLocated)
    {
        containingChamberLocated = true;
        contenders.Clear();
        int orbTally = NetOrbTally(realmOrbs, countOrbs);
        if (orbTally is 0)
            return latestChamberIdent;

        Vector3 middle = realmOrbs[0].Center;
        float radius = realmOrbs[0].Radius;
        bool insideBegin = IsInside(latestChamberIdent);

        Vector3 chunkOrigin;
        if (carriedChunkOrigin is { } carried)
        {
            chunkOrigin = carried;
        }
        else if (!stash.ChamberGraph.TryFetchLandOrigin(latestChamberIdent, out chunkOrigin) && !insideBegin)
        {
            return latestChamberIdent;
        }

        bool exteriorChooseAllowed = !insideBegin;
        bool exteriorAdded;
        if (insideBegin)
        {
            if (stash.FetchChamberStruct(latestChamberIdent) is null)
                return latestChamberIdent;
            contenders.Add(latestChamberIdent);
            exteriorAdded = false;
        }
        else
        {
            AddAllOutsideCells(realmOrbs, orbTally, latestChamberIdent, chunkOrigin, contenders);
            exteriorAdded = true;
        }

        for (int idx = 0; idx < contenders.Count; ++idx)
        {
            uint chamberIdent = contenders.SequencedIdents[idx];
            if (!IsInside(chamberIdent))
            {
                if (stash.ChamberGraph.ObtainShown(chamberIdent) is null)
                    continue;
                if (stash.GetBuilding(chamberIdent) is { } structure)
                    CheckBuildingTransit(stash, structure, realmOrbs, orbTally, contenders, out _);
                continue;
            }

            var chamber = stash.FetchChamberStruct(chamberIdent);
            if (chamber is null)
                continue;

            FindTransitCellsSphere(stash, chamber, chamberIdent, realmOrbs, orbTally, contenders, out bool quitStraddle);
            exteriorChooseAllowed |= quitStraddle;
            if (quitStraddle && !exteriorAdded)
            {
                AddAllOutsideCells(realmOrbs, orbTally, latestChamberIdent, chunkOrigin, contenders);
                exteriorAdded = true;
            }
        }

        if (KineticTelemetry.ProbeCellSetEnabled)
            KineticTelemetry.TraceChamberSetAssemble(latestChamberIdent, middle, contenders);

        // The land cell directly under the centre is the only outdoor cell that may win.
        uint landChamberUnderMiddle = 0u;
        {
            Vector3 chooseSpot = middle - chunkOrigin;
            uint chooseChamber = latestChamberIdent;
            if (MechLandDefs.TuneToBeyond(ref chooseChamber, ref chooseSpot))
                landChamberUnderMiddle = chooseChamber;
        }

        uint exteriorChoose = 0u;
        foreach (uint contender in contenders.SequencedIdents)
        {
            if (IsInside(contender))
            {
                if (PtInChamber(stash, stash.FetchChamberStruct(contender), middle))
                    return contender;
            }
            else if (exteriorChoose is 0u
                && landChamberUnderMiddle is not 0u
                && exteriorChooseAllowed
                && stash.ChamberGraph.ObtainShown(contender) is not null
                && contender == landChamberUnderMiddle)
            {
                exteriorChoose = contender;
            }
        }
        if (exteriorChoose is not 0u)
            return exteriorChoose;

        // Nothing claimed the point. If the seed cell rejects the sphere too,
        // try its stab list before conceding.
        if (insideBegin && stash.FetchChamberStruct(latestChamberIdent) is { } seed && ContactSweep.HasCellContainment(stash, seed))
        {
            Vector3 seedOwn = Vector3.Transform(middle, seed.InverseWorldTransform);
            if (!ContactSweep.SphereIntersectsCell(stash, seed, seedOwn, radius))
            {
                uint recovered = FindVisibleChildCell(stash, latestChamberIdent, middle, useStabRoster: true, contenders.UnionMark);
                if (recovered is not 0u && recovered != latestChamberIdent)
                    return recovered;
                containingChamberLocated = false;
            }
        }

        return latestChamberIdent;
    }
}
