using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public struct StrideScreenPoint(float x, float y, float z, float w)
{
    public float X = x, Y = y, Z = z, W = w;
}

public static class StrideScreenClip
{
    public const float LowerW = StrideVisibilityMath.Epsilon;

    public static StrideScreenPoint ConvertToMonitor(
        Vector3 pt, in Matrix4x4 objectToClip, float viewRectWidth, float viewRectHeight)
    {
        Vector4 clip = Vector4.Transform(new Vector4(pt, 1f), objectToClip);
        return new StrideScreenPoint(
            clip.X * viewRectWidth * 0.5f + clip.W * viewRectWidth * 0.5f,
            clip.W * viewRectHeight * 0.5f - clip.Y * viewRectHeight * 0.5f,
            clip.Z,
            clip.W);
    }

    public static int ClipAgainstLens(
        ReadOnlySpan<StrideScreenPoint> feed,
        ReadOnlySpan<Vector2> lensRimVerts,
        Span<StrideScreenPoint> product)
    {
        Span<StrideScreenPoint> bufA = stackalloc StrideScreenPoint[64];
        Span<StrideScreenPoint> bufB = stackalloc StrideScreenPoint[64];
        var latest = bufA;
        int tally = feed.Length;
        feed.CopyTo(latest);
        int reversals = 0;

        bool anyBelow = false;
        for (int idx = 0; idx < tally; ++idx)
            if (latest[idx].W < LowerW) { anyBelow = true; break; }
        if (anyBelow)
        {
            tally = ClipPassW(latest[..tally], bufB);
            if (tally < 3) return 0;
            var swap = latest;
            latest = bufB;
            bufB = swap;
            ++reversals;
        }

        int num = lensRimVerts.Length;
        for (int e = num - 1; e >= 0; --e)
        {
            Vector2 a = lensRimVerts[e == num - 1 ? 0 : e + 1];
            Vector2 b = lensRimVerts[e];
            tally = ClipPassRim(latest[..tally], a, b, bufB);
            if (tally < 3) return 0;
            var swap = latest;
            latest = bufB;
            bufB = swap;
            ++reversals;
        }

        // Restore original winding: each pass reversed the order once
        if ((reversals & 1) is not 0)
        {
            for (int idx = 0; idx < tally; ++idx)
                product[idx] = latest[tally - 1 - idx];
        }
        else
        {
            latest[..tally].CopyTo(product);
        }
        return tally;
    }

    private static int ClipPassW(ReadOnlySpan<StrideScreenPoint> pts, Span<StrideScreenPoint> outPts)
    {
        int outTally = 0;
        var earlier = pts[0];
        float sEarlier = earlier.W - LowerW;
        bool inEarlier = sEarlier >= 0f;
        for (int idx = pts.Length - 1; idx >= 0; --idx)
        {
            var cur = pts[idx];
            float s = cur.W - LowerW;
            bool inCur = s >= 0f;
            if (inEarlier != inCur)
                outPts[outTally++] = Lerp(earlier, cur, sEarlier / (sEarlier - s));
            if (inCur)
                outPts[outTally++] = cur;
            earlier = cur; sEarlier = s; inEarlier = inCur;
        }
        return outTally;
    }

    private static int ClipPassRim(
        ReadOnlySpan<StrideScreenPoint> pts, Vector2 a, Vector2 b, Span<StrideScreenPoint> outPts)
    {
        float exc = b.X - a.X;
        float ey = b.Y - a.Y;
        float Flank(in StrideScreenPoint point) => (point.X - a.X * point.W) * ey - (point.Y - a.Y * point.W) * exc;

        int outTally = 0;
        var earlier = pts[0];
        float s0 = Flank(earlier);
        float sEarlier = s0;
        bool inEarlier = s0 <= 0f;
        for (int idx = pts.Length - 1; idx >= 0; --idx)
        {
            var cur = pts[idx];
            float s = idx is not 0 ? Flank(cur) : s0;   // final pair reuses point 0's side
            bool inCur = s <= 0f;
            if (inEarlier != inCur)
                outPts[outTally++] = Lerp(earlier, cur, sEarlier / (sEarlier - s));
            if (inCur)
                outPts[outTally++] = cur;
            earlier = cur; sEarlier = s; inEarlier = inCur;
        }
        return outTally;
    }

    private static StrideScreenPoint Lerp(in StrideScreenPoint point, in StrideScreenPoint q, float t)
    {
        return new(
                point.X + (q.X - point.X) * t,
                point.Y + (q.Y - point.Y) * t,
                point.Z + (q.Z - point.Z) * t,
                point.W + (q.W - point.W) * t);
    }
}
