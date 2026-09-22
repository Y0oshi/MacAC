using System.Numerics;
using MacAC.Dat;
using Plane = System.Numerics.Plane;

namespace MacAC.Mechanics.Kinetics;

public static partial class CellBspProbe
{
    private const float Eps = KineticConstants.EPSILON;

    // A sphere in the tree's local frame
    internal struct Ball(Vector3 middle, float radius)
    {
        public Vector3 Center = middle;
        public float Radius = radius;
    }

    internal static bool PolygStrikesOrbPrecise(
        in Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbMiddle,
        float orbRadius,
        ref Vector3 linkPt)
    {
        int num = verts.Length;
        if (num is 0) return true;

        float distance = Vector3.Dot(polyPlane.Normal, orbMiddle) + polyPlane.D;
        float rad = orbRadius - Eps;

        if (MathF.Abs(distance) > rad) return false;

        float diff = rad * rad - distance * distance;
        linkPt = orbMiddle - polyPlane.Normal * distance;

        int earlierIndex = num - 1;
        for (int idx = 0; idx < num; ++idx)
        {
            var v = verts[idx];
            var lv = verts[earlierIndex];
            earlierIndex = idx;

            var rim = v - lv;
            var disp = linkPt - lv;
            var cross = Vector3.Cross(polyPlane.Normal, rim);

            if (Vector3.Dot(disp, cross) >= 0f) continue;

            earlierIndex = num - 1;
            for (int jdx = 0; jdx < num; ++jdx)
            {
                v = verts[jdx];
                lv = verts[earlierIndex];
                earlierIndex = jdx;

                rim = v - lv;
                disp = linkPt - lv;
                cross = Vector3.Cross(polyPlane.Normal, rim);
                float dispDot = Vector3.Dot(disp, cross);

                if (dispDot < 0f)
                {
                    if (cross.LengthSquared() * diff < dispDot * dispDot)
                        return false;

                    float dispRim = Vector3.Dot(disp, rim);
                    if (dispRim >= 0f && dispRim <= rim.LengthSquared())
                        return true;
                }

                if (disp.LengthSquared() <= diff)
                    return true;
            }
            return false;
        }
        return true;
    }

    internal static bool VerifyPassableSupport(
        Plane plane,
        ReadOnlySpan<Vector3> verts,
        Vector3 middle,
        float supportRadius,
        Vector3 up)
    {
        float angleUp = Vector3.Dot(plane.Normal, up);
        if (angleUp < Eps) return false;

        float angle = (Vector3.Dot(plane.Normal, middle) + plane.D) / angleUp;
        middle -= up * angle;

        float radsum = supportRadius * supportRadius;

        int num = verts.Length;
        int earlierIndex = num - 1;

        for (int idx = 0; idx < num; ++idx)
        {
            Vector3 v = verts[idx];
            Vector3 lv = verts[earlierIndex];
            earlierIndex = idx;

            Vector3 rim = v - lv;
            Vector3 disp = middle - lv;
            Vector3 cross = Vector3.Cross(plane.Normal, rim);
            float diff = Vector3.Dot(disp, cross);

            if (diff < 0f)
            {
                if (cross.LengthSquared() * radsum < diff * diff)
                    return false;

                float dispRim = Vector3.Dot(disp, rim);
                return dispRim >= 0f && dispRim <= rim.LengthSquared() ? true : false;
            }

            if (disp.LengthSquared() <= radsum)
                return true;
        }
        return true;
    }

    internal static bool SeekCrossedRim(
        Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbMiddle,
        Vector3 up,
        out Vector3 norm)
    {
        norm = Vector3.Zero;

        float angleUp = Vector3.Dot(polyPlane.Normal, up);
        if (MathF.Abs(angleUp) < Eps) return false;

        float angle = (Vector3.Dot(polyPlane.Normal, orbMiddle) + polyPlane.D) / angleUp;
        Vector3 middle = orbMiddle - up * angle;

        int num = verts.Length;
        int earlierIndex = num - 1;

        for (int idx = 0; idx < num; ++idx)
        {
            Vector3 v = verts[idx];
            Vector3 lv = verts[earlierIndex];
            earlierIndex = idx;

            Vector3 rim = v - lv;
            Vector3 disp = middle - lv;
            Vector3 cross = Vector3.Cross(polyPlane.Normal, rim);

            if (Vector3.Dot(disp, cross) < 0f)
            {
                float crossLength = cross.Length();
                norm = crossLength > 0f ? cross * (1f / crossLength) : Vector3.Zero;
                return true;
            }
        }
        return false;
    }

    // Broad phase: does the sphere reach the node's bounding sphere?
    private static bool Overlaps(PhysicsBspNode joint, Ball orb)
    {
        Orb bs = joint.Bounds;
        Vector3 d = orb.Center - bs.Center;
        float r = orb.Radius + bs.Radius;
        return d.LengthSquared() < r * r;
    }

    private static bool SmackFacing(
        SettledPolygon poly,
        Ball orb,
        Vector3 travel,
        ref Vector3 linkPt,
        ref SettledPolygon? strikePoly)
    {
        bool strike = PolygStrikesOrbPrecise(
            poly.Plane, poly.Vertices,
            orb.Center, orb.Radius,
            ref linkPt);

        if (strike) strikePoly = poly;

        float relocateDot = Vector3.Dot(travel, poly.Plane.Normal);
        return relocateDot >= 0f ? false : strike;
    }

