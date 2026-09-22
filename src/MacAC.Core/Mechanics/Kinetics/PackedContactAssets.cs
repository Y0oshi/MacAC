using System.Collections.Immutable;
using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A window into a flat array: rows [Start, Start+Count).</summary>
public readonly record struct PackedIndexRange(int Start, int Count)
{
    public int FinishExclusive => checked(Start + Count);

    internal void Validate(int len, string label)
    {
        if (Start < 0 || Count < 0 || Start > len - Count)
            throw new InvalidDataException($"{label} range [{Start}, {Start}+{Count}) exceeds array length {len}.");
    }
}

public readonly record struct PackedContactSphere(Vector3 Origin, float Radius);

public readonly record struct PackedContactCylinder(Vector3 Origin, float Radius, float Height);

public readonly record struct PackedContactPolygon(
    ushort Id,
    Plane Plane,
    FaceCulling SidesType,
    int NumPoints,
    PackedIndexRange VertexRange);

public sealed class PackedPolygonTable
{
    public static PackedPolygonTable Empty { get; } = new([], []);

    public PackedPolygonTable(ImmutableArray<PackedContactPolygon> polygons, ImmutableArray<Vector3> vertices)
    {
        PackedRows.DemandPresent(polygons, "Polygon rows", nameof(polygons));
        PackedRows.DemandPresent(vertices, "Vertex rows", nameof(vertices));

        ushort previousIdent = 0;
        for (int idx = 0; idx < polygons.Length; ++idx)
        {
            var polyg = polygons[idx];
            polyg.VertexRange.Validate(vertices.Length, $"polygon[{idx}].vertices");

            if (polyg.NumPoints != polyg.VertexRange.Count)
            {
                throw new InvalidDataException(
                    $"polygon[{idx}] NumPoints {polyg.NumPoints} doesn't match its vertex count {polyg.VertexRange.Count}.");
            }
            if (!Enum.IsDefined(polyg.SidesType))
                throw new InvalidDataException($"polygon[{idx}] has unrecognized cull mode {(int)polyg.SidesType}.");
            if (idx is not 0 && polyg.Id <= previousIdent)
                throw new InvalidDataException("Flat polygon rows has to be strictly ordered by unique ID");

            previousIdent = polyg.Id;
        }

        Polygons = polygons;
        Vertices = vertices;
    }

    public ImmutableArray<PackedContactPolygon> Polygons { get; }

    public ImmutableArray<Vector3> Vertices { get; }

    public bool TrySeekPolygOrdinal(ushort ident, out int ordinal)
    {
        int lo = 0;
        int hi = Polygons.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            ushort sensor = Polygons[mid].Id;
            if (sensor == ident)
            {
                ordinal = mid;
                return true;
            }
            if (sensor < ident)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        ordinal = -1;
        return false;
    }
}

// What every flattened BSP node row exposes for shape validation
internal interface IPackedBspRow
{
    BspTag Type { get; }
    int PositiveDescendantOrdinal { get; }
    int NegativeDescendantOrdinal { get; }
}

public readonly record struct PackedKineticBspNode(
    BspTag Type,
    Plane SplittingPlane,
    int PositiveDescendantOrdinal,
    int NegativeDescendantOrdinal,
    int LeafIndex,
    int Solid,
    PackedContactSphere BoundingSphere,
    PackedIndexRange PolygonIndexRange) : IPackedBspRow;

public sealed class PackedKineticBsp
{
    public PackedKineticBsp(
        int trunkOrdinal,
        ImmutableArray<PackedKineticBspNode> nodes,
        ImmutableArray<int> polygonIndexStream,
        PackedPolygonTable polygChart)
    {
        PackedRows.DemandPresent(nodes, "Node rows", nameof(nodes));
        PackedRows.DemandPresent(polygonIndexStream, "Polygon-index rows", nameof(polygonIndexStream));
        ArgumentNullException.ThrowIfNull(polygChart);

        if (nodes.Length is 0)
        {
            if (trunkOrdinal != -1)
                throw new InvalidDataException("An empty physics BSP must use root index -1");
            if (polygonIndexStream.Length is not 0)
                throw new InvalidDataException("An empty physics BSP can't contain leaf polygon indices");
        }
        else
        {
            PackedRows.DemandTrunkAtZero(trunkOrdinal, "physics BSP");
            for (int idx = 0; idx < nodes.Length; ++idx)
            {
                PackedRows.VetJoint(nodes[idx], idx, nodes.Length, "physics node");
                nodes[idx].PolygonIndexRange.Validate(polygonIndexStream.Length, $"physics node[{idx}].polygonIndices");
            }

            int polygTally = polygChart.Polygons.Length;
            for (int idx = 0; idx < polygonIndexStream.Length; ++idx)
            {
                int polygOrdinal = polygonIndexStream[idx];
                if ((uint)polygOrdinal >= (uint)polygTally)
                    throw new InvalidDataException($"leaf polygon stream[{idx}] index {polygOrdinal} exceeds polygon count {polygTally}.");
            }

            PackedRows.VetPreOrderingForm(trunkOrdinal, nodes, "physics BSP");
        }

        TrunkOrdinal = trunkOrdinal;
        Joints = nodes;
        PolygOrdinalFlow = polygonIndexStream;
        PolygChart = polygChart;
    }

