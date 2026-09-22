using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics;

internal static partial class PackedBspQuery
{
    private const float Eps = KineticConstants.EPSILON;

    // A sphere in the tree's local frame
    private struct Ball(Vector3 middle, float radius)
    {
        public Vector3 Center = middle;
        public float Radius = radius;
    }

    private static ReadOnlySpan<Vector3> CornersOf(
        PackedKineticBsp tree,
        in PackedContactPolygon polyg)
    {
        return tree.PolygChart.Vertices.AsSpan(
                polyg.VertexRange.Start,
                polyg.VertexRange.Count);
    }

    private static bool Overlaps(
        in PackedKineticBspNode joint,
        Ball orb)
    {
        Vector3 d = orb.Center - joint.BoundingSphere.Origin;
        float radius = orb.Radius + joint.BoundingSphere.Radius;
        return d.LengthSquared() < radius * radius;
    }

    private static bool SmackFacing(
        PackedKineticBsp tree,
        int polygOrdinal,
        Ball orb,
        Vector3 travel,
        ref Vector3 linkPt,
        ref int strikePolygOrdinal)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        bool strike = CellBspProbe.PolygStrikesOrbPrecise(
            polyg.Plane,
            CornersOf(tree, polyg),
            orb.Center,
            orb.Radius,
            ref linkPt);

        if (strike)
            strikePolygOrdinal = polygOrdinal;

