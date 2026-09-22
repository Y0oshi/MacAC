using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

// Packed cell-containment queries: point, sphere and box against the containment rows
internal static partial class PackedBspQuery
{
    public static bool PtInsideCellBsp(
        PackedCellContainmentBsp tree,
        Vector3 pt)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return PtInsideChamberBsp(tree, tree.TrunkIdx, pt);
    }

    public static bool SphereIntersectsCellBsp(
        PackedCellContainmentBsp tree,
        Vector3 middle,
        float radius)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return SphereIntersectsCellBsp(tree, tree.TrunkIdx, middle, radius);
    }

    public static bool BoxIntersectsCellBsp(
        PackedCellContainmentBsp tree,
        Vector3 lower,
        Vector3 upper)
    {
        ArgumentNullException.ThrowIfNull(tree);
        return BoxIntersectsCellBsp(tree, tree.TrunkIdx, lower, upper);
    }

    private static bool PtInsideChamberBsp(
        PackedCellContainmentBsp tree,
        int jointOrdinal,
        Vector3 pt)
    {
        if (jointOrdinal < 0)
            return true;

        var joint = tree.Nodes[jointOrdinal];
        if (joint.Type == BspTag.Leaf)
            return true;

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, pt) + joint.SplittingPlane.D;
        if (gap >= 0f)
        {
            return joint.PositiveDescendantOrdinal >= 0
                ? PtInsideChamberBsp(tree, joint.PositiveDescendantOrdinal, pt)
                : true;
        }

        return false;
    }

    private static bool SphereIntersectsCellBsp(
        PackedCellContainmentBsp tree,
        int jointOrdinal,
        Vector3 middle,
        float radius)
    {
        if (jointOrdinal < 0)
            return true;

        var joint = tree.Nodes[jointOrdinal];
        if (joint.Type == BspTag.Leaf)
            return true;

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, middle) + joint.SplittingPlane.D;
        float expandedRadius = radius + 0.01f;
        if (gap < -expandedRadius)
            return false;

        return joint.PositiveDescendantOrdinal >= 0
            ? SphereIntersectsCellBsp(
                tree,
                joint.PositiveDescendantOrdinal,
                middle,
                radius)
            : true;
    }

    private static bool BoxIntersectsCellBsp(
        PackedCellContainmentBsp tree,
        int jointOrdinal,
        Vector3 lower,
        Vector3 upper)
    {
        if (jointOrdinal < 0)
            return true;

        var joint = tree.Nodes[jointOrdinal];
        if (joint.Type == BspTag.Leaf)
            return true;

        if (CellBspProbe.ClassifyBbox(joint.SplittingPlane, lower, upper) ==
            CellBspProbe.FacetFlank.Negative)

            return false;

        return joint.PositiveDescendantOrdinal >= 0
            ? BoxIntersectsCellBsp(
                tree,
                joint.PositiveDescendantOrdinal,
                lower,
                upper)
            : true;
    }
}
