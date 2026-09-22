using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A portal polygon reduced to its plane, centroid and bounding radius for crossing tests.</summary>
/// <param name="OwnerCellId">The EnvCell that owns this portal.</param>
/// <param name="Flags">PortalFlags value.</param>
/// <param name="Radius">Bounding radius of the portal polygon.</param>
public readonly record struct PortalFace(
    Vector3 Normal,
    float D,
    uint TargetCellId,
    uint OwnerCellId,
    ushort Flags,
    Vector3 Centroid,
    float Radius)
{
    public static PortalFace FromVertices(ReadOnlySpan<Vector3> vertices, uint markChamberIdent, uint holderChamberIdent, ushort flagSet)
    {
        if (vertices.Length < 3)
            throw new ArgumentException("Need no fewer than 3 vertices", nameof(vertices));

        Vector3 norm = Vector3.Normalize(Vector3.Cross(vertices[1] - vertices[0], vertices[2] - vertices[0]));
        float d = -Vector3.Dot(norm, vertices[0]);

        Vector3 total = Vector3.Zero;
        foreach (Vector3 v in vertices)
            total += v;
        Vector3 centroid = total / vertices.Length;

        float reach = 0f;
        foreach (Vector3 v in vertices)
        {
            float r = Vector3.Distance(centroid, v);
            if (r > reach)
                reach = r;
        }

        return new PortalFace(norm, d, markChamberIdent, holderChamberIdent, flagSet, centroid, reach);
    }

    public static PortalFace FromVertices(Vector3 v0, Vector3 v1, Vector3 v2, uint markChamberIdent, uint holderChamberIdent, ushort flagSet)
    {
        ReadOnlySpan<Vector3> triangle = [v0, v1, v2];
        return FromVertices(triangle, markChamberIdent, holderChamberIdent, flagSet);
    }

    public bool IsCrossing(Vector3 formerSpot, Vector3 newSpot)
    {
        float dx = MathF.Min(MathF.Abs(formerSpot.X - Centroid.X), MathF.Abs(newSpot.X - Centroid.X));
        float dy = MathF.Min(MathF.Abs(formerSpot.Y - Centroid.Y), MathF.Abs(newSpot.Y - Centroid.Y));
        if (MathF.Sqrt(dx * dx + dy * dy) > Radius)
            return false;

        float prior = Vector3.Dot(Normal, formerSpot) + D;
        float following = Vector3.Dot(Normal, newSpot) + D;
        return prior * following < 0f;
    }
}
