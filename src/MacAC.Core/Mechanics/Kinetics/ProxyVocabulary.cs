using System.Numerics;
using System.Runtime.CompilerServices;

namespace MacAC.Mechanics.Kinetics;

public enum ProxyContactType : byte { BSP, Cylinder, Sphere }

public readonly record struct ProxyEntry(
    uint EntityId,
    uint GfxObjId,
    Vector3 Position,
    Quaternion Rotation,
    float Radius,
    ProxyContactType CollisionType = ProxyContactType.BSP,
    float CylHeight = 0f,
    float Scale = 1.0f,
    uint State = 0u,
    ActorImpactFlagSet Flags = ActorImpactFlagSet.None,
    Vector3 LocalPosition = default,
    Quaternion LocalRotation = default);

public enum CanonCellSetRoute
{
    None,

    Cylsphere,

    BoundingBox,
}

public readonly record struct CanonPartRow(
    uint EntityId,
    int PartIndex,
    uint GfxObjId,
    uint CellId,
    bool ClipPlanesRequired);
