using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public struct StrideViewVertex
{
    public Vector2 Point;
    public StridePlane Plane;
}

public readonly record struct StrideViewPoly(
    int VertexCount, int VertexIndex, float XMin, float XMax, float YMin, float YMax);

public sealed class StrideViewSet
{
    public readonly List<StrideViewPoly> Polys = [];
    public readonly List<StrideViewVertex> Vertices = [];
    public int VertTallySum;
}

public sealed class StridePortalView
{
    public StridePortalFlags[] GatewayFlagSet = [];

    public readonly StrideViewSet View = new();

    public float UpperInDistanceSquared;

    public int ViewCount;
    public bool ChamberLensDone;
    public int LensStamp;
    public int RefreshTally;

    public void RestartForPush()
    {
        ViewCount = 0;
        RefreshTally = 0;
        LensStamp = 0;
    }
}

public struct StridePortalFlags
{
    public bool Observed;
    public bool InView;
}

public interface IStrideRayCaster
{
    Vector3 RayThrough(float monitorX, float monitorY);
}

public static class StrideCopyView
{
    public const int UpperVerts = 31;
    public const float DedupThreshold = 1f; // strict > 1 px

    public static bool AffixWholeViewRectQuad(
        StridePortalView dest, IStrideRayCaster rays, Vector3 viewpoint,
        float viewRectWidth, float viewRectHeight)
    {
        Span<StrideScreenPoint> quad =
        [
            new(0f, viewRectHeight, 0f, 1f),
            new(viewRectWidth, viewRectHeight, 0f, 1f),
            new(viewRectWidth, 0f, 0f, 1f),
            new(0f, 0f, 0f, 1f),
        ];
        return Append(dest, quad, rays, viewpoint);
    }

    public static bool Append(
        StridePortalView dest, Span<StrideScreenPoint> pts,
        IStrideRayCaster rays, Vector3 viewpoint)
    {
        int npts = pts.Length;
        if (npts is 0) return false;

        Span<bool> keep = stackalloc bool[npts];
        keep[0] = true;
        int num = 1;
        int previous = 0;
        int secondToPrevious = 0;
        int second = 0;

        for (int idx = 0; idx < npts; ++idx)
        {
            ref StrideScreenPoint point = ref pts[idx];
            if (point.W != 1f)
            {
                point.X /= point.W;
                point.Y /= point.W;
                point.W = 1f;
            }
            if (idx is 0) continue;

            bool distinct =
                MathF.Abs(pts[idx].X - pts[previous].X) > DedupThreshold
                || MathF.Abs(pts[idx].Y - pts[previous].Y) > DedupThreshold;
            keep[idx] = distinct;
            if (!distinct) continue;

            if (num is 1)
            {
                ++num;
                second = idx;
            }
            else
            {
                var pp = pts[secondToPrevious];
                var earlier = pts[previous];
                var cur = pts[idx];
                float span = MathF.Max(MathF.Abs(pp.X - cur.X), MathF.Abs(pp.Y - cur.Y));
                float cross = (pp.X - earlier.X) * (earlier.Y - cur.Y)
                              - (pp.Y - earlier.Y) * (earlier.X - cur.X);
                if (MathF.Abs(cross) >= span)
                {
                    ++num;
                    secondToPrevious = previous;
                }
                else
                {
                    keep[previous] = false;
                    if (second == previous) second = idx;
                }
            }
            previous = idx;
        }

        var lead = pts[0];
        bool previousDistinct =
            MathF.Abs(lead.X - pts[previous].X) > DedupThreshold
            || MathF.Abs(lead.Y - pts[previous].Y) > DedupThreshold;
        keep[previous] = previousDistinct;
        if (previousDistinct)
        {
            float span = MathF.Max(
                MathF.Abs(pts[secondToPrevious].X - lead.X),
                MathF.Abs(pts[secondToPrevious].Y - lead.Y));
            float cross = (pts[secondToPrevious].X - pts[previous].X) * (pts[previous].Y - lead.Y)
                          - (pts[previous].X - lead.X) * (pts[secondToPrevious].Y - pts[previous].Y);
            if (MathF.Abs(cross) < span)
            {
                keep[previous] = false;
                --num;
                previous = secondToPrevious;
            }
        }
        else
        {
            --num;
            previous = secondToPrevious;
        }
        secondToPrevious = previous;
        if (second > 0)
        {
            float span = MathF.Max(
                MathF.Abs(pts[secondToPrevious].X - pts[second].X),
                MathF.Abs(pts[secondToPrevious].Y - pts[second].Y));
            float cross = (lead.Y - pts[second].Y) * (pts[secondToPrevious].X - lead.X)
                          - (lead.X - pts[second].X) * (pts[secondToPrevious].Y - lead.Y);
            if (MathF.Abs(cross) < span)
            {
                --num;
                keep[0] = false;
            }
        }

        if (num < 3) return false;
        if (num > UpperVerts) num = UpperVerts;

        var lens = dest.View;
        if (dest.ViewCount is 0)
        {
            lens.Polys.Clear();
            lens.Vertices.Clear();
            lens.VertTallySum = 0;
        }
        int vbase = lens.VertTallySum;
        lens.VertTallySum = vbase + num + 1;

        int written = 0;
        for (int idx = 0; idx < npts && written < num; ++idx)
        {
            if (!keep[idx]) continue;
            lens.Vertices.Add(new StrideViewVertex
            {
                Point = new Vector2(MathF.Abs(pts[idx].X), MathF.Abs(pts[idx].Y)),
            });
            ++written;
        }
        lens.Vertices.Add(new StrideViewVertex { Point = lens.Vertices[vbase].Point });

        float xmin, xmax, ymin, ymax;
        Vector2 seed = lens.Vertices[vbase + num - 1].Point;
        xmin = xmax = seed.X;
        ymin = ymax = seed.Y;
        for (int kdx = num - 2; kdx >= 0; --kdx)
        {
            Vector2 pt = lens.Vertices[vbase + kdx].Point;
            if (pt.X < xmin) xmin = pt.X; else if (pt.X > xmax) xmax = pt.X;
            if (pt.Y < ymin) ymin = pt.Y; else if (pt.Y > ymax) ymax = pt.Y;
        }
        lens.Polys.Add(new StrideViewPoly(num, vbase, xmin, xmax, ymin, ymax));

        Span<Vector3> ray = stackalloc Vector3[num + 1];
        for (int kdx = 0; kdx < num; ++kdx)
        {
            Vector2 pt = lens.Vertices[vbase + kdx].Point;
            ray[kdx] = rays.RayThrough(pt.X, pt.Y);
        }
        ray[num] = ray[0];
        for (int kdx = num - 1; kdx >= 0; --kdx)
        {
            var norm = Vector3.Cross(ray[kdx + 1], ray[kdx]);
            if (MathF.Abs(norm.X) >= StrideVisibilityMath.Epsilon
                || MathF.Abs(norm.Y) >= StrideVisibilityMath.Epsilon
                || MathF.Abs(norm.Z) >= StrideVisibilityMath.Epsilon)
            {
                norm *= 1f / MathF.Sqrt(
                    norm.X * norm.X + norm.Y * norm.Y + norm.Z * norm.Z);
            }
            var vertex = lens.Vertices[vbase + kdx];
            vertex.Plane = new StridePlane(norm, -Vector3.Dot(norm, viewpoint));
            lens.Vertices[vbase + kdx] = vertex;
        }

        dest.ViewCount += 1;
        return true;
    }
}
