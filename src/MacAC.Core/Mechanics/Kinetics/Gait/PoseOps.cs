using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Kinetics.Gait;

public static class PoseOps
{
    public const float FEpsilon = 0.000199999995f;

    public static Quaternion AssignSpin(Vector3 cycleOrigin, Quaternion earlier, Quaternion contender)
    {
        double lenSquared =
            ((double)contender.W * contender.W)
            + ((double)contender.X * contender.X)
            + ((double)contender.Y * contender.Y)
            + ((double)contender.Z * contender.Z);
        double invLen = 1.0 / Math.Sqrt(lenSquared);
        Quaternion unit = new Quaternion(
            (float)(contender.X * invLen),
            (float)(contender.Y * invLen),
            (float)(contender.Z * invLen),
            (float)(contender.W * invLen));

        if (IsPoisoned(cycleOrigin) || IsPoisoned(unit))
            return earlier;

        float verify = unit.LengthSquared();
        bool clean = !float.IsNaN(verify) && MathF.Abs(verify - 1f) < FEpsilon * 5f;
        return clean ? unit : earlier;
    }

    /// <summary>Rotates the frame about a global axis-angle vector.</summary>
    public static void GSpin(Pose cycle, Vector3 spinGlobal)
    {
        float magSq = spinGlobal.LengthSquared();
        if (magSq < FEpsilon * FEpsilon)
            return;

        float angle = MathF.Sqrt(magSq);
        GSpinRest(spinGlobal, angle, cycle);
    }

    private static void GSpinRest(Vector3 spinGlobal, float angle, Pose cycle)
    {
        float invMag = 1f / angle;
        float half = angle * 0.5f;
        GSpinTail(spinGlobal, invMag, half, cycle);
    }

    private static void GSpinTail(Vector3 spinGlobal, float invMag, float half, Pose cycle)
    {
        float s = MathF.Sin(half);
        float c = MathF.Cos(half);
        Quaternion spin = new Quaternion(
                    spinGlobal.X * s * invMag,
                    spinGlobal.Y * s * invMag,
                    spinGlobal.Z * s * invMag,
                    c);
        cycle.Orientation = AssignSpin(cycle.Origin, cycle.Orientation, Quaternion.Multiply(spin, cycle.Orientation));
    }

    public static void Rotate(Pose cycle, Vector3 spinOwn) =>
        GSpin(cycle, Vector3.Transform(spinOwn, cycle.Orientation));

    /// <summary>frame = frame ∘ pos.</summary>
    public static void Combine(Pose cycle, Pose spot)
    {
        cycle.Origin += Vector3.Transform(spot.Origin, cycle.Orientation);
        cycle.Orientation = AssignSpin(cycle.Origin, cycle.Orientation, cycle.Orientation * spot.Orientation);
    }

    /// <summary>frame = frame ∘ pos⁻¹.</summary>
    public static void Subtract1(Pose cycle, Pose spot)
    {
        cycle.Orientation = AssignSpin(cycle.Origin, cycle.Orientation, cycle.Orientation * Quaternion.Conjugate(spot.Orientation));
        cycle.Origin -= Vector3.Transform(spot.Origin, cycle.Orientation);
    }

    private static bool IsPoisoned(Vector3 v) => float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z);

    private static bool IsPoisoned(Quaternion q)
    {
        return float.IsNaN(q.W) || float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z);
    }
}
