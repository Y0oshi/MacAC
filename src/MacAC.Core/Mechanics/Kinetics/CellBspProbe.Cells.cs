using System.Numerics;
using MacAC.Dat;
using Plane = System.Numerics.Plane;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Cell-containment BSP queries (point, sphere, box) and plane-side classification.</summary>
public static partial class CellBspProbe
{
    internal const float BboxPlaneEpsilon = 0.000199999995f;

    internal enum FacetFlank
    {
        Positive = 0,
        Negative = 1,
        Straddle = 2,
    }

    public static bool PtInsideChamberBsp(CellBspNode? joint, Vector3 pt)
    {
        if (joint is null) return true;
        if (joint.Tag == BspTag.Leaf) return true;

        float distance = Vector3.Dot(joint.Splitter.Normal, pt) + joint.Splitter.D;

        if (distance >= 0f)
            return joint.Front is not null ? PtInsideChamberBsp(joint.Front, pt) : true;

        // Behind → outside
        return false;
    }

    public static bool SphereIntersectsCellBsp(CellBspNode? joint, Vector3 middle, float radius)
    {
        if (joint is null) return true;
        if (joint.Tag == BspTag.Leaf) return true;

        float distance = Vector3.Dot(joint.Splitter.Normal, middle) + joint.Splitter.D;
        float rad = radius + 0.01f;

        // Behind splitting plane by more than radius → sphere fully outside
        if (distance < -rad) return false;

        return joint.Front is not null
            ? SphereIntersectsCellBsp(joint.Front, middle, radius)
            : true;
    }

    public static bool BoxIntersectsCellBsp(CellBspNode? joint, Vector3 lower, Vector3 upper)
    {
        if (joint is null) return true;
        if (joint.Tag == BspTag.Leaf) return true;

        if (ClassifyBbox(joint.Splitter, lower, upper) == FacetFlank.Negative)
            return false;

        return joint.Front is not null
            ? BoxIntersectsCellBsp(joint.Front, lower, upper)
            : true;
    }

    internal static FacetFlank WhichFlank(in Plane plane, Vector3 pt, float eps)
    {
        float distance = Vector3.Dot(plane.Normal, pt) + plane.D;
        if (distance >= eps) return FacetFlank.Positive;
        return distance < -eps ? FacetFlank.Negative : FacetFlank.Straddle;
    }

    internal static FacetFlank ClassifyBbox(in Plane plane, Vector3 lower, Vector3 upper)
    {
        Span<Vector3> corners =
        [
            new Vector3(lower.X, lower.Y, lower.Z),
            new Vector3(upper.X, upper.Y, upper.Z),
            new Vector3(lower.X, lower.Y, upper.Z),
            new Vector3(lower.X, upper.Y, lower.Z),
            new Vector3(upper.X, lower.Y, lower.Z),
            new Vector3(upper.X, lower.Y, upper.Z),
            new Vector3(lower.X, upper.Y, upper.Z),
            new Vector3(upper.X, upper.Y, lower.Z),
        ];

        FacetFlank side0 = WhichFlank(plane, corners[0], BboxPlaneEpsilon);
        if (side0 == FacetFlank.Straddle) return FacetFlank.Straddle;

        for (int idx = 1; idx < corners.Length; ++idx)
        {
            if (WhichFlank(plane, corners[idx], BboxPlaneEpsilon) != side0)
                return FacetFlank.Straddle;
        }

        return side0;
    }
}