    public int TrunkOrdinal { get; }

    public ImmutableArray<PackedKineticBspNode> Joints { get; }

    public ImmutableArray<int> PolygOrdinalFlow { get; }

    public PackedPolygonTable PolygChart { get; }
}

public readonly record struct PackedCellBspNode(
    BspTag Type,
    Plane SplittingPlane,
    int PositiveDescendantOrdinal,
    int NegativeDescendantOrdinal,
    int LeafIndex) : IPackedBspRow;

/// <summary>A cell's containment BSP (point-in-cell tests) flattened to pre-order rows.</summary>
public sealed class PackedCellContainmentBsp
{
    public PackedCellContainmentBsp(int trunkOrdinal, ImmutableArray<PackedCellBspNode> nodes)
    {
        PackedRows.DemandPresent(nodes, "Node rows", nameof(nodes));

        if (nodes.Length is 0)
        {
            if (trunkOrdinal != -1)
                throw new InvalidDataException("An empty cell BSP must use root index -1");
        }
        else
        {
            PackedRows.DemandTrunkAtZero(trunkOrdinal, "cell BSP");
            for (int idx = 0; idx < nodes.Length; ++idx)
                PackedRows.VetJoint(nodes[idx], idx, nodes.Length, "cell node");
            PackedRows.VetPreOrderingForm(trunkOrdinal, nodes, "cell-containment BSP");
        }

        TrunkIdx = trunkOrdinal;
        Nodes = nodes;
    }

    public int TrunkIdx { get; }

    public ImmutableArray<PackedCellBspNode> Nodes { get; }
}

/// <summary>A setup's simple collision volumes and the step heights that go with them.</summary>
public sealed class PackedSetupContact
{
    public PackedSetupContact(
        ImmutableArray<PackedContactCylinder> cylinders,
        ImmutableArray<PackedContactSphere> spheres,
        float height,
        float radius,
        float hopUpHeight,
        float hopDownHeight)
    {
        PackedRows.DemandPresent(cylinders, "Cylinder rows", nameof(cylinders));
        PackedRows.DemandPresent(spheres, "Sphere rows", nameof(spheres));

        Cylinders = cylinders;
        Spheres = spheres;
        Height = height;
        Radius = radius;
        StepUpHeight = hopUpHeight;
        StepDownHeight = hopDownHeight;
    }

    public ImmutableArray<PackedContactCylinder> Cylinders { get; }

    public ImmutableArray<PackedContactSphere> Spheres { get; }

    public float Height { get; }

    public float Radius { get; }

    public float StepUpHeight { get; }

    public float StepDownHeight { get; }
}

public readonly record struct PackedGfxObjVisualExtent(
    Vector3 Min,
    Vector3 Max,
    Vector3 Center,
    float Radius,
    Vector3 HalfExtents);

public sealed record PackedGfxObjContactAsset(
    PackedKineticBsp PhysicsBsp,
    PackedContactSphere? BoundingSphere,
    PackedGfxObjVisualExtent? VisualBounds);

public sealed record PackedCellStructContactAsset(
    PackedKineticBsp PhysicsBsp,
    PackedCellContainmentBsp ContainmentBsp,
    PackedPolygonTable PortalPolygons);

public readonly record struct PackedEnvCellPortal(
    ushort OtherCellId,
    ushort PolygonId,
    ushort Flags,
    int PolygonIndex);

/// <summary>An EnvCell's portals and visibility list, independent of its geometry.</summary>
public sealed class PackedEnvCellTopology
{
    public PackedEnvCellTopology(ImmutableArray<PackedEnvCellPortal> portals, ImmutableArray<uint> visibleCellIds, bool observedBeyond)
    {
        PackedRows.DemandPresent(portals, "Portal rows", nameof(portals));
        PackedRows.DemandPresent(visibleCellIds, "Visible-cell rows", nameof(visibleCellIds));

        for (int idx = 1; idx < visibleCellIds.Length; ++idx)
        {
            if (visibleCellIds[idx] <= visibleCellIds[idx - 1])
                throw new InvalidDataException("Visible cell IDs has to be strictly ordered and unique");
        }

        for (int idx = 0; idx < portals.Length; ++idx)
        {
            if (portals[idx].PolygonIndex < 0)
                throw new InvalidDataException($"portal[{idx}] has not valid polygon index {portals[idx].PolygonIndex}.");
        }

        Portals = portals;
        VisibleCellIds = visibleCellIds;
        SeenOutside = observedBeyond;
    }