        float relocateDot = Vector3.Dot(travel, polyg.Plane.Normal);
        return relocateDot >= 0f ? false : strike;
    }

    private static bool Touches(
        PackedKineticBsp tree,
        int polygOrdinal,
        Ball orb)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        Vector3 linkPt = Vector3.Zero;
        return CellBspProbe.PolygStrikesOrbPrecise(
            polyg.Plane,
            CornersOf(tree, polyg),
            orb.Center,
            orb.Radius,
            ref linkPt);
    }

    private static bool TouchesPassable(
        PackedKineticBsp tree,
        int polygOrdinal,
        SweepPath trail,
        Ball orb,
        Vector3 up)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        float dp = Vector3.Dot(up, polyg.Plane.Normal);
        if (dp <= trail.WalkableAllowance)
            return false;

        Vector3 linkPt = Vector3.Zero;
        return CellBspProbe.PolygStrikesOrbPrecise(
            polyg.Plane,
            CornersOf(tree, polyg),
            orb.Center,
            orb.Radius,
            ref linkPt);
    }

    private static bool Supports(
        PackedKineticBsp tree,
        int polygOrdinal,
        Ball orb,
        Vector3 up,
        bool small)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        var verts = CornersOf(tree, polyg);

        float angleUp = Vector3.Dot(polyg.Plane.Normal, up);
        if (angleUp < Eps)
            return false;

        float angle =
            (Vector3.Dot(polyg.Plane.Normal, orb.Center) + polyg.Plane.D) /
            angleUp;
        Vector3 middle = orb.Center - up * angle;

        float radiusSquared = orb.Radius * orb.Radius;
        if (small)
            radiusSquared *= 0.25f;

        int earlierOrdinal = verts.Length - 1;
        for (int idx = 0; idx < verts.Length; ++idx)
        {
            Vector3 vert = verts[idx];
            Vector3 earlierVert = verts[earlierOrdinal];
            earlierOrdinal = idx;

            Vector3 rim = vert - earlierVert;
            Vector3 displacement = middle - earlierVert;
            Vector3 cross = Vector3.Cross(polyg.Plane.Normal, rim);
            float difference = Vector3.Dot(displacement, cross);

            if (difference < 0f)
            {
                if (cross.LengthSquared() * radiusSquared < difference * difference)
                    return false;

                float displacementAlongRim = Vector3.Dot(displacement, rim);
                if (displacementAlongRim >= 0f &&
                    displacementAlongRim <= rim.LengthSquared())

                    return true;

                return false;
            }

            if (displacement.LengthSquared() <= radiusSquared)
                return true;
        }

        return true;
    }

    private static bool PushBack(
        PackedKineticBsp tree,
        int polygOrdinal,
        SweepPath trail,
        ref Ball validLocus,
        Vector3 travel)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];

        Vector3 feedMiddle = validLocus.Center;
        float strollInterpolationPrior = trail.WalkInterp;
        float locusDot =
            Vector3.Dot(validLocus.Center, polyg.Plane.Normal) + polyg.Plane.D;
        float travelDot = Vector3.Dot(travel, polyg.Plane.Normal);
        float gap;

        if (travelDot <= Eps)
        {
            if (travelDot >= -Eps)
            {
                InspectPushBack(
                    tree,
                    polygOrdinal,
                    feedMiddle,
                    validLocus.Center,
                    validLocus.Radius,
                    strollInterpolationPrior,
                    trail.WalkInterp,
                    locusDot,
                    travelDot,
                    0f,
                    imposed: false);
                return false;
            }

            gap = locusDot - validLocus.Radius;
        }
        else
        {
            gap = -validLocus.Radius - locusDot;
        }

        float invGap = gap / travelDot;
        float interpolation = (1f - invGap) * trail.WalkInterp;

        if (interpolation >= trail.WalkInterp || interpolation < -0.5f)
        {
            InspectPushBack(
                tree,
                polygOrdinal,
                feedMiddle,
                validLocus.Center,
                validLocus.Radius,
                strollInterpolationPrior,
                trail.WalkInterp,
                locusDot,
                travelDot,
                invGap,
                imposed: false);
            return false;
        }

        validLocus.Center -= travel * invGap;
        trail.WalkInterp = interpolation;

        InspectPushBack(
            tree,
            polygOrdinal,
            feedMiddle,
            validLocus.Center,
            validLocus.Radius,
            strollInterpolationPrior,
            trail.WalkInterp,
            locusDot,
            travelDot,
            invGap,
            imposed: true);

        if (KineticTelemetry.ProbePolyDumpEnabled)
        {
            KineticTelemetry.TracePolyPrint(
                trail.CheckCellId,
                Thaw(tree, polygOrdinal));
        }

        return true;
    }

    private static void InspectPushBack(
        PackedKineticBsp tree,
        int polygOrdinal,
        Vector3 feedMiddle,
        Vector3 productMiddle,
        float radius,
        float strollInterpolationPrior,
        float strollInterpolationFollowing,
        float locusDot,
        float travelDot,
        float invGap,
        bool imposed)
    {
        if (!KineticTelemetry.ProbePushBackEnabled)
            return;

        KineticTelemetry.TracePushBackAdjust(
            feedMiddle,
            productMiddle,
            tree.PolygChart.Polygons[polygOrdinal].Plane,
            radius,
            strollInterpolationPrior,
            strollInterpolationFollowing,
            locusDot,
            travelDot,
            invGap,
            imposed);
    }

    private static bool SeekCrossedRim(
        PackedKineticBsp tree,
        int polygOrdinal,
        Ball orb,
        Vector3 up,
        ref Vector3 norm)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        if (!CellBspProbe.SeekCrossedRim(
                polyg.Plane,
                CornersOf(tree, polyg),
                orb.Center,
                up,
                out Vector3 crossedNorm))

            return false;

        norm = crossedNorm;
        return true;
    }

    private static Vector3 RealmNorm(Vector3 norm, Quaternion ownToRealm)
    {
        Vector3 realmNorm = Vector3.Transform(norm, ownToRealm);
        return realmNorm.LengthSquared() > KineticConstants.EpsilonSq
            ? Vector3.Normalize(realmNorm)
            : Vector3.UnitZ;
    }

    private static Plane RealmPlane(
        Vector3 realmNorm,
        ReadOnlySpan<Vector3> ownVerts,
        Quaternion ownToRealm,
        float scaling,
        Vector3 realmOrigin)
    {
        float d = ownVerts.Length > 0
            ? -Vector3.Dot(
                realmNorm,
                Vector3.Transform(ownVerts[0] * scaling, ownToRealm) + realmOrigin)
            : 0f;
        return new Plane(realmNorm, d);
    }

    private static void NudgeOffPolyg(
        PackedKineticBsp tree,
        int polygOrdinal,
        ref Ball validLocus,
        ref Ball validPosition2,
        bool hasValidPosition2,
        float radius,
        bool middleSolid,
        bool wipeChamber)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        Vector3 relocateDir = Vector3.Zero;

        if (middleSolid)
        {
            relocateDir = polyg.Plane.Normal;
        }
        else
        {
            Vector3 up = Vector3.UnitZ;
            if (!SeekCrossedRim(
                    tree,
                    polygOrdinal,
                    validLocus,
                    up,
                    ref relocateDir))

                relocateDir = polyg.Plane.Normal;
        }

        float gap =
            Vector3.Dot(validLocus.Center, polyg.Plane.Normal) + polyg.Plane.D;
        float pushQuantity = radius - gap;
        if (pushQuantity <= 0f)
            pushQuantity = Eps;

        Vector3 shift = relocateDir * pushQuantity;
        validLocus.Center += shift;
        if (hasValidPosition2)
            validPosition2.Center += shift;
    }

    private static float MomentToPlane(
        PackedKineticBsp tree,
        int polygOrdinal,
        Ball verifyLocus,
        Vector3 latestLocus,
        Vector3 travel)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        float locusDot =
            Vector3.Dot(latestLocus, polyg.Plane.Normal) + polyg.Plane.D;
        if (MathF.Abs(locusDot) < verifyLocus.Radius)
            return 1f;

        float travelDot = Vector3.Dot(travel, polyg.Plane.Normal);
        if (MathF.Abs(travelDot) <= Eps)
            return 0f;

        float radius = locusDot < 0f
            ? -verifyLocus.Radius
            : verifyLocus.Radius;
        return (radius - locusDot) / travelDot;
    }

    private static void NoteStrikeForSensors(
        PackedKineticBsp tree,
        int polygOrdinal)
    {
        if (KineticTelemetry.ProbeBuildingEnabled ||
            KineticTelemetry.ProbeIndoorBspEnabled)
        {
            KineticTelemetry.PreviousBspStrikePoly =
                Thaw(tree, polygOrdinal);
        }
    }

    private static SettledPolygon Thaw(
        PackedKineticBsp tree,
        int polygOrdinal)
    {
        var polyg = tree.PolygChart.Polygons[polygOrdinal];
        return new SettledPolygon
        {
            Id = polyg.Id,
            Plane = polyg.Plane,
            SidesType = polyg.SidesType,
            NumPoints = polyg.NumPoints,
            Vertices = CornersOf(tree, polyg).ToArray(),
        };
    }
}
