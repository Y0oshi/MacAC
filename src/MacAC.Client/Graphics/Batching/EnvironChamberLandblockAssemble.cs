using System.Collections.Immutable;
using System.Numerics;
using MacAC.Dat;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Drawing.Batches;

namespace MacAC.Client.Graphics.Batching;

public sealed record EnvironChamberShellStance(
    uint CellId,
    ulong GeometryId,
    uint EnvironmentId,
    ushort CellStructure,
    ImmutableArray<ushort> Surfaces,
    Vector3 WorldPosition,
    Quaternion Rotation,
    Matrix4x4 Transform,
    BatchBoundingBox LocalBounds,
    BatchBoundingBox WorldBounds);

public sealed class EnvironChamberLandblockAssemble
{
    public EnvironChamberLandblockAssemble(
        uint lbIdent,
        IEnumerable<FetchedChamber> visibilityCells,
        IEnumerable<EnvironChamberShellStance> shells,
        IEnumerable<StrideBuildingMint.Entry>? walkBuildings = null,
        float strollUpperZ = 0f,
        float strollLowerZ = 0f)
    {
        LbIdent = lbIdent;
        VisChambers = [.. visibilityCells];
        Shells = [.. shells];
        StrollStructures = [.. (walkBuildings ?? [])];
        StrollStructureTriMeshDeps = GatherStrollStructureTriMeshDeps(StrollStructures);
        StrollUpperZ = strollUpperZ;
        StrollLowerZ = strollLowerZ;

        uint anticipatedStem = lbIdent & 0xFFFF0000u;
        if (VisChambers.Any(chamber => (chamber.CellId & 0xFFFF0000u) != anticipatedStem))
            throw new ArgumentException("A visibility cell belongs to a different landblock", nameof(visibilityCells));
        if (Shells.Any(shell => (shell.CellId & 0xFFFF0000u) != anticipatedStem))
            throw new ArgumentException("A render shell belongs to a different landblock", nameof(shells));
        if (StrollStructures.Any(listing => (listing.Building.LocusChamberIdent & 0xFFFF0000u) != anticipatedStem))
            throw new ArgumentException("A walk building belongs to a different landblock", nameof(walkBuildings));
    }

    public uint LbIdent { get; }
    public ImmutableArray<FetchedChamber> VisChambers { get; }
    public ImmutableArray<EnvironChamberShellStance> Shells { get; }

    public ImmutableArray<StrideBuildingMint.Entry> StrollStructures { get; }

    public ImmutableArray<ulong> StrollStructureTriMeshDeps { get; }

    public float StrollUpperZ { get; }

    public float StrollLowerZ { get; }

    private static ImmutableArray<ulong> GatherStrollStructureTriMeshDeps(
        ImmutableArray<StrideBuildingMint.Entry> structures)
    {
        HashSet<uint> observed = new HashSet<uint>();
        var deps = ImmutableArray.CreateBuilder<ulong>();
        for (int structureOrdinal = 0; structureOrdinal < structures.Length; ++structureOrdinal)
        {
            var tiers = structures[structureOrdinal].Building.DowngradeTiers;
            for (int tierOrdinal = 0; tierOrdinal < tiers.Length; ++tierOrdinal)
            {
                uint ident = tiers[tierOrdinal].GfxObjId;
                if (ident is not 0 && observed.Add(ident))
                    deps.Add(ident);
            }
        }
        return deps.ToImmutable();
    }
}

public sealed class EnvCellLandblockBuildAssembler(uint lbIdent)
{
    private readonly uint _lbIdent = lbIdent;
    private readonly List<FetchedChamber> _visChambers = [];
    private readonly List<EnvironChamberShellStance> _shells = [];
    private readonly List<StrideBuildingMint.Entry> _strollStructures = [];
    private float _strollUpperZ;
    private float _strollLowerZ;
    private bool _built;

    public void AppendStrollStructures(IEnumerable<StrideBuildingMint.Entry> listings)
    {
        if (_built)
            throw new InvalidOperationException("This landblock cell build is by now complete");
        _strollStructures.AddRange(listings);
    }

    public void AssignStrollZSlab(float upperZ, float lowerZ)
    {
        if (_built)
            throw new InvalidOperationException("This landblock cell build is by now complete");
        _strollUpperZ = upperZ;
        _strollLowerZ = lowerZ;
    }

