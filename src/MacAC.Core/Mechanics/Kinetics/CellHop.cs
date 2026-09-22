using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

public static partial class CellHop
{
    private const float EPSILON = 0.02f;

    private const float FEpsilon = 0.000199999995f;

    private const uint BeyondGateway = 0xFFFF;
    private const uint LoBitmask = 0xFFFFu;
    private const uint LeadInsideLo = 0x0100u;

    public static uint FindVisibleChildCell(
        KineticAssetCache stash,
        uint beginChamberIdent,
        Vector3 realmPt,
        bool useStabRoster,
        ICollection<uint>? probedChambers = null)
    {
        probedChambers?.Add(beginChamberIdent);
        var begin = stash.FetchChamberStruct(beginChamberIdent);
        if (begin is null)
            return 0u;

        if (PtInChamber(stash, begin, realmPt))
            return beginChamberIdent;

        if (useStabRoster)
        {
            foreach (uint ident in begin.VisibleCellIds)
            {
                probedChambers?.Add(ident);
                if (PtInChamber(stash, stash.FetchChamberStruct(ident), realmPt))
                    return ident;
            }
        }
        else
        {
            foreach (PortalFacts gateway in begin.Portals)
            {
                probedChambers?.Add(gateway.OtherCellId);
                if (PtInChamber(stash, stash.FetchChamberStruct(gateway.OtherCellId), realmPt))
                    return gateway.OtherCellId;
            }
        }

        return 0u;
    }

    private static bool IsInside(uint chamberIdent) => (chamberIdent & LoBitmask) >= LeadInsideLo;

    private static Orb[] One(Vector3 origin, float radius) => [new Orb { Center = origin, Radius = radius }];

    private static int NetOrbTally(IReadOnlyList<Orb> realmOrbs, int countOrbs)
    {
        if (countOrbs <= 0 || realmOrbs.Count is 0)
            return 0;
        return countOrbs < realmOrbs.Count ? countOrbs : realmOrbs.Count;
    }

    // Signed distance of a world point from a plane expressed in the cell's own frame
    private static float OwnHeight(CellKinetics chamber, in Plane plane, Vector3 realmPt)
    {
        Vector3 own = Vector3.Transform(realmPt, chamber.InverseWorldTransform);
        return Vector3.Dot(own, plane.Normal) + plane.D;
    }

    // Cells that were merely probed still count toward the query footprint a caller may be collecting
    private static void CaptureUnionSoleSensor(ICollection<uint> contenders, uint chamberIdent)
    {
        if (contenders is ChamberArray { UnionMark: { } footprint })
            footprint.Add(chamberIdent);
    }

    private static bool PtInChamber(KineticAssetCache stash, CellKinetics? chamber, Vector3 realmPt)
    {
        if (chamber is null || chamber.Portals.Count is 0 || !ContactSweep.HasCellContainment(stash, chamber))
            return false;
        return ContactSweep.PointInsideCell(stash, chamber, Vector3.Transform(realmPt, chamber.InverseWorldTransform));
    }

    // The portal's plane from the graph polygons, else from the prepared topology (which must agree
    // with the graph portal)
    private static bool TryGetPortalPlane(CellKinetics chamber, int gatewayOrdinal, PortalFacts gateway, out Plane plane)
    {
        if (chamber.PortalPolygons is not null && chamber.PortalPolygons.TryGetValue(gateway.PolygonId, out SettledPolygon? polyg))
        {
            plane = polyg.Plane;
            return true;
        }

        var wiring = chamber.PlanarWiring;
        var chart = chamber.PlanarGatewayPolygs;
        if (wiring is null || chart is null)
        {
            plane = default;
            return false;
        }

        if ((uint)gatewayOrdinal >= (uint)wiring.Portals.Length)
            throw new InvalidDataException($"Cell 0x{chamber.SourceId:X8} portal {gatewayOrdinal} is absent from its prepared topology");

        var dense = wiring.Portals[gatewayOrdinal];
        if (dense.OtherCellId != gateway.OtherCellId || dense.PolygonId != gateway.PolygonId || dense.Flags != gateway.Flags)
            throw new InvalidDataException($"Cell 0x{chamber.SourceId:X8} portal {gatewayOrdinal} doesn't match its prepared topology");

        int polygOrdinal = dense.PolygonIndex;
        if ((uint)polygOrdinal >= (uint)chart.Polygons.Length)
            throw new InvalidDataException($"Cell 0x{chamber.SourceId:X8} portal {gatewayOrdinal} references not valid prepared polygon index {polygOrdinal}.");

        plane = chart.Polygons[polygOrdinal].Plane;
        return true;
    }
}
