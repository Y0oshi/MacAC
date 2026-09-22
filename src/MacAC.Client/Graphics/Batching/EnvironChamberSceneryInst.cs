using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public struct EnvironChamberSceneryInst
{
    public ulong ObjectId;

    public uint InstIdent;

    public bool IsSetup;

    public bool IsStructure;

    public bool IsListingChamber;

    public Vector3 RealmLocus;

    public Vector3 OwnLocus;

    public Quaternion Spin;

    public uint LatestPreviewChamberIdent;

    public Vector3 Scale;

    public Matrix4x4 Transform;

    public BatchBoundingBox OwnBoundingBbox;

    public BatchBoundingBox BoundingBox;

    public uint Flags;
}

public class EnvironChamberLandblock
{
    public int GridX { get; set; }

    public int GridY { get; set; }

    public object Lock { get; } = new();

    public List<EnvironChamberSceneryInst> Instances { get; set; } = [];

    public Dictionary<uint, BatchBoundingBox> EnvironChamberLimits { get; set; } = [];

    /// <summary>Set of EnvCell IDs in this landblock that have the SeenOutside flag.</summary>
    public HashSet<uint> ObservedBeyondChambers { get; set; } = [];

    public Dictionary<ulong, List<InstanceData>> StaticPieceClusters { get; set; } = [];

    public Dictionary<ulong, List<InstanceData>> StructurePieceClusters { get; set; } = [];

    /// <summary>World-space bounding box of this landblock.</summary>
    public BatchBoundingBox BoundingBox { get; set; }

    public BatchBoundingBox SumEnvironChamberLimits { get; set; }

    /// <summary>Whether instances (positions/bounding boxes) have been generated.</summary>
    public bool InstsPrimed { get; set; }

    /// <summary>Whether mesh data for all instances has been prepared (CPU-side).</summary>
    public bool TriMeshBlobPrimed { get; set; }

    /// <summary>Whether GPU resources have been uploaded.</summary>
    public bool GpuPrimed { get; set; }
}
