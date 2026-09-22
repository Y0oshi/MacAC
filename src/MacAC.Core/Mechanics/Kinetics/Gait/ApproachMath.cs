using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>Heading and distance arithmetic shared by the move-to, sticky and turn-to logic.</summary>
public static class ApproachMath
{
    public const float Epsilon = 0.000199999995f;

    private const float DegPerRad = 180f / MathF.PI;
    private const float RadPerDeg = MathF.PI / 180f;
    private const float ChunkLen = 192f;

    /// <summary>Retail heading_diff: the turn from h2 to h1 in the direction the turn command implies.</summary>
    public static float BearingDiff(float h1, float h2, uint pivotCmd)
    {
        float d = h1 - h2;
        if (MathF.Abs(h1 - h2) < Epsilon)
            d = 0f;
        if (d < -Epsilon)
            d += 360f;
        if (Epsilon < d && pivotCmd != LocomotionDirective.TurnRight)
            d = 360f - d;
        return d;
    }

    public static bool BearingGreater(float a, float b, uint pivotCmd)
    {
        bool greater = MathF.Abs(a - b) > 180f ? b > a : a > b;
        return pivotCmd == LocomotionDirective.TurnRight ? greater : !greater;
    }

    public static float PlaceBearing(Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X;
        float dy = to.Y - from.Y;
        return Wrap360(450f - MathF.Atan2(dy, dx) * DegPerRad);
    }

    public static float FetchBearing(Quaternion facing)
    {
        Vector3 ahead = Vector3.Transform(new Vector3(0f, 1f, 0f), facing);
        float yawDeg = MathF.Atan2(ahead.Y, ahead.X) * DegPerRad;
        return Wrap360(90f - yawDeg);
    }

    public static Quaternion ApplyBearing(Quaternion baseFacing, float bearingDeg)
    {
        _ = baseFacing;
        float yaw = (90f - bearingDeg) * RadPerDeg;
        return Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw - MathF.PI / 2f);
    }

    public static float BearingFromYaw(float yawRad) => Wrap360(90f - yawRad * DegPerRad);

    public static float YawFromBearing(float bearingDeg)
    {
        float yaw = (90f - bearingDeg) * RadPerDeg;
        while (yaw > MathF.PI) yaw -= 2f * MathF.PI;
        while (yaw < -MathF.PI) yaw += 2f * MathF.PI;
        return yaw;
    }

    /// <summary>Signed gap between two upright cylinders: positive apart, negative overlapping.</summary>
    public static float CylinderGap(
        float ownRadius, float ownHeight, Vector3 ownSpot,
        float markRadius, float markHeight, Vector3 markSpot)
    {
        float radialGap = Vector3.Distance(ownSpot, markSpot) - (ownRadius + markRadius);
        float verticalGap = ownSpot.Z <= markSpot.Z
            ? markSpot.Z - (ownSpot.Z + ownHeight)
            : ownSpot.Z - (markSpot.Z + markHeight);

        if (verticalGap > 0f && radialGap > 0f)
            return MathF.Sqrt(verticalGap * verticalGap + radialGap * radialGap);
        if (verticalGap < 0f && radialGap < 0f)
            return -MathF.Sqrt(verticalGap * verticalGap + radialGap * radialGap);
        return radialGap;
    }

    public static float CylinderGapNoZ(float ownRadius, Vector3 ownSpot, float markRadius, Vector3 markSpot)
    {
        float dx = markSpot.X - ownSpot.X;
        float dy = markSpot.Y - ownSpot.Y;
        return MathF.Sqrt(dx * dx + dy * dy) - ownRadius - markRadius;
    }

    /// <summary>Normalises in place; true when the vector was too small to have a direction.</summary>
    public static bool StandardizeVerifySmall(ref Vector3 v)
    {
        float len = v.Length();
        if (len < 1e-8f)
            return true;
        v /= len;
        return false;
    }

    public static Vector3 GlobalToOwnVec(Quaternion cycleFacing, Vector3 realmVec) =>
        Vector3.Transform(realmVec, Quaternion.Conjugate(cycleFacing));

    public static Vector3 OriginToRealm(
        uint originChamberIdent,
        float originX,
        float originY,
        float originZ,
        int onlineMiddleLbX,
        int onlineMiddleLbY)
    {
        int lbX = (int)((originChamberIdent >> 24) & 0xFFu);
        int lbY = (int)((originChamberIdent >> 16) & 0xFFu);
        return new Vector3(
            originX + (lbX - onlineMiddleLbX) * ChunkLen,
            originY + (lbY - onlineMiddleLbY) * ChunkLen,
            originZ);
    }

    private static float Wrap360(float bearingDeg)
    {
        bearingDeg %= 360f;
        return bearingDeg < 0f ? bearingDeg + 360f : bearingDeg;
    }
}
