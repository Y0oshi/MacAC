using System.Collections.Immutable;
using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

// Retail SetPositionError
internal enum PlaceError
{
    Ok = 0,
    GeneralFailure = 1,
    NoValidPosition = 2,
    NoCell = 3,
    Collided = 4,
    InvalidArguments = 0x100,
}

// Whether a placement landed the body in a cell now, later, or not at all
internal enum KineticResidenceVerdict
{
    Committed,
    DeferredCell,
    Unchanged,
}

// What a placement asks the proxy registry to do with the body's shadow
internal enum ProxyCommitAction
{
    None,
    Recalculate,
    Replace,
    Preserve,
}

// Retail SetPositionStruct flags
[Flags]
internal enum KineticSetPositionFlags : uint
{
    None = 0,
    Placement = 0x001,
    Teleport = 0x002,
    Restore = 0x004,
    Slide = 0x010,
    DoNotCreateCells = 0x020,
    Scatter = 0x100,
    RandomScatter = 0x200,
    Line = 0x400,
    SendPositionEvent = 0x1000,
}

// Which placement rules apply: ordinary objects, hooks, storage, or corpses
internal enum KineticPlacementClass
{
    Ordinary,
    Hook,
    Storage,
    Corpse,
}

// Everything a SetPosition needs to know about the mover and where it wants to be
internal readonly record struct KineticSetPositionRequest(
    Vector3 Position,
    Quaternion Orientation,
    uint CellId,
    Vector3 CellLocalPosition,
    ImmutableArray<PackedContactSphere> Spheres,
    float Scale,
    float StepUpHeight,
    float StepDownHeight,
    KineticStateFlags MoverPhysicsState = KineticStateFlags.None,
    MoverState MoverFlags = MoverState.None,
    uint MovingEntityId = 0u,
    KineticPlacementClass PlacementClass = KineticPlacementClass.Ordinary,
    KineticSetPositionFlags Flags = KineticSetPositionFlags.Placement,
    Vector3 Line = default,
    float ScatterRadiusX = 0f,
    float ScatterRadiusY = 0f,
    uint ScatterAttempts = 0u,
    uint? CurrentCellId = null);

// The contact side-channel a placement leaves behind for the body to absorb
internal readonly record struct PlaceContactReport(
    bool ContactPlaneValid,
    Plane ContactPlane,
    uint ContactPlaneCellId,
    bool ContactPlaneIsWater,
    bool LastKnownContactPlaneValid,
    Plane LastKnownContactPlane,
    uint LastKnownContactPlaneCellId,
    bool LastKnownContactPlaneIsWater,
    bool SlidingNormalValid,
    Vector3 SlidingNormal,
    bool CollisionNormalValid,
    Vector3 CollisionNormal,
    bool CollidedWithEnvironment,
    int FramesStationaryFall,
    Vector3 AdjustOffset,
    uint? LastCollidedObjectId,
    ImmutableArray<uint> CollidedObjectIds);

// The result of a SetPosition: where the body ended up and what it touched on the way
internal readonly record struct PlaceOutcome(
    PlaceError Error,
    KineticResidenceVerdict Residence,
    Vector3 Position,
    Quaternion Orientation,
    uint CellId,
    Vector3 CellLocalPosition,
    bool InContact = false,
    bool OnWalkable = false,
    Plane ContactPlane = default,
    uint ContactPlaneCellId = 0u,
    bool ContactPlaneIsWater = false,
    bool SlidingNormalValid = false,
    Vector3 SlidingNormal = default,
    bool CollisionNormalValid = false,
    Vector3 CollisionNormal = default,
    int FramesStationaryFall = 0,
    bool CollidedWithEnvironment = false,
    bool CollisionHandlerResult = false,
    bool CellChanged = false,
    ProxyCommitAction ShadowAction = ProxyCommitAction.None,
    ImmutableArray<uint> CrossCellIds = default,
    ImmutableArray<uint> CollidedObjectIds = default,
    ImmutableArray<uint> QueriedCellIds = default)
{
    internal bool IsSuccessful => Error == PlaceError.Ok;
    internal bool IsSealed => IsSuccessful && Residence == KineticResidenceVerdict.Committed;
    internal bool IsPostponed => IsSuccessful && Residence == KineticResidenceVerdict.DeferredCell;
}
