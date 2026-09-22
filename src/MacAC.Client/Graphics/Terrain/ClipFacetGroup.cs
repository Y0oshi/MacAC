using System.Buffers;
using System.Numerics;

namespace MacAC.Client.Graphics;

public readonly struct ClipFacetGroup
{
    private const int UpperPlanes = 8;

    private const float CollinearSinEps = 0.0087265f;

    private const float DegenerateRimLength = 1e-6f;

    private const float LowerPolygArea = 1e-7f;
    private readonly Vector4[] _planes;

    private ClipFacetGroup(Vector4[] planes, bool isPlaneOverflow, bool isNothingShown)
    {
        _planes = planes ?? [];
        IsPlaneOverflow = isPlaneOverflow;
        IsNothingShown = isNothingShown;
    }

    public int Count => _planes?.Length ?? 0;

    public IReadOnlyList<Vector4> Planes => _planes ?? (IReadOnlyList<Vector4>)[];

    internal Vector4[] PlaneArr => _planes ?? [];

    public bool IsPlaneOverflow { get; }

    public bool IsNothingShown { get; }

    public static ClipFacetGroup Empty { get; } =
        new([], isPlaneOverflow: false, isNothingShown: true);

    public static ClipFacetGroup From(ChamberLens zone)
    {
        if (zone is null || zone.IsEmpty || zone.Polygons.Count is 0)
            return Empty;

        return zone.Polygons.Count > 1 ? Overflow() : From(zone.Polygons[0]);
    }

    public static ClipFacetGroup From(in LensPolyg polyg)
    {
        if (polyg.IsEmpty)
            return Empty;

        Vector2[] feed = polyg.Vertices;
        Vector2[]? rented = null;
        Span<Vector2> verts = feed.Length <= 32
            ? stackalloc Vector2[feed.Length]
            : (rented = ArrayPool<Vector2>.Shared.Rent(feed.Length)).AsSpan(0, feed.Length);

        try
        {
            int tally = StandardizeAndCombine(feed, verts);

            if (tally < 3)
                return Empty;

            ReadOnlySpan<Vector2> normalized = verts[..tally];

            if (tally > UpperPlanes)
                return Overflow();

            Vector4[] planes = new Vector4[tally];
            for (int idx = 0; idx < tally; ++idx)
            {
                Vector2 p = normalized[idx];
                Vector2 q = normalized[(idx + 1) % tally];
                Vector2 direction = q - p;
                Vector2 num = Vector2.Normalize(new Vector2(-direction.Y, direction.X));
                planes[idx] = new Vector4(num.X, num.Y, 0f, -Vector2.Dot(num, p));
            }
            return new ClipFacetGroup(planes, isPlaneOverflow: false, isNothingShown: false);
        }
        finally
        {
            if (rented is not null)
                ArrayPool<Vector2>.Shared.Return(rented);
        }
    }

    private static ClipFacetGroup Overflow() =>
        new([], isPlaneOverflow: true, isNothingShown: false);

    private static int StandardizeAndCombine(ReadOnlySpan<Vector2> feed, Span<Vector2> pts)
    {
        if (feed.Length < 3)
            return 0;

        int tally = 0;
        foreach (Vector2 vert in feed)
        {
            if (tally is 0 || (vert - pts[tally - 1]).LengthSquared() > DegenerateRimLength * DegenerateRimLength)
                pts[tally++] = vert;
        }
        if (tally >= 2 && (pts[tally - 1] - pts[0]).LengthSquared() <= DegenerateRimLength * DegenerateRimLength)
            --tally;
        if (tally < 3)
            return 0;

        if (SignedArea2(pts[..tally]) < 0f)
            pts[..tally].Reverse();

        bool altered = true;
        while (altered && tally >= 3)
        {
            altered = false;
            for (int idx = 0; idx < tally; ++idx)
            {
                Vector2 earlier = pts[(idx - 1 + tally) % tally];
                Vector2 cur = pts[idx];
                Vector2 upcoming = pts[(idx + 1) % tally];

                Vector2 d0 = cur - earlier;
                Vector2 d1 = upcoming - cur;
                float l0 = d0.Length();
                float l1 = d1.Length();
                if (l0 < DegenerateRimLength || l1 < DegenerateRimLength)
                {
                    pts[(idx + 1)..tally].CopyTo(pts[idx..]);
                    --tally;
                    altered = true;
                    break;
                }

                d0 /= l0;
                d1 /= l1;
                float cross = d0.X * d1.Y - d0.Y * d1.X; // sin θ
                float dot = d0.X * d1.X + d0.Y * d1.Y;   // cos θ
                if (dot > 0f && MathF.Abs(cross) < CollinearSinEps)
                {
                    pts[(idx + 1)..tally].CopyTo(pts[idx..]);
                    --tally; // cur lies on the straight line prev→next
                    altered = true;
                    break;
                }
            }
        }

        if (tally < 3)
            return 0;

        return MathF.Abs(SignedArea2(pts[..tally])) * 0.5f < LowerPolygArea ? 0 : tally;
    }

    private static float SignedArea2(ReadOnlySpan<Vector2> poly)
    {
        float a = 0f;
        for (int idx = 0; idx < poly.Length; ++idx)
        {
            Vector2 p = poly[idx];
            Vector2 q = poly[(idx + 1) % poly.Length];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a;
    }
}
