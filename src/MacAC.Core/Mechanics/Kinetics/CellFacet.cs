using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>A cell's polygons fan-triangulated in world space, for point-in-column floor sampling.</summary>
public sealed class CellFacet
{
    private readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C);

    private const float DegenerateArea = 1e-10f;
    private const float RimSlack = 1e-6f;

    private readonly List<Tri> _tris;

    public uint CellId { get; }

    public CellFacet(uint chamberIdent, Dictionary<ushort, Vector3> verts, List<List<short>> polygVertIdents)
    {
        CellId = chamberIdent;
        _tris = [];

        List<Vector3> loop = new List<Vector3>();
        foreach (List<short> polyg in polygVertIdents)
        {
            if (polyg.Count < 3)
                continue;

            loop.Clear();
            bool done = true;
            foreach (short vertIdent in polyg)
            {
                if (!verts.TryGetValue((ushort)vertIdent, out Vector3 at))
                {
                    done = false;
                    break;
                }
                loop.Add(at);
            }
            if (!done)
                continue;

            Fan(loop);
        }
    }

    public CellFacet(uint chamberIdent, PackedPolygonTable polygs, Quaternion spin, Vector3 translation)
    {
        ArgumentNullException.ThrowIfNull(polygs);
        CellId = chamberIdent;

        int allowance = 0;
        foreach (PackedContactPolygon polyg in polygs.Polygons)
            allowance += Math.Max(0, polyg.VertexRange.Count - 2);
        _tris = new List<Tri>(allowance);

        foreach (PackedContactPolygon polyg in polygs.Polygons)
        {
            var span = polyg.VertexRange;
            if (span.Count < 3)
                continue;

            Vector3 pivot = Place(polygs.Vertices[span.Start], spin, translation);
            Vector3 trailing = Place(polygs.Vertices[span.Start + 1], spin, translation);
            for (int kdx = 2; kdx < span.Count; ++kdx)
            {
                Vector3 leading = Place(polygs.Vertices[span.Start + kdx], spin, translation);
                _tris.Add(new Tri(pivot, trailing, leading));
                trailing = leading;
            }
        }
    }

    public float? ProbeFloorZ(float realmX, float realmY)
    {
        foreach (Tri tri in _tris)
        {
            if (ColumnStrikes(realmX, realmY, tri, out float z))
                return z;
        }
        return null;
    }

    private static Vector3 Place(Vector3 vert, Quaternion spin, Vector3 translation) =>
        Vector3.Transform(vert, spin) + translation;

    // Fan triangulation: (v0, v1, v2), (v0, v2, v3), ..
    private void Fan(List<Vector3> loop)
    {
        for (int idx = 1; idx < loop.Count - 1; ++idx)
            _tris.Add(new Tri(loop[0], loop[idx], loop[idx + 1]));
    }

    // Barycentric point-in-triangle on the XY projection, interpolating Z on a hit
    private static bool ColumnStrikes(float px, float py, in Tri tri, out float z)
    {
        z = 0;
        (Vector3 a, Vector3 b, Vector3 c) = tri;

        float v0x = c.X - a.X, v0y = c.Y - a.Y;
        float v1x = b.X - a.X, v1y = b.Y - a.Y;
        float v2x = px - a.X, v2y = py - a.Y;

        float dot00 = v0x * v0x + v0y * v0y;
        float dot01 = v0x * v1x + v0y * v1y;
        float dot02 = v0x * v2x + v0y * v2y;
        float dot11 = v1x * v1x + v1y * v1y;
        float dot12 = v1x * v2x + v1y * v2y;

        float denom = dot00 * dot11 - dot01 * dot01;
        if (MathF.Abs(denom) < DegenerateArea)
            return false;

        float invDenom = 1f / denom;
        float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
        float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
        if (u < -RimSlack || v < -RimSlack || u + v > 1f + RimSlack)
            return false;

        z = a.Z * (1 - u - v) + b.Z * v + c.Z * u;
        return true;
    }
}
