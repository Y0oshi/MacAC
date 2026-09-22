using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Range tests between two upright cylinders, in the three flavours the retail client used.</summary>
public static class ReachMath
{
    public static bool ObjectsInSpan(
        Vector3 leadLocus,
        float leadRadius,
        float leadHeight,
        Vector3 secondLocus,
        float secondRadius,
        float secondHeight,
        double span,
        bool useRadii,
        bool ignoreZDiff)
    {
        double gap = ignoreZDiff
            ? PlanarGap(leadLocus, secondLocus)
            : useRadii
                ? ApproachMath.CylinderGap(leadRadius, leadHeight, leadLocus, secondRadius, secondHeight, secondLocus)
                : Vector3.Distance(leadLocus, secondLocus);
        return gap <= span;
    }

    private static double PlanarGap(Vector3 a, Vector3 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
