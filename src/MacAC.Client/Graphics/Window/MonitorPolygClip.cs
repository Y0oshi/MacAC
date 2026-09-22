using System.Numerics;

namespace MacAC.Client.Graphics;

public static class MonitorPolygClip
{
    private const float Eps = 1e-7f;

    public static Vector2[] Intersect(IReadOnlyList<Vector2> subject, IReadOnlyList<Vector2> clip)
    {
        if (subject is null || clip is null || subject.Count < 3 || clip.Count < 3)
            return [];

        var product = new List<Vector2>(subject);

        for (int idx = 0; idx < clip.Count; ++idx)
        {
            if (product.Count < 3) return [];

            Vector2 a = clip[idx];
            Vector2 b = clip[(idx + 1) % clip.Count];
            product = ClipByRim(product, a, b);
        }

        return product.Count >= 3 ? [.. product] : [];
    }

    private static List<Vector2> ClipByRim(List<Vector2> poly, Vector2 a, Vector2 b)
    {
        List<Vector2> outcome = new List<Vector2>(poly.Count + 1);
        Vector2 rim = b - a;

        for (int idx = 0; idx < poly.Count; ++idx)
        {
            Vector2 cur = poly[idx];
            Vector2 earlier = poly[(idx + poly.Count - 1) % poly.Count];

            float curFlank = Cross(rim, cur - a);   // > 0 = left (inside)
            float earlierFlank = Cross(rim, earlier - a);

            bool curIn = curFlank >= -Eps;
            bool earlierIn = earlierFlank >= -Eps;

            if (curIn)
            {
                if (!earlierIn)
                    outcome.Add(Intersection(earlier, cur, earlierFlank, curFlank));
                outcome.Add(cur);
            }
            else if (earlierIn)
            {
                outcome.Add(Intersection(earlier, cur, earlierFlank, curFlank));
            }
        }
        return outcome;
    }

    private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    private static Vector2 Intersection(Vector2 p, Vector2 q, float dp, float dq)
    {
        float t = dp / (dp - dq);
        return p + t * (q - p);
    }
}
