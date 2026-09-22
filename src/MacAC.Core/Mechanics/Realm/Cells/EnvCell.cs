using System.Collections.Immutable;
using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Mechanics.Realm.Cells;

/// <summary>An indoor cell: a posed CellStruct with portals, visibility list and containment BSP.</summary>
public sealed class EnvCell : ObjRefChamber
{
    private const uint StemBitmask = 0xFFFF0000u;

    public EnvCell(
        uint ident,
        Matrix4x4 realmXform,
        Matrix4x4 invRealmXform,
        Vector3 ownLimitsLower,
        Vector3 ownLimitsUpper,
        IReadOnlyList<ChamberGateway> gateways,
        IReadOnlyList<uint> stabRoster,
        bool observedBeyond,
        CellBspTree? containmentBsp,
        PackedCellContainmentBsp? planarContainmentBsp = null)
        : base(ident, realmXform, invRealmXform, ownLimitsLower, ownLimitsUpper, gateways, stabRoster, observedBeyond)
    {
        ContainmentBsp = containmentBsp;
        FlatContainmentBsp = planarContainmentBsp;
    }

    public CellBspTree? ContainmentBsp { get; }

    public PackedCellContainmentBsp? FlatContainmentBsp { get; }

    public override bool PtInCell(Vector3 realmPt)
    {
        if (Portals.Count is 0)
            return false;
        Vector3 own = Vector3.Transform(realmPt, InverseWorldTransform);
        if (FlatContainmentBsp is { TrunkIdx: >= 0 })
            return PackedBspQuery.PtInsideCellBsp(FlatContainmentBsp, own);
        if (ContainmentBsp?.Root is { } trunk)
            return CellBspProbe.PtInsideChamberBsp(trunk, own);
        return false;
    }

    public static EnvCell FromDat(
        uint ident,
        RoomCell datChamber,
        ShellCell chamberStruct,
        Matrix4x4 realmXform,
        PackedCellContainmentBsp? planarContainmentBsp = null)
    {
        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);

        Bounds bbox = new Bounds();
        foreach (MeshVertex vert in chamberStruct.Vertices.ByIndex.Values)
            bbox.Add(new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z));

        List<ChamberGateway> gateways = new List<ChamberGateway>(datChamber.Doorways.Count);
        foreach (CellDoorway portal in datChamber.Doorways)
        {
            gateways.Add(new ChamberGateway(
                anotherChamberIdent: portal.OtherCellId,
                anotherGatewayIdent: portal.OtherPortalId,
                polygIdent: portal.PolygonId,
                flagSet: (ushort)portal.Bits,
                polygOwn: GatewayPolyg(chamberStruct, portal.PolygonId)));
        }

        uint stem = ident & StemBitmask;
        List<uint> stabs = new List<uint>();
        if (datChamber.VisibleCells is { } shown)
        {
            foreach (ushort lo in shown)
                stabs.Add(stem | lo);
        }

        return new EnvCell(
            ident, realmXform, inv, bbox.Min, bbox.Max, gateways, stabs,
            datChamber.Bits.HasFlag(RoomCellBits.SeenOutside), chamberStruct.CellTree, planarContainmentBsp);
    }

    public static EnvCell FromReadied(uint ident, Matrix4x4 realmXform, PackedCellStructContactAsset structure, PackedEnvCellTopology wiring)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(wiring);
        Matrix4x4.Invert(realmXform, out Matrix4x4 inv);

        Bounds bbox = new Bounds();
        bbox.AppendAll(structure.PhysicsBsp.PolygChart.Vertices);
        bbox.AppendAll(structure.PortalPolygons.Vertices);

        List<ChamberGateway> gateways = new List<ChamberGateway>(wiring.Portals.Length);
        foreach (PackedEnvCellPortal gateway in wiring.Portals)
        {
            FromReadiedLoop(structure, gateway, gateways);
        }

        return new EnvCell(
            ident, realmXform, inv, bbox.Min, bbox.Max, gateways,
            wiring.VisibleCellIds, wiring.SeenOutside, containmentBsp: null, structure.ContainmentBsp);
    }

    private static void FromReadiedLoop(PackedCellStructContactAsset structure, PackedEnvCellPortal gateway, List<ChamberGateway> gateways)
    {
        var polyg = structure.PortalPolygons.Polygons[gateway.PolygonIndex];
        Vector3[] corners = new Vector3[polyg.VertexRange.Count];
        structure.PortalPolygons.Vertices.AsSpan(polyg.VertexRange.Start, polyg.VertexRange.Count).CopyTo(corners);
        gateways.Add(new ChamberGateway(gateway.OtherCellId, anotherGatewayIdent: 0, gateway.PolygonId, gateway.Flags, corners));
    }

    // A portal polygon's corners in cell space; empty if any vertex is missing
    private static IReadOnlyList<Vector3> GatewayPolyg(ShellCell chamberStruct, ushort polygIdent)
    {
        if (!chamberStruct.Facets.TryGetValue(polygIdent, out Facet? poly) || poly.VertexIds.Count < 3)
            return [];
        Vector3[] corners = new Vector3[poly.VertexIds.Count];
        for (int idx = 0; idx < corners.Length; ++idx)
        {
            if (!chamberStruct.Vertices.ByIndex.TryGetValue((ushort)poly.VertexIds[idx], out MeshVertex? vertex))
                return [];
            corners[idx] = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
        }
        return corners;
    }

    // An axis-aligned box that collapses to the origin when nothing was added
    private struct Bounds
    {
        private Vector3 _lower = new(float.MaxValue);
        private Vector3 _upper = new(float.MinValue);

        public Bounds()
        {
        }

        public readonly Vector3 Min => _lower.X == float.MaxValue ? Vector3.Zero : _lower;

        public readonly Vector3 Max => _lower.X == float.MaxValue ? Vector3.Zero : _upper;

        public void Add(Vector3 p)
        {
            _lower = Vector3.Min(_lower, p);
            _upper = Vector3.Max(_upper, p);
        }

        public void AppendAll(ImmutableArray<Vector3> pts)
        {
            foreach (Vector3 p in pts)
                Add(p);
        }
    }
}