    private static bool Touches(SettledPolygon poly, Ball orb)
    {
        Vector3 cp = Vector3.Zero;
        return PolygStrikesOrbPrecise(
            poly.Plane, poly.Vertices,
            orb.Center, orb.Radius,
            ref cp);
    }

    private static bool TouchesPassable(
        SettledPolygon poly,
        SweepPath trail,
        Ball orb,
        Vector3 up)
    {
        float dp = Vector3.Dot(up, poly.Plane.Normal);
        if (dp <= trail.WalkableAllowance) return false;

        Vector3 cp = Vector3.Zero;
        return PolygStrikesOrbPrecise(
            poly.Plane, poly.Vertices,
            orb.Center, orb.Radius,
            ref cp);
    }

    private static bool Supports(
        SettledPolygon poly,
        Ball orb,
        Vector3 up,
        bool small)
    {
        return VerifyPassableSupport(
                poly.Plane,
                poly.Vertices,
                orb.Center,
                small ? orb.Radius * 0.5f : orb.Radius,
                up);
    }

    private static bool PushBack(
        SettledPolygon poly,
        SweepPath trail,
        ref Ball validSpot,
        Vector3 travel)
    {
        Vector3 feedMiddle = validSpot.Center;
        float strollLerpPrior = trail.WalkInterp;

        float dpSpot = Vector3.Dot(validSpot.Center, poly.Plane.Normal) + poly.Plane.D;
        float dpRelocate = Vector3.Dot(travel, poly.Plane.Normal);
        float distance;

        if (dpRelocate <= Eps)
        {
            if (dpRelocate >= -Eps)
            {
                if (KineticTelemetry.ProbePushBackEnabled)
                {
                    KineticTelemetry.TracePushBackAdjust(
                        feedMiddle, validSpot.Center, poly.Plane, validSpot.Radius,
                        strollLerpPrior, trail.WalkInterp,
                        dpSpot, dpRelocate, 0f,
                        imposed: false);
                }
                return false;
            }
            distance = dpSpot - validSpot.Radius;
        }
        else
        {
            distance = -validSpot.Radius - dpSpot;
        }

        float idxDistance = distance / dpRelocate;
        float lerp = (1f - idxDistance) * trail.WalkInterp;

        if (lerp >= trail.WalkInterp || lerp < -0.5f)
        {
            if (KineticTelemetry.ProbePushBackEnabled)
            {
                KineticTelemetry.TracePushBackAdjust(
                    feedMiddle, validSpot.Center, poly.Plane, validSpot.Radius,
                    strollLerpPrior, trail.WalkInterp,
                    dpSpot, dpRelocate, idxDistance,
                    imposed: false);
            }
            return false;
        }

        validSpot.Center -= travel * idxDistance;
        trail.WalkInterp = lerp;

        if (KineticTelemetry.ProbePushBackEnabled)
        {
            KineticTelemetry.TracePushBackAdjust(
                feedMiddle, validSpot.Center, poly.Plane, validSpot.Radius,
                strollLerpPrior, trail.WalkInterp,
                dpSpot, dpRelocate, idxDistance,
                imposed: true);
        }

        if (KineticTelemetry.ProbePolyDumpEnabled)

            KineticTelemetry.TracePolyPrint(trail.CheckCellId, poly);

        return true;
    }

    private static bool SeekCrossedRim(
        SettledPolygon poly,
        Ball orb,
        Vector3 up,
        ref Vector3 norm)
    {
        if (!SeekCrossedRim(poly.Plane, poly.Vertices, orb.Center, up, out var crossedNorm))
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
        SettledPolygon poly,
        ref Ball validSpot,
        ref Ball validPos2,
        bool hasValidPos2,
        float radius,
        bool middleSolid,
        bool wipeChamber)
    {
        Vector3 relocateDirection = Vector3.Zero;

        if (middleSolid)
        {
            relocateDirection = poly.Plane.Normal;
        }
        else
        {
            Vector3 up = Vector3.UnitZ;
            if (!SeekCrossedRim(poly, validSpot, up, ref relocateDirection))
                relocateDirection = poly.Plane.Normal;
        }

        float distance = Vector3.Dot(validSpot.Center, poly.Plane.Normal) + poly.Plane.D;
        float pushAmt = radius - distance;
        if (pushAmt <= 0f) pushAmt = Eps;

        Vector3 shift = relocateDirection * pushAmt;
        validSpot.Center += shift;

        if (hasValidPos2)
            validPos2.Center += shift;
    }

    private static float MomentToPlane(
        SettledPolygon poly,
        Ball verifySpot,
        Vector3 curSpot,
        Vector3 travel)
    {
        float dpSpot = Vector3.Dot(curSpot, poly.Plane.Normal) + poly.Plane.D;
        if (MathF.Abs(dpSpot) < verifySpot.Radius) return 1f;

        float dpRelocate = Vector3.Dot(travel, poly.Plane.Normal);
        if (MathF.Abs(dpRelocate) <= Eps) return 0f;

        float r = dpSpot < 0f ? -verifySpot.Radius : verifySpot.Radius;
        return (r - dpSpot) / dpRelocate;
    }
}