    public ImmutableArray<PackedEnvCellPortal> Portals { get; }

    public ImmutableArray<uint> VisibleCellIds { get; }

    public bool SeenOutside { get; }
}

/// <summary>An EnvCell's structure and topology, checked against each other.</summary>
public sealed class PackedCellContactAsset
{
    public PackedCellContactAsset(PackedCellStructContactAsset structure, PackedEnvCellTopology wiring)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(wiring);

        int polygTally = structure.PortalPolygons.Polygons.Length;
        for (int idx = 0; idx < wiring.Portals.Length; ++idx)
        {
            int polygOrdinal = wiring.Portals[idx].PolygonIndex;
            if ((uint)polygOrdinal >= (uint)polygTally)
                throw new InvalidDataException($"portal[{idx}] polygon index {polygOrdinal} exceeds portal polygon count {polygTally}.");
        }

        BuildingSpec = structure;
        Topology = wiring;
    }

    public PackedCellStructContactAsset BuildingSpec { get; }

    public PackedEnvCellTopology Topology { get; }
}

// Validation shared by every packed row set
internal static class PackedRows
{
    public static void DemandPresent<T>(ImmutableArray<T> ranks, string what, string parameterLabel)
    {
        if (ranks.IsDefault)
            throw new ArgumentException($"{what} must not be default", parameterLabel);
    }

    public static void DemandTrunkAtZero(int trunkOrdinal, string label)
    {
        if (trunkOrdinal is not 0)
            throw new InvalidDataException($"A deterministic pre-order {label} must start at index 0, not {trunkOrdinal}.");
    }

    public static void VetJoint<TRow>(in TRow joint, int ordinal, int jointTally, string rankLabel)
        where TRow : struct, IPackedBspRow
    {
        PackedBspNodeContract.Validate(joint.Type, joint.PositiveDescendantOrdinal >= 0, joint.NegativeDescendantOrdinal >= 0, $"{rankLabel}[{ordinal}]");
        DemandDescendantInSpan(joint.PositiveDescendantOrdinal, jointTally, ordinal, rankLabel, "positive");
        DemandDescendantInSpan(joint.NegativeDescendantOrdinal, jointTally, ordinal, rankLabel, "negative");
    }

    // Walks the tree positive-child-first and demands the rows be laid out in exactly that order
    public static void VetPreOrderingForm<TRow>(int trunkOrdinal, ImmutableArray<TRow> joints, string label)
        where TRow : struct, IPackedBspRow
    {
        bool[] observed = new bool[joints.Length];
        Stack<int> queued = new Stack<int>();
        queued.Push(trunkOrdinal);
        int anticipated = 0;

        while (queued.TryPop(out int ordinal))
        {
            if (observed[ordinal])
                throw new InvalidDataException($"{label} contains a cycle or shared child");
            if (ordinal != anticipated)
            {
                throw new InvalidDataException(
                    $"{label} isn't in deterministic positive-prior to-negative pre-order: wanted node {anticipated}, encountered {ordinal}.");
            }

            observed[ordinal] = true;
            ++anticipated;

            TRow rank = joints[ordinal];
            if (rank.NegativeDescendantOrdinal >= 0)
                queued.Push(rank.NegativeDescendantOrdinal);
            if (rank.PositiveDescendantOrdinal >= 0)
                queued.Push(rank.PositiveDescendantOrdinal);
        }

        if (anticipated != joints.Length)
            throw new InvalidDataException($"{label} contains {joints.Length - anticipated} unreachable node(s)");
    }

    private static void DemandDescendantInSpan(int descendantOrdinal, int jointTally, int holderOrdinal, string rankLabel, string rim)
    {
        if (descendantOrdinal < -1 || descendantOrdinal >= jointTally)
            throw new InvalidDataException($"{rankLabel}[{holderOrdinal}] {rim} child {descendantOrdinal} is out of range");
    }
}

internal static class PackedBspNodeContract
{
    public static void Validate(BspTag kind, bool hasPositive, bool hasNegative, string label)
    {
        if (!Enum.IsDefined(kind) || kind == BspTag.Portal)
            throw new InvalidDataException($"{label} has not supported type 0x{(uint)kind:X8}.");

        (bool wantPositive, bool wantNegative) = kind switch
        {
            BspTag.Leaf => (false, false),
            BspTag.BPIn or BspTag.BPnn => (true, false),
            BspTag.BpIN or BspTag.BpnN => (false, true),
            BspTag.BPIN or BspTag.BPnN => (true, true),
            BspTag.BPOL or BspTag.BPFL => (false, false),
            _ => throw new InvalidDataException($"{label} has not supported type 0x{(uint)kind:X8}."),
        };

        if (hasPositive != wantPositive || hasNegative != wantNegative)
        {
            throw new InvalidDataException(
                $"{label} type {kind} needs children (+={wantPositive}, -={wantNegative}) but contains (+={hasPositive}, -={hasNegative}).");
        }
    }
}
