using System.Numerics;
using MacAC.Dat;

namespace MacAC.Client.Graphics.Batching;

internal sealed class StructureRegistryBulletin
{
    internal StructureRegistryBulletin(
        BuildingSpec[] structures,
        uint lbIdent,
        IReadOnlyDictionary<uint, FetchedChamber> chambersByChamberIdent)
    {
        Buildings = structures;
        LbIdent = lbIdent;
        ChambersByChamberIdent = chambersByChamberIdent;
        PrepSealed = structures.Length == 0;
    }

    internal BuildingSpec[] Buildings { get; }
    internal uint LbIdent { get; }
    internal IReadOnlyDictionary<uint, FetchedChamber> ChambersByChamberIdent { get; }
    internal StructureRegistry Registry { get; } = new();
    internal List<(FetchedChamber Cell, uint BuildingId)> ChamberStamps { get; } = [];
    internal int StructureCur { get; set; }
    internal uint UpcomingStructureIdent { get; set; } = 1;
    internal bool PrepSealed { get; set; }
    internal bool BulletinSealed { get; set; }
}

public static class BuildingFetcher
{
    public static StructureRegistry Build(
        TerrainTileExtras details,
        uint lbIdent,
        IReadOnlyDictionary<uint, FetchedChamber> chambersByChamberIdent)
    {
        var bulletin = ReadyBulletin(
            details,
            lbIdent,
            chambersByChamberIdent);
        while (!ProgressPrepOne(bulletin))
        {
        }
        SealBulletin(bulletin);
        return bulletin.Registry;
    }

    internal static StructureRegistryBulletin ReadyBulletin(
        TerrainTileExtras details,
        uint lbIdent,
        IReadOnlyDictionary<uint, FetchedChamber> chambersByChamberIdent)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(chambersByChamberIdent);

        int structureTally = details.Structures?.Count ?? 0;
        var structures = new BuildingSpec[structureTally];
        for (int ordinal = 0; ordinal < structureTally; ++ordinal)
            structures[ordinal] = details.Structures![ordinal];
        return new StructureRegistryBulletin(
            structures,
            lbIdent,
            chambersByChamberIdent);
    }

    internal static bool ProgressPrepOne(
        StructureRegistryBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        if (bulletin.PrepSealed)
            return true;
        if (bulletin.StructureCur >= bulletin.Buildings.Length)
        {
            bulletin.PrepSealed = true;
            return true;
        }

        AppendStructure(
            bulletin,
            bulletin.Buildings[bulletin.StructureCur]);
        bulletin.StructureCur++;
        if (bulletin.StructureCur >= bulletin.Buildings.Length)
            bulletin.PrepSealed = true;
        return bulletin.PrepSealed;
    }

    internal static void SealBulletin(
        StructureRegistryBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        if (!bulletin.PrepSealed)
        {
            throw new InvalidOperationException(
                "A building registry can't publish prior to preparation completes");
        }
        if (bulletin.BulletinSealed)
            return;

        foreach ((FetchedChamber chamber, uint structureIdent) in bulletin.ChamberStamps)
            chamber.StructureTag = structureIdent;
        bulletin.BulletinSealed = true;
    }

    private static void AppendStructure(
        StructureRegistryBulletin bulletin,
        BuildingSpec bDetails)
    {
        uint lbBitmask = bulletin.LbIdent & 0xFFFF0000u;
        HashSet<uint> environChamberIdents = new HashSet<uint>();
        List<Vector3[]> quitGatewayPolys = new List<Vector3[]>();

        if (bDetails.Doorways is not null)
        {
            foreach (BuildingDoorway gateway in bDetails.Doorways)
            {
                if (gateway.OtherCellId == 0xFFFF) continue;
                environChamberIdents.Add(lbBitmask | gateway.OtherCellId);
            }
        }

        // Step B: BFS through interior portals
        Queue<uint> fifo = new Queue<uint>(environChamberIdents);
        while (fifo.Count > 0)
        {
            uint latest = fifo.Dequeue();
            if (!bulletin.ChambersByChamberIdent.TryGetValue(latest, out var chamber))
                continue;
            foreach (var gateway in chamber.Portals)
            {
                if (gateway.OtherCellId == 0xFFFF) continue;
                uint neighbourIdent = lbBitmask | gateway.OtherCellId;
                if (environChamberIdents.Add(neighbourIdent))
                    fifo.Enqueue(neighbourIdent);
            }
        }

        foreach (uint chamberIdent in environChamberIdents)
        {
            if (!bulletin.ChambersByChamberIdent.TryGetValue(chamberIdent, out var chamber))
                continue;
            for (int gatewayOrdinal = 0; gatewayOrdinal < chamber.Portals.Count; ++gatewayOrdinal)
            {
                if (chamber.Portals[gatewayOrdinal].OtherCellId != 0xFFFF) continue;
                if (gatewayOrdinal >= chamber.PortalPolygons.Count) continue;
                Vector3[] ownPolyg = chamber.PortalPolygons[gatewayOrdinal];
                if (ownPolyg.Length < 3) continue;
                Vector3[] realmPolyg = new Vector3[ownPolyg.Length];
                for (int vertOrdinal = 0; vertOrdinal < ownPolyg.Length; ++vertOrdinal)
                {
                    realmPolyg[vertOrdinal] = Vector3.Transform(
                        ownPolyg[vertOrdinal],
                        chamber.WorldTransform);
                }
                quitGatewayPolys.Add(realmPolyg);
            }
        }

        bool hasGatewayLimits = false;
        var gatewayLower = new Vector3(float.MaxValue);
        var gatewayUpper = new Vector3(float.MinValue);
        foreach (Vector3[] polyg in quitGatewayPolys)
        {
            foreach (Vector3 vert in polyg)
            {
                hasGatewayLimits = true;
                gatewayLower = Vector3.Min(gatewayLower, vert);
                gatewayUpper = Vector3.Max(gatewayUpper, vert);
            }
        }

        if (environChamberIdents.Count is 0)
            return;

        AppendStructureRest(bulletin, environChamberIdents, quitGatewayPolys, hasGatewayLimits, gatewayLower, gatewayUpper);
    }

    private static void AppendStructureRest(StructureRegistryBulletin bulletin, HashSet<uint> environChamberIdents, List<Vector3[]> quitGatewayPolys, bool hasGatewayLimits, Vector3 gatewayLower, Vector3 gatewayUpper)
    {
        uint structureIdent = bulletin.UpcomingStructureIdent++;
        bulletin.Registry.Add(new Structure
        {
            StructureIdent = structureIdent,
            EnvironChamberIdents = environChamberIdents,
            QuitGatewayPolygs = quitGatewayPolys,
            HasGatewayLimits = hasGatewayLimits,
            GatewayLimits = hasGatewayLimits
                        ? new BatchBoundingBox(gatewayLower, gatewayUpper)
                        : default,
        });
        foreach (uint chamberIdent in environChamberIdents)
        {
            if (bulletin.ChambersByChamberIdent.TryGetValue(chamberIdent, out var chamber))
                bulletin.ChamberStamps.Add((chamber, structureIdent));
        }
    }
}
