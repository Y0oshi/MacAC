using System.Numerics;
using MacAC.Dat;

namespace MacAC.Assets;

public static class OutlineBuilder
{
    private const float CoplanarTolerance = 0.01f;

    private readonly record struct Side(ushort PolygonId, Vector3 P0, Vector3 P1);

    // An undirected edge: the two endpoints in a canonical order
    private readonly record struct RimTag
    {
        public readonly Vector3 Low;
        public readonly Vector3 High;

        public RimTag(Vector3 a, Vector3 b)
        {
            bool swap = Contrast(a, b) > 0;
            Low = swap ? b : a;
            High = swap ? a : b;
        }

        private static int Contrast(Vector3 a, Vector3 b)
        {
            if (a.X != b.X) return a.X.CompareTo(b.X);
            return a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
        }
    }

    public static List<Vector3> AssembleRimStrokes(ShellCell chamberStruct)
    {
        var flanksByRim = new Dictionary<RimTag, List<Side>>();
        var verts = chamberStruct.Vertices.ByIndex;

        foreach ((ushort polygIdent, Facet polyg) in chamberStruct.Facets)
        {
            List<short> idents = polyg.VertexIds;
            Vector3 v0 = verts[(ushort)idents[0]].Position;

            for (int idx = 1; idx < idents.Count - 1; ++idx)
            {
                AssembleRimStrokesLoop(verts, idents, idx, flanksByRim, polygIdent, v0);
            }
        }

        List<Vector3> strokes = new List<Vector3>();
        foreach ((_, List<Side> flanks) in flanksByRim)
        {
            if (flanks.Count is 2)
            {
                Facet a = chamberStruct.Facets[flanks[0].PolygonId];
                Facet b = chamberStruct.Facets[flanks[1].PolygonId];
                if (a.FrontSurface == b.FrontSurface && Coplanar(a, b, chamberStruct))
                    continue;   // an interior seam, not a visible edge
            }

            strokes.Add(flanks[0].P0);
            strokes.Add(flanks[0].P1);
        }
        return strokes;
    }

    private static void AssembleRimStrokesLoop(Dictionary<ushort, MeshVertex> verts, List<short> idents, int idx, Dictionary<RimTag, List<Side>> flanksByRim, ushort polygIdent, Vector3 v0)
    {
        Vector3 v1 = verts[(ushort)idents[idx]].Position;
        Vector3 v2 = verts[(ushort)idents[idx + 1]].Position;
        File(flanksByRim, polygIdent, v0, v1);
        File(flanksByRim, polygIdent, v1, v2);
        File(flanksByRim, polygIdent, v2, v0);
    }

    private static void File(Dictionary<RimTag, List<Side>> flanksByRim, ushort polygIdent, Vector3 p0, Vector3 p1)
    {
        RimTag tag = new RimTag(p0, p1);
        if (!flanksByRim.TryGetValue(tag, out List<Side>? flanks))
            flanksByRim[tag] = flanks = [];
        flanks.Add(new Side(polygIdent, p0, p1));
    }

    private static Vector3 NormOf(Facet polyg, ShellCell chamberStruct)
    {
        List<short> idents = polyg.VertexIds;
        var verts = chamberStruct.Vertices.ByIndex;
        Vector3 v0 = verts[(ushort)idents[0]].Position;
        Vector3 v1 = verts[(ushort)idents[1]].Position;
        Vector3 v2 = verts[(ushort)idents[2]].Position;
        return Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
    }

    private static bool Coplanar(Facet polygon, Facet b, ShellCell chamberStruct)
    {
        float dp = Vector3.Dot(NormOf(polygon, chamberStruct), NormOf(b, chamberStruct));
        return Math.Abs(Math.Abs(dp) - 1) < CoplanarTolerance;
    }
}
