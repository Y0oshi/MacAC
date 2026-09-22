using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static class ContactPrimitives
{
    public const float Epsilon = 1e-4f;

    public const float EpsilonSq = 1e-8f;

    public static bool OrbIntersectsRay(
        Vector3 orbMiddle, float orbRadius,
        Vector3 rayOrigin, Vector3 rayDirection,
        out double t)
    {
        t = 0.0;

        float dx = rayOrigin.X - orbMiddle.X;
        float dy = rayOrigin.Y - orbMiddle.Y;
        float dz = rayOrigin.Z - orbMiddle.Z;

        float c = dx * dx + dy * dy + dz * dz - orbRadius * orbRadius;
        if (c <= 0f)
            return false;

        float a = rayDirection.X * rayDirection.X + rayDirection.Y * rayDirection.Y + rayDirection.Z * rayDirection.Z;
        if (a < EpsilonSq)
            return false;   // degenerate ray

        float b = -(dx * rayDirection.X + dy * rayDirection.Y + dz * rayDirection.Z);
        float disc = b * b - c * a;
        if (disc < 0f)
            return false;   // no real intersection

        float trunk = MathF.Sqrt(disc);
        t = (b < trunk ? b + trunk : b - trunk) / a;
        return true;
    }

    public static bool RayPlaneIntersect(Plane plane, Vector3 rayOrigin, Vector3 rayDirection, out double t)
    {
        t = 0.0;

        float denom = Vector3.Dot(rayDirection, plane.Normal);
        if (MathF.Abs(denom) < Epsilon)
            return false;   // ray is (nearly) parallel to plane

        float along = -(Vector3.Dot(rayOrigin, plane.Normal) + plane.D) / denom;
        t = along;
        return along >= 0f;
    }

    public static void CalcNorm(ReadOnlySpan<Vector3> verts, out Vector3 norm, out float planeD)
    {
        norm = Vector3.Zero;
        planeD = 0f;

        int num = verts.Length;
        if (num < 3)
            return;

        float accX = 0f, accY = 0f, accZ = 0f;
        Vector3 v0 = verts[0];
        for (int idx = 1; idx < num - 1; ++idx)
        {
            Vector3 vi = verts[idx];
            Vector3 vj = verts[idx + 1];
            float ax = vi.X - v0.X, ay = vi.Y - v0.Y, az = vi.Z - v0.Z;
            float bx = vj.X - v0.X, by = vj.Y - v0.Y, bz = vj.Z - v0.Z;

            accX += ay * bz - az * by;
            accY += az * bx - ax * bz;
            accZ += ax * by - ay * bx;
        }

        float length = MathF.Sqrt(accX * accX + accY * accY + accZ * accZ);
        if (length < EpsilonSq)
            return;

        float invLength = 1f / length;
        norm = new Vector3(accX * invLength, accY * invLength, accZ * invLength);

        float dotTotal = 0f;
        for (int idx = 0; idx < num; ++idx)
            dotTotal += Vector3.Dot(norm, verts[idx]);
        planeD = -(dotTotal / num);
    }

    public static bool OrbIntersectsPoly(
        Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbMiddle, float orbRadius,
        out Vector3 linkPt)
    {
        linkPt = Vector3.Zero;

        float distance = Height(polyPlane, orbMiddle);
        float rad = orbRadius - Epsilon;
        if (MathF.Abs(distance) > rad)
            return false;

        linkPt = orbMiddle - polyPlane.Normal * distance;
        float radSq = rad * rad - distance * distance;   // available slack² for edge tests

        int countVerts = verts.Length;
        if (countVerts is 0)
            return true;

        bool inside = true;
        int earlier = countVerts - 1;
        for (int idx = 0; idx < countVerts; ++idx)
        {
            Vector3 v0 = verts[earlier];
            Vector3 v1 = verts[idx];
            earlier = idx;

            Vector3 rim = v1 - v0;
            Vector3 disp = linkPt - v0;
            Vector3 perp = RimPerp(rim, polyPlane.Normal);

            float dp = Vector3.Dot(disp, perp);
            if (dp < 0f)
            {
                if (perp.LengthSquared() * radSq < dp * dp)
                    return false;   // too far outside to be reached by sphere radius

                float along = Vector3.Dot(disp, rim);
                if (along >= 0f && along < rim.LengthSquared())
                    return true;

                inside = false;
            }

            if (disp.LengthSquared() <= radSq)
                return true;
        }

        return inside;
    }

    public static bool FindTimeOfCollision(
        Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbOrigin, float orbRadius,
        Vector3 rayDirection,
        out float t)
    {
        t = 0f;

        float denom = Vector3.Dot(rayDirection, polyPlane.Normal);
        if (MathF.Abs(denom) < Epsilon)
            return false;

        int countVerts = verts.Length;
        t = Height(polyPlane, orbOrigin) / denom;
        Vector3 link = orbOrigin - rayDirection * t;
        float radSq = orbRadius * orbRadius;

        bool inside = true;
        int earlier = countVerts - 1;
        for (int idx = 0; idx < countVerts; ++idx)
        {
            Vector3 v0 = verts[earlier];
            Vector3 v1 = verts[idx];
            earlier = idx;

            Vector3 rim = v1 - v0;
            Vector3 disp = link - v0;
            Vector3 perp = RimPerp(rim, polyPlane.Normal);

            float dp = Vector3.Dot(disp, perp);
            if (dp < 0f)
            {
                if (perp.LengthSquared() * radSq < dp * dp)
                    return false;

                float along = Vector3.Dot(disp, rim);
                if (along >= 0f && along < rim.LengthSquared())
                    return true;

                inside = false;
            }

            if (disp.LengthSquared() < radSq)
                return true;
        }

        return inside;
    }

    public static bool StrikesPassable(
        Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbMiddle, float orbRadius,
        Vector3 travelDirection)
    {
        if (Vector3.Dot(polyPlane.Normal, travelDirection) < 0f)
            return false;
        return OrbIntersectsPoly(polyPlane, verts, orbMiddle, orbRadius, out _);
    }

    public static bool SeekPassableImpact(
        Plane polyPlane,
        ReadOnlySpan<Vector3> verts,
        Vector3 orbOrigin,
        Vector3 travelDirection,
        out Vector3 rimNorm)
    {
        rimNorm = Vector3.Zero;

        float denom = Vector3.Dot(polyPlane.Normal, travelDirection);
        if (MathF.Abs(denom) < Epsilon)
            return false;

        int countVerts = verts.Length;
        float t = Height(polyPlane, orbOrigin) / denom;
        Vector3 link = orbOrigin - travelDirection * t;

        int earlier = countVerts - 1;
        for (int idx = 0; idx < countVerts; ++idx)
        {
            Vector3 v0 = verts[earlier];
            Vector3 v1 = verts[idx];
            earlier = idx;

            Vector3 rim = v1 - v0;
            Vector3 disp = link - v0;
            Vector3 perp = RimPerp(rim, polyPlane.Normal);

            // Retail sums this dot by hand; keep it scalar
            float dp = disp.X * perp.X + disp.Y * perp.Y + disp.Z * perp.Z;
            if (dp >= 0f)
                continue;

            float length = perp.Length();
            if (length < EpsilonSq)
                return false;

            rimNorm = perp / length;
            return true;
        }

        return false;
    }

    public static float ShiftOrb(Plane plane, float orbRadius, Vector3 orbMiddle, Vector3 travelDirection)
    {
        float distance = Vector3.Dot(orbMiddle, plane.Normal) + plane.D;
        if (MathF.Abs(distance) < orbRadius)
            return float.MaxValue;  // already touching - no slide needed

        float denom = Vector3.Dot(travelDirection, plane.Normal);
        if (MathF.Abs(denom) < Epsilon)
            return 0f;              // movement is parallel to plane

        float shift = distance <= 0f ? -orbRadius : orbRadius;
        return (shift - distance) / denom;
    }

    public static bool SweptSphereHitsSphere(
        Vector3 carrierMiddle, float carrierRadius,
        Vector3 sweepDiff,
        Vector3 markMiddle, float markRadius,
        out float t)
    {
        t = 0f;

        float radTotal = carrierRadius + markRadius;

        float mx = sweepDiff.X, my = sweepDiff.Y, mz = sweepDiff.Z;
        float distanceSq = mx * mx + my * my + mz * mz;
        if (distanceSq < EpsilonSq)
            return false;   // degenerate sweep (stationary mover)

        float sx = markMiddle.X - carrierMiddle.X;
        float sy = markMiddle.Y - carrierMiddle.Y;
        float sz = markMiddle.Z - carrierMiddle.Z;

        float gap = sx * sx + sy * sy + sz * sz - radTotal * radTotal;
        if (gap < EpsilonSq)
            return false;   // already overlapping - use static test separately

        // Positive when the sphere is in FRONT of us (moving toward it)
        float similar = -(sx * mx + sy * my + sz * mz);

        float disc = similar * similar - gap * distanceSq;
        if (disc < 0f)
            return false;

        float cDistance = MathF.Sqrt(disc);
        float trunk = (similar - cDistance < 0f) ? -(cDistance + similar) : -(similar - cDistance);

        t = trunk / distanceSq;
        return t > 0f && t <= 1f;
    }

    public static bool LandOnOrb(
        Plane plane, float orbRadius,
        ref Vector3 orbMiddle, ref Vector3 travelDirection,
        ref float strollLerp)
    {
        float distanceToPlane = Vector3.Dot(orbMiddle, plane.Normal) + plane.D;
        float denom = Vector3.Dot(travelDirection, plane.Normal);

        float tLand;
        if (denom > Epsilon)
            tLand = (-orbRadius - distanceToPlane) / denom;   // moving away from the surface
        else if (denom >= -Epsilon)
            return false;                                     // parallel to plane
        else
            tLand = (distanceToPlane - orbRadius) / denom;    // moving toward the surface

        float newLerp = (1f - tLand) * strollLerp;
        if (newLerp >= strollLerp || newLerp < -0.5f)
            return false;

        orbMiddle -= travelDirection * tLand;
        strollLerp = newLerp;
        return true;
    }

    // Signed distance of a point from a plane (normal·p + d)
    private static float Height(in Plane plane, Vector3 p) => Vector3.Dot(plane.Normal, p) + plane.D;

    // The in-plane outward perpendicular of a polygon edge (edge × normal, retail component order)
    private static Vector3 RimPerp(Vector3 rim, Vector3 norm)
    {
        return new(
        rim.Z * norm.Y - rim.Y * norm.Z,
        rim.X * norm.Z - rim.Z * norm.X,
        rim.Y * norm.X - rim.X * norm.Y);
    }
}