    public void AppendChamber(
        uint environChamberIdent,
        RoomCell environChamber,
        ShellCell chamberStruct,
        Vector3 kineticsChamberOrigin,
        Matrix4x4 kineticsChamberXform,
        Vector3 shellRealmLocus,
        Matrix4x4 shellXform,
        bool hasDrawableGeo)
    {
        if (_built)
            throw new InvalidOperationException("This landblock cell build is by now complete");

        BatchBoundingBox ownLimits = CalculateOwnLimits(chamberStruct);
        _visChambers.Add(AssembleVisChamber(
            environChamberIdent,
            environChamber,
            chamberStruct,
            kineticsChamberOrigin,
            kineticsChamberXform,
            ownLimits));

        if (!hasDrawableGeo)
            return;

        ulong geoIdent = CalculateGeoIdent(
            environChamber.ShellId,
            environChamber.ShellCellIndex,
            environChamber.SkinIds);
        _shells.Add(new EnvironChamberShellStance(
            environChamberIdent,
            geoIdent,
            environChamber.ShellId,
            environChamber.ShellCellIndex,
            [.. environChamber.SkinIds],
            shellRealmLocus,
            environChamber.Position.Orientation,
            shellXform,
            ownLimits,
            ConvertBoundingBbox(ownLimits, shellXform)));
    }

    public EnvironChamberLandblockAssemble Build()
    {
        if (_built)
            throw new InvalidOperationException("This landblock cell build is by now complete");
        _built = true;
        return new EnvironChamberLandblockAssemble(
            _lbIdent, _visChambers, _shells, _strollStructures, _strollUpperZ, _strollLowerZ);
    }

    public static ulong CalculateGeoIdent(
        uint surroundingsIdent,
        ushort chamberStructure,
        IReadOnlyList<ushort> canvases)
    {
        return EnvCellGeometryKey.Compute(surroundingsIdent, chamberStructure, canvases);
    }

    private static BatchBoundingBox CalculateOwnLimits(ShellCell chamberStruct)
    {
        var lower = new Vector3(float.MaxValue);
        var upper = new Vector3(float.MinValue);
        foreach (MeshVertex vert in chamberStruct.Vertices.ByIndex.Values)
        {
            Vector3 locus = new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z);
            lower = Vector3.Min(lower, locus);
            upper = Vector3.Max(upper, locus);
        }

