using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Retail Frame::set_vector_heading: aim an orientation along a direction, keeping +Z up.</summary>
public static class CanonPoseMath
{
    public static Quaternion AssignVectorBearing(Quaternion latestFacing, Vector3 dir)
    {
        float lenSquared = dir.LengthSquared();
        if (lenSquared < KineticConstants.EpsilonSq || !float.IsFinite(lenSquared))
            return latestFacing;

        Vector3 ahead = dir / MathF.Sqrt(lenSquared);
        Vector3 right = RightOf(ahead);
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, ahead));

        Matrix4x4 spin = new Matrix4x4(
            right.X, right.Y, right.Z, 0f,
            ahead.X, ahead.Y, ahead.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);

        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(spin));
    }

    // The horizontal axis perpendicular to ahead, scaled the retail way to avoid underflow
    private static Vector3 RightOf(Vector3 ahead)
    {
        if (ahead.X == 0f && ahead.Y == 0f)
            return Vector3.UnitX;

        float scaling = MathF.Max(MathF.Abs(ahead.X), MathF.Abs(ahead.Y));
        float x = ahead.X / scaling;
        float y = ahead.Y / scaling;
        float horizontalLen = MathF.Sqrt((x * x) + (y * y));
        return new Vector3(y / horizontalLen, -x / horizontalLen, 0f);
    }
}
