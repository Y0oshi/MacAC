using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

// Row walks over the packed tree: which polygons a sphere hits, supports, or is buried in
internal static partial class PackedBspQuery
{
    private static bool TraverseFacingStrike(
        PackedKineticBsp tree,
        int jointOrdinal,
        Ball orb,
        Vector3 travel,
        ref int strikePolygOrdinal,
        ref Vector3 linkPt)
    {
        if (jointOrdinal < 0)
            return false;

        var joint = tree.Joints[jointOrdinal];
        if (!Overlaps(joint, orb))
            return false;

        if (joint.Type == BspTag.Leaf)
        {
            if (joint.PolygonIndexRange.Count is 0)
                return false;

            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                if (SmackFacing(
                        tree,
                        polygOrdinal,
                        orb,
                        travel,
                        ref linkPt,
                        ref strikePolygOrdinal))

                    return true;
            }

            return false;
        }

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, orb.Center) +
            joint.SplittingPlane.D;
        float reach = orb.Radius - Eps;

        if (gap >= reach)
        {
            return TraverseFacingStrike(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                travel,
                ref strikePolygOrdinal,
                ref linkPt);
        }

        if (gap <= -reach)
        {
            return TraverseFacingStrike(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                travel,
                ref strikePolygOrdinal,
                ref linkPt);
        }

        if (joint.PositiveDescendantOrdinal >= 0 &&
            TraverseFacingStrike(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                travel,
                ref strikePolygOrdinal,
                ref linkPt))

            return true;

        if (joint.NegativeDescendantOrdinal >= 0 &&
            TraverseFacingStrike(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                travel,
                ref strikePolygOrdinal,
                ref linkPt))

            return true;

        return false;
    }

    private static void TraversePassable(
        PackedKineticBsp tree,
        int jointOrdinal,
        SweepPath trail,
        ref Ball validLocus,
        Vector3 travel,
        Vector3 up,
        ref int strikePolygOrdinal,
        ref ushort strikePolygIdent,
        ref bool altered)
    {
        if (jointOrdinal < 0)
            return;

        var joint = tree.Joints[jointOrdinal];
        if (!Overlaps(joint, validLocus))
            return;

        if (joint.Type == BspTag.Leaf)
        {
            if (joint.PolygonIndexRange.Count is 0)
                return;

            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                bool passable = TouchesPassable(
                    tree,
                    polygOrdinal,
                    trail,
                    validLocus,
                    up);
                bool adjusted = passable &&
                    PushBack(
                        tree,
                        polygOrdinal,
                        trail,
                        ref validLocus,
                        travel);

                if (passable && adjusted)
                {
                    altered = true;
                    strikePolygOrdinal = polygOrdinal;
                    strikePolygIdent = tree.PolygChart.Polygons[polygOrdinal].Id;
                }
            }

            return;
        }

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, validLocus.Center) +
            joint.SplittingPlane.D;
        float reach = validLocus.Radius - Eps;

        if (gap >= reach)
        {
            TraversePassable(
                tree,
                joint.PositiveDescendantOrdinal,
                trail,
                ref validLocus,
                travel,
                up,
                ref strikePolygOrdinal,
                ref strikePolygIdent,
                ref altered);
            return;
        }

        if (gap <= -reach)
        {
            TraversePassable(
                tree,
                joint.NegativeDescendantOrdinal,
                trail,
                ref validLocus,
                travel,
                up,
                ref strikePolygOrdinal,
                ref strikePolygIdent,
                ref altered);
            return;
        }

        TraversePassable(
            tree,
            joint.PositiveDescendantOrdinal,
            trail,
            ref validLocus,
            travel,
            up,
            ref strikePolygOrdinal,
            ref strikePolygIdent,
            ref altered);
        TraversePassable(
            tree,
            joint.NegativeDescendantOrdinal,
            trail,
            ref validLocus,
            travel,
            up,
            ref strikePolygOrdinal,
            ref strikePolygIdent,
            ref altered);
    }

    private static bool TraverseSupported(
        PackedKineticBsp tree,
        int jointOrdinal,
        SweepPath trail,
        Ball orb,
        Vector3 up)
    {
        if (jointOrdinal < 0)
            return false;

        var joint = tree.Joints[jointOrdinal];
        if (!Overlaps(joint, orb))
            return false;

        if (joint.Type == BspTag.Leaf)
        {
            if (joint.PolygonIndexRange.Count is 0)
                return false;

            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                if (TouchesPassable(tree, polygOrdinal, trail, orb, up) &&
                    Supports(tree, polygOrdinal, orb, up, small: true))

                    return true;
            }

            return false;
        }

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, orb.Center) +
            joint.SplittingPlane.D;
        float reach = orb.Radius - Eps;

        if (gap >= reach)
        {
            return TraverseSupported(
                tree,
                joint.PositiveDescendantOrdinal,
                trail,
                orb,
                up);
        }

        if (gap <= -reach)
        {
            return TraverseSupported(
                tree,
                joint.NegativeDescendantOrdinal,
                trail,
                orb,
                up);
        }

        if (TraverseSupported(
                tree,
                joint.PositiveDescendantOrdinal,
                trail,
                orb,
                up))

            return true;

        return TraverseSupported(
            tree,
            joint.NegativeDescendantOrdinal,
            trail,
            orb,
            up);
    }

    private static bool TraverseSolid(
        PackedKineticBsp tree,
        int jointOrdinal,
        Ball orb,
        bool middleVerify)
    {
        if (jointOrdinal < 0)
            return false;

        var joint = tree.Joints[jointOrdinal];
        if (joint.Type == BspTag.Leaf)
        {
            if (joint.PolygonIndexRange.Count is 0)
                return false;

            if (middleVerify && joint.Solid is not 0)
            {
                if (KineticTelemetry.ProbePlacementFailEnabled)
                    KineticTelemetry.PreviousStanceFailSolidLeaf = true;
                return true;
            }

            if (!Overlaps(joint, orb))
                return false;

            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                if (!Touches(tree, polygOrdinal, orb))
                    continue;

                NoteStrikeForSensors(tree, polygOrdinal);
                if (KineticTelemetry.ProbePlacementFailEnabled)
                {
                    TraverseSolidBranch(tree, polygOrdinal);
                }

                return true;
            }

            return false;
        }

        if (!Overlaps(joint, orb))
            return false;

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, orb.Center) +
            joint.SplittingPlane.D;
        float reach = orb.Radius - Eps;

        if (gap >= reach)
        {
            return TraverseSolid(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                middleVerify);
        }

        if (gap <= -reach)
        {
            return TraverseSolid(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                middleVerify);
        }

        if (gap < 0f)
        {
            if (TraverseSolid(
                    tree,
                    joint.PositiveDescendantOrdinal,
                    orb,
                    middleVerify: false))

                return true;

            return TraverseSolid(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                middleVerify);
        }

        if (TraverseSolid(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                middleVerify))

            return true;

        return TraverseSolid(
            tree,
            joint.NegativeDescendantOrdinal,
            orb,
            middleVerify: false);
    }

    private static void TraverseSolidBranch(PackedKineticBsp tree, int polygOrdinal)
    {
        var polyg =
                            tree.PolygChart.Polygons[polygOrdinal];
        KineticTelemetry.PreviousStanceFailPolyIdent = polyg.Id;
        KineticTelemetry.PreviousStanceFailPolyNorm =
                                polyg.Plane.Normal;
        KineticTelemetry.PreviousStanceFailPolyD = polyg.Plane.D;
    }

    private static bool TraverseSolidPolyg(
        PackedKineticBsp tree,
        int jointOrdinal,
        Ball orb,
        float radius,
        ref bool middleSolid,
        ref int strikePolygOrdinal,
        bool middleVerify)
    {
        if (jointOrdinal < 0)
            return middleSolid;

        var joint = tree.Joints[jointOrdinal];
        if (joint.Type == BspTag.Leaf)
        {
            if (joint.PolygonIndexRange.Count is 0)
                return false;

            if (middleVerify && joint.Solid is not 0)
                middleSolid = true;

            if (!Overlaps(joint, orb))
                return middleSolid;

            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                if (Touches(tree, polygOrdinal, orb))
                {
                    strikePolygOrdinal = polygOrdinal;
                    return true;
                }
            }

            return middleSolid;
        }

        if (!Overlaps(joint, orb))
            return middleSolid;

        float gap =
            Vector3.Dot(joint.SplittingPlane.Normal, orb.Center) +
            joint.SplittingPlane.D;
        float reach = radius - Eps;

        if (gap >= reach)
        {
            return TraverseSolidPolyg(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                radius,
                ref middleSolid,
                ref strikePolygOrdinal,
                middleVerify);
        }

        if (gap <= -reach)
        {
            return TraverseSolidPolyg(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                radius,
                ref middleSolid,
                ref strikePolygOrdinal,
                middleVerify);
        }

        if (gap <= 0f)
        {
            TraverseSolidPolyg(
                tree,
                joint.NegativeDescendantOrdinal,
                orb,
                radius,
                ref middleSolid,
                ref strikePolygOrdinal,
                middleVerify);
            if (strikePolygOrdinal >= 0)
                return middleSolid;

            return TraverseSolidPolyg(
                tree,
                joint.PositiveDescendantOrdinal,
                orb,
                radius,
                ref middleSolid,
                ref strikePolygOrdinal,
                middleVerify: false);
        }

        TraverseSolidPolyg(
            tree,
            joint.PositiveDescendantOrdinal,
            orb,
            radius,
            ref middleSolid,
            ref strikePolygOrdinal,
            middleVerify);
        if (strikePolygOrdinal >= 0)
            return middleSolid;

        return TraverseSolidPolyg(
            tree,
            joint.NegativeDescendantOrdinal,
            orb,
            radius,
            ref middleSolid,
            ref strikePolygOrdinal,
            middleVerify: false);
    }

    private static bool TraverseStaticStrike(
        PackedKineticBsp tree,
        int jointOrdinal,
        Vector3 middle,
        float radius,
        ref ushort strikePolygIdent,
        ref Vector3 strikeNorm)
    {
        if (jointOrdinal < 0)
            return false;

        var joint = tree.Joints[jointOrdinal];
        Vector3 difference = middle - joint.BoundingSphere.Origin;
        float combinedRadius = radius + joint.BoundingSphere.Radius;
        if (difference.LengthSquared() >= combinedRadius * combinedRadius)
            return false;

        if (joint.Type == BspTag.Leaf)
        {
            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                var polyg =
                    tree.PolygChart.Polygons[polygOrdinal];
                Vector3 linkPt = Vector3.Zero;
                if (CellBspProbe.PolygStrikesOrbPrecise(
                        polyg.Plane,
                        CornersOf(tree, polyg),
                        middle,
                        radius,
                        ref linkPt))
                {
                    strikePolygIdent = polyg.Id;
                    strikeNorm = polyg.Plane.Normal;
                    return true;
                }
            }

            return false;
        }

        float divideGap =
            Vector3.Dot(joint.SplittingPlane.Normal, middle) + joint.SplittingPlane.D;
        float reach = radius - Eps;

        if (divideGap >= reach)
        {
            return TraverseStaticStrike(
                tree,
                joint.PositiveDescendantOrdinal,
                middle,
                radius,
                ref strikePolygIdent,
                ref strikeNorm);
        }

        if (divideGap <= -reach)
        {
            return TraverseStaticStrike(
                tree,
                joint.NegativeDescendantOrdinal,
                middle,
                radius,
                ref strikePolygIdent,
                ref strikeNorm);
        }

        if (TraverseStaticStrike(
                tree,
                joint.PositiveDescendantOrdinal,
                middle,
                radius,
                ref strikePolygIdent,
                ref strikeNorm))

            return true;

        return TraverseStaticStrike(
            tree,
            joint.NegativeDescendantOrdinal,
            middle,
            radius,
            ref strikePolygIdent,
            ref strikeNorm);
    }

    private static void TraverseSweptStrike(
        PackedKineticBsp tree,
        int jointOrdinal,
        Vector3 middle,
        float radius,
        Vector3 travel,
        ref ushort strikePolygIdent,
        ref Vector3 strikeNorm,
        ref float finestMoment)
    {
        if (jointOrdinal < 0)
            return;

        var joint = tree.Joints[jointOrdinal];
        Vector3 difference = middle - joint.BoundingSphere.Origin;
        float combinedRadius =
            radius + joint.BoundingSphere.Radius + travel.Length() + 0.1f;
        if (difference.LengthSquared() >= combinedRadius * combinedRadius)
            return;

        if (joint.Type == BspTag.Leaf)
        {
            int finish = joint.PolygonIndexRange.FinishExclusive;
            for (int idx = joint.PolygonIndexRange.Start; idx < finish; ++idx)
            {
                int polygOrdinal = tree.PolygOrdinalFlow[idx];
                var polyg =
                    tree.PolygChart.Polygons[polygOrdinal];
                if (Vector3.Dot(travel, polyg.Plane.Normal) >= 0f)
                    continue;

                Vector3 linkPt = Vector3.Zero;
                if (CellBspProbe.PolygStrikesOrbPrecise(
                        polyg.Plane,
                        CornersOf(tree, polyg),
                        middle,
                        radius,
                        ref linkPt))
                {
                    if (0f < finestMoment)
                    {
                        finestMoment = 0f;
                        strikePolygIdent = polyg.Id;
                        strikeNorm = polyg.Plane.Normal;
                    }

                    continue;
                }

                Vector3 finishMiddle = middle + travel;
                if (CellBspProbe.PolygStrikesOrbPrecise(
                        polyg.Plane,
                        CornersOf(tree, polyg),
                        finishMiddle,
                        radius,
                        ref linkPt) &&
                    1f < finestMoment)
                {
                    finestMoment = 1f;
                    strikePolygIdent = polyg.Id;
                    strikeNorm = polyg.Plane.Normal;
                }
            }

            return;
        }

        float divideGap =
            Vector3.Dot(joint.SplittingPlane.Normal, middle) + joint.SplittingPlane.D;
        float reach = radius + travel.Length();

        if (divideGap >= reach)
        {
            TraverseSweptStrike(
                tree,
                joint.PositiveDescendantOrdinal,
                middle,
                radius,
                travel,
                ref strikePolygIdent,
                ref strikeNorm,
                ref finestMoment);
            return;
        }

        if (divideGap <= -reach)
        {
            TraverseSweptStrike(
                tree,
                joint.NegativeDescendantOrdinal,
                middle,
                radius,
                travel,
                ref strikePolygIdent,
                ref strikeNorm,
                ref finestMoment);
            return;
        }

        TraverseSweptStrike(
            tree,
            joint.PositiveDescendantOrdinal,
            middle,
            radius,
            travel,
            ref strikePolygIdent,
            ref strikeNorm,
            ref finestMoment);
        TraverseSweptStrike(
            tree,
            joint.NegativeDescendantOrdinal,
            middle,
            radius,
            travel,
            ref strikePolygIdent,
            ref strikeNorm,
            ref finestMoment);
    }
}
