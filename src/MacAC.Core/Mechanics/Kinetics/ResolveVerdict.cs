using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public readonly record struct ResolveVerdict(
    Vector3 Position,
    uint CellId,
    bool IsOnGround,
    bool CollisionNormalValid = false,
    /// <summary>Outward surface normal of the wall the sphere hit. Used by the velocity-reflection step.</summary>
    Vector3 CollisionNormal = default,
    bool Ok = true,
    Quaternion Orientation = default,
    bool InContact = false,
    bool OnWalkable = false,
    Plane ContactPlane = default,
    uint ContactPlaneCellId = 0,
    bool ContactPlaneIsWater = false)
{
    public uint PreviousCollidedObjectIdent { get; init; }

    public bool CollidedWithEnvironment { get; init; }
}
