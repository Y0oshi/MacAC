using System.Numerics;

namespace MacAC.Mechanics.Targeting;

/// <summary>A part's pick geometry: a bounding sphere plus its visual polygons.</summary>
public sealed record CanonPickMesh(
    Vector3 SphereCenter,
    float SphereRadius,
    IReadOnlyList<CanonPickPolygon> Polygons);

/// <summary>One visual polygon; winding and sidedness come straight from the DAT.</summary>
public sealed record CanonPickPolygon(
    IReadOnlyList<Vector3> Vertices,
    bool SingleSided);

/// <summary>One part that survived the ordinary world-render visibility walk.</summary>
public readonly record struct CanonPickPart(
    uint ServerGuid,
    uint LocalEntityId,
    int PartIndex,
    Matrix4x4 LocalToWorld,
    CanonPickMesh Mesh);

public readonly record struct CanonPickHit(
    uint ServerGuid,
    uint LocalEntityId,
    int PartIndex,
    double Distance,
    bool PolygonHit);
