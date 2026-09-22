using System.Numerics;
using MacAC.Dat;
using Plane = System.Numerics.Plane;
using ReadiedChamberGraphLandblock = MacAC.Mechanics.Realm.Cells.ReadiedChamberGraphLandblock;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Axis-aligned extent of a GfxObj's vertices in its own frame.</summary>
public sealed class GfxObjVisualExtent
{
    public required Vector3 Min { get; init; }
    public required Vector3 Max { get; init; }
    public required Vector3 Center { get; init; }
    /// <summary>Local-space radius (diagonal half-length) - loose bound.</summary>
    public required float Radius { get; init; }
    public required Vector3 HalfExtents { get; init; }

    internal PackedGfxObjVisualExtent Pack() => new(Min, Max, Center, Radius, HalfExtents);

    internal static GfxObjVisualExtent Unpack(in PackedGfxObjVisualExtent dense)
    {
        return new()
        {
            Min = dense.Min,
            Max = dense.Max,
            Center = dense.Center,
            Radius = dense.Radius,
            HalfExtents = dense.HalfExtents,
        };
    }
}

/// <summary>A dat polygon with its vertices looked up and its plane computed.</summary>
public sealed class SettledPolygon
{
    public required Vector3[] Vertices { get; init; }
    public required Plane Plane { get; init; }
    public required int NumPoints { get; init; }
    public required FaceCulling SidesType { get; init; }
    public ushort Id { get; init; }
}

/// <summary>A GfxObj's collision data in whichever forms the cache holds: graph, packed, or both.</summary>
public sealed class GfxObjKinetics
{
    public uint SourceId { get; init; }
    public PhysicsBspTree? BSP { get; init; }
    public Dictionary<ushort, Facet>? PhysicsPolygons { get; init; }
    public Orb? BoundingSphere { get; init; }
    public MeshVertices? Vertices { get; init; }

    public Dictionary<ushort, SettledPolygon> Settled { get; init; } = new();

    public PackedKineticBsp? DenseKineticBsp { get; internal set; }

    public PackedGfxObjVisualExtent? VisualLimits { get; init; }

    // True while any parsed dat graph is still retained
    internal bool HoldsGraph
    {
        get
        {
            return BSP is not null || PhysicsPolygons is not null || Vertices is not null || Settled.Count is not 0;
        }
    }
}

/// <summary>A setup's collision volumes and step heights.</summary>
public sealed class SetupKinetics
{
    public uint SourceId { get; init; }
    public List<Capsule> CylSpheres { get; init; } = [];
    public List<Orb> Spheres { get; init; } = [];
    public float Height { get; init; }
    public float Radius { get; init; }
    public float StepUpHeight { get; init; }
    public float StepDownHeight { get; init; }
    public PackedSetupContact? PlanarImpact { get; internal set; }

    internal bool HoldsGraph => CylSpheres.Count is not 0 || Spheres.Count is not 0;
}

/// <summary>An environment cell's collision data plus its placement and portal topology.</summary>
public sealed class CellKinetics
{
    public uint SourceId { get; init; }
    public PhysicsBspTree? BSP { get; init; }
    public Dictionary<ushort, Facet>? PhysicsPolygons { get; init; }
    public MeshVertices? Vertices { get; init; }
    public Matrix4x4 WorldTransform { get; init; }
    public Matrix4x4 InverseWorldTransform { get; init; }

    public required Dictionary<ushort, SettledPolygon> Resolved { get; init; }

    public PackedKineticBsp? PackedKineticBsp { get; init; }

    public CellBspTree? CellBSP { get; init; }

    public PackedCellContainmentBsp? PlanarContainmentBsp { get; init; }

    public PackedPolygonTable? PlanarGatewayPolygs { get; init; }

    public PackedEnvCellTopology? PlanarWiring { get; init; }

    public IReadOnlyList<PortalFacts> Portals { get; init; } = [];

    public Dictionary<ushort, SettledPolygon>? PortalPolygons { get; init; }

    public IReadOnlySet<uint> VisibleCellIds { get; init; } = new HashSet<uint>();

    public bool SeenOutside { get; init; }

    public uint RestrictionObj { get; init; }

    internal bool HoldsGraph
    {
        get
        {
            return BSP is not null || CellBSP is not null || PhysicsPolygons is not null || Vertices is not null
        || Resolved.Count is not 0 || PortalPolygons is not null;
        }
    }
}

// Everything a landblock swap will install into, and remove from, the active cache
internal sealed record BakedKineticCacheLandblock(
    uint LandblockPrefix,
    IReadOnlyList<KeyValuePair<uint, GfxObjKinetics>> GfxObjects,
    IReadOnlyList<KeyValuePair<uint, GfxObjVisualExtent>> VisualBounds,
    IReadOnlyList<KeyValuePair<uint, PackedGfxObjContactAsset>> FlatGfxObjects,
    IReadOnlyList<KeyValuePair<uint, SetupKinetics>> Setups,
    IReadOnlyList<KeyValuePair<uint, PackedSetupContact>> FlatSetups,
    IReadOnlyList<uint> CellIdsToRemove,
    IReadOnlyList<KeyValuePair<uint, CellKinetics>> Cells,
    IReadOnlyList<uint> FlatCellIdsToRemove,
    IReadOnlyList<KeyValuePair<uint, PackedCellStructContactAsset>> FlatCells,
    IReadOnlyList<uint> FlatEnvCellIdsToRemove,
    IReadOnlyList<KeyValuePair<uint, PackedEnvCellTopology>> FlatEnvCells,
    IReadOnlyList<uint> BuildingIdsToRemove,
    IReadOnlyList<KeyValuePair<uint, BuildingKinetics>> Buildings,
    ReadiedChamberGraphLandblock CellGraph);
