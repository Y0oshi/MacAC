using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public static class StrideVisibilityMath
{
    public const float Epsilon = 0.000199999995f;

    public const float HeavensHeight = 1000f;

    public const float InsideColumn = 0f;

    public const float BeyondColumn = 1001f;

    public static float FetchPtThreshold(float x, float y, in StridePlane plane)
    {
        Vector3 num = plane.Normal;
        if (num.Z > Epsilon)
        {
            float h = -((x * num.X + y * num.Y + plane.D) / num.Z);
            return h >= HeavensHeight ? BeyondColumn : h > 0f ? -h : InsideColumn;
        }
        if (num.Z < -Epsilon)
        {
            float h = -((x * num.X + y * num.Y + plane.D) / num.Z);
            return h <= 0f ? BeyondColumn : h >= HeavensHeight ? InsideColumn : h;
        }
        float d = x * num.X + y * num.Y + plane.D;
        return d < -Epsilon ? BeyondColumn : InsideColumn;
    }

    public static void PopulateClipHeights(
        float x, float y, in StridePlane cyPlane, ReadOnlySpan<StridePlane> rimPlanes,
        Span<float> limits)
    {
        limits[0] = FetchPtThreshold(x, y, cyPlane);
        for (int idx = 0; idx < rimPlanes.Length; ++idx)
            limits[idx + 1] = FetchPtThreshold(x, y, rimPlanes[idx]);
    }

    public static StrideBoundingType CornerPlaneVerify(float tied, float lowerZ, float upperZ)
    {
        if (tied == BeyondColumn) return StrideBoundingType.Outside;
        if (tied != InsideColumn)
        {
            if (tied <= 0f)
            {
                float h = -tied;
                if (h > lowerZ)
                {
                    return upperZ <= h ? StrideBoundingType.Outside : StrideBoundingType.PartiallyInside;
                }
            }
            else if (tied < upperZ)
            {
                return tied <= lowerZ ? StrideBoundingType.Outside : StrideBoundingType.PartiallyInside;
            }
        }
        return StrideBoundingType.EntirelyInside;
    }

    public static StrideBoundingType ChunkPlaneVerify(
        float b1, float b2, float b3, float b4, float lowerZ, float upperZ)
    {
        var c1 = CornerPlaneVerify(b1, lowerZ, upperZ);
        var c2 = CornerPlaneVerify(b2, lowerZ, upperZ);
        var c3 = CornerPlaneVerify(b3, lowerZ, upperZ);
        var c4 = CornerPlaneVerify(b4, lowerZ, upperZ);
        if (c1 == StrideBoundingType.Outside)
        {
            if (c2 == StrideBoundingType.Outside
                && c3 == StrideBoundingType.Outside
                && c4 == StrideBoundingType.Outside)

                return StrideBoundingType.Outside;
        }
        else if (c1 == StrideBoundingType.EntirelyInside
                 && c2 == StrideBoundingType.EntirelyInside
                 && c3 == StrideBoundingType.EntirelyInside
                 && c4 == StrideBoundingType.EntirelyInside)
        {
            return StrideBoundingType.EntirelyInside;
        }
        return StrideBoundingType.PartiallyInside;
    }

    public static StrideBoundingType ChunkVerify(
        ReadOnlySpan<float> corner00, ReadOnlySpan<float> corner01,
        ReadOnlySpan<float> corner10, ReadOnlySpan<float> corner11,
        int planeTally, float upperZ, float lowerZ)
    {
        var outcome = ChunkPlaneVerify(
            corner00[0], corner01[0], corner10[0], corner11[0], lowerZ, upperZ);
        if (outcome == StrideBoundingType.Outside) return StrideBoundingType.Outside;
        for (int kdx = 1; kdx <= planeTally; ++kdx)
        {
            var r = ChunkPlaneVerify(
                corner00[kdx], corner01[kdx], corner10[kdx], corner11[kdx], lowerZ, upperZ);
            if (r == StrideBoundingType.Outside) return StrideBoundingType.Outside;
            if (r == StrideBoundingType.PartiallyInside)
                outcome = StrideBoundingType.PartiallyInside;
        }
        return outcome;
    }

    public static StrideBoundingType ViewconeVerify(
        Vector3 middle, float radius, in StridePlane cyPlane,
        ReadOnlySpan<StridePlane> rimPlanes)
    {
        float d = Vector3.Dot(cyPlane.Normal, middle) + cyPlane.D;
        if (d < -radius) return StrideBoundingType.Outside;
        bool partial = d <= radius;
        foreach (ref readonly StridePlane plane in rimPlanes)
        {
            d = Vector3.Dot(plane.Normal, middle) + plane.D;
            if (d < -radius) return StrideBoundingType.Outside;
            if (d <= radius) partial = true;
        }
        return partial ? StrideBoundingType.PartiallyInside : StrideBoundingType.EntirelyInside;
    }

    public static bool IsRejectedByGatewayPolygBoundaryGuard(ReadOnlySpan<Vector3> ownVerts)
    {
        bool everyVertOnPlusX = true;
        bool everyVertOnMinusX = true;
        bool everyVertOnPlusY = true;
        bool everyVertOnMinusY = true;

        for (int idx = 0; idx < ownVerts.Length; ++idx)
        {
            float x = ownVerts[idx].X;
            float y = ownVerts[idx].Y;
            if (x != 12f) everyVertOnPlusX = false;
            if (x != -12f) everyVertOnMinusX = false;
            if (y != 12f) everyVertOnPlusY = false;
            if (y != -12f) everyVertOnMinusY = false;
        }

        return everyVertOnPlusX || everyVertOnMinusX
            || everyVertOnPlusY || everyVertOnMinusY;
    }
}

public readonly record struct StridePlane(Vector3 Normal, float D);

public enum StrideBoundingType
{
    Outside = 0,
    PartiallyInside = 1,
    EntirelyInside = 2,
}