        return lower.X == float.MaxValue
            ? new BatchBoundingBox(Vector3.Zero, Vector3.Zero)
            : new BatchBoundingBox(lower, upper);
    }

    private static BatchBoundingBox ConvertBoundingBbox(BatchBoundingBox bbox, Matrix4x4 xform)
    {
        Span<Vector3> corners =
        [
            new(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
            new(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
            new(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
            new(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
            new(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
            new(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
            new(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
            new(bbox.Max.X, bbox.Max.Y, bbox.Max.Z),
        ];

        var lower = new Vector3(float.MaxValue);
        var upper = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            Vector3 transformed = Vector3.Transform(corner, xform);
            lower = Vector3.Min(lower, transformed);
            upper = Vector3.Max(upper, transformed);
        }

        return new BatchBoundingBox(lower, upper);
    }

    private static FetchedChamber AssembleVisChamber(
        uint environChamberIdent,
        RoomCell environChamber,
        ShellCell chamberStruct,
        Vector3 chamberOrigin,
        Matrix4x4 chamberXform,
        BatchBoundingBox ownLimits)
    {
        Matrix4x4.Invert(chamberXform, out var inv);

        List<ChamberGatewayDetails> gateways = new List<ChamberGatewayDetails>();
        var clipPlanes = new List<GatewayClipFacet>();
        List<Vector3[]> gatewayPolygs = new List<Vector3[]>();

        foreach (CellDoorway gateway in environChamber.Doorways)
        {
            AssembleVisChamberLoop(gateways, gateway, chamberStruct, clipPlanes, gatewayPolygs);
        }

        uint lbStem = environChamberIdent & 0xFFFF0000u;
        List<uint> shownChambers = new List<uint>();
        if (environChamber.VisibleCells is not null)
        {
            foreach (var loIdent in environChamber.VisibleCells)
                shownChambers.Add(lbStem | loIdent);
        }

        return new FetchedChamber
        {
            CellId = environChamberIdent,
            RealmPlace = chamberOrigin,
            WorldTransform = chamberXform,
            InverseWorldTransform = inv,
            OwnLimitsLower = ownLimits.Min,
            OwnLimitsUpper = ownLimits.Max,
            Portals = gateways,
            ClipPlanes = clipPlanes,
            PortalPolygons = gatewayPolygs,
            VisibleCells = shownChambers,
            SeenOutside = environChamber.Bits.HasFlag(RoomCellBits.SeenOutside),
            Stroll = StrideCellMint.FromDecoded(environChamberIdent, environChamber, chamberStruct, chamberXform, inv),
        };
    }

    private static void AssembleVisChamberLoop(List<ChamberGatewayDetails> gateways, CellDoorway gateway, ShellCell chamberStruct, List<GatewayClipFacet> clipPlanes, List<Vector3[]> gatewayPolygs)
    {
        gateways.Add(new ChamberGatewayDetails(
                    gateway.OtherCellId,
                    gateway.PolygonId,
                    (ushort)gateway.Bits,
                    gateway.OtherPortalId));
        if (chamberStruct.Facets.TryGetValue(gateway.PolygonId, out var poly)
                        && poly.VertexIds.Count >= 3)
        {
            AssembleVisChamberBranch(chamberStruct, poly, gateway, clipPlanes);
        }
        else
        {
            clipPlanes.Add(default);
        }
        Vector3[] polygVerts = [];
        if (chamberStruct.Facets.TryGetValue(gateway.PolygonId, out var gatewayPolyg)
                        && gatewayPolyg.VertexIds.Count >= 3)
        {
            polygVerts = new Vector3[gatewayPolyg.VertexIds.Count];
            bool allSettled = true;
            for (int vertOrdinal = 0; vertOrdinal < gatewayPolyg.VertexIds.Count; ++vertOrdinal)
            {
                if (chamberStruct.Vertices.ByIndex.TryGetValue(
                    (ushort)gatewayPolyg.VertexIds[vertOrdinal], out var vert))
                {
                    polygVerts[vertOrdinal] = new Vector3(
                        vert.Position.X,
                        vert.Position.Y,
                        vert.Position.Z);
                }
                else
                {
                    allSettled = false;
                    break;
                }
            }

            if (!allSettled)
                polygVerts = [];
        }
        gatewayPolygs.Add(polygVerts);
    }

    private static void AssembleVisChamberBranch(ShellCell chamberStruct, Facet poly, CellDoorway gateway, List<GatewayClipFacet> clipPlanes)
    {
        Vector3 p0 = Vector3.Zero, p1 = Vector3.Zero, p2 = Vector3.Zero;
        bool located = true;
        if (chamberStruct.Vertices.ByIndex.TryGetValue((ushort)poly.VertexIds[0], out var v0))
            p0 = new Vector3(v0.Position.X, v0.Position.Y, v0.Position.Z);
        else
            located = false;
        if (located && chamberStruct.Vertices.ByIndex.TryGetValue((ushort)poly.VertexIds[1], out var v1))
            p1 = new Vector3(v1.Position.X, v1.Position.Y, v1.Position.Z);
        else
            located = false;
        if (located && chamberStruct.Vertices.ByIndex.TryGetValue((ushort)poly.VertexIds[2], out var v2))
            p2 = new Vector3(v2.Position.X, v2.Position.Y, v2.Position.Z);
        else
            located = false;
        if (located)
        {
            AssembleVisChamberBranch2(p1, p0, p2, gateway, clipPlanes);
        }
        else
        {
            clipPlanes.Add(default);
        }
    }

    private static void AssembleVisChamberBranch2(Vector3 p1, Vector3 p0, Vector3 p2, CellDoorway gateway, List<GatewayClipFacet> clipPlanes)
    {
        Vector3 norm = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
        float d = -Vector3.Dot(norm, p0);
        int insideFlank = ((ushort)gateway.Bits & 0x2) is 0 ? 1 : 0;
        clipPlanes.Add(new GatewayClipFacet
        {
            Normal = norm,
            D = d,
            InsideFlank = insideFlank,
        });
    }
}
