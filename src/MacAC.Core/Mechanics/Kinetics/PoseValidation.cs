using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public static class PoseValidation
{
    private const float UnitTolerance = 0.001f;

    public static bool IsValid(uint chamberIdent, Vector3 origin, Quaternion spin)
    {
        if (!MechLandDefs.IncomingValidChamberIdent(chamberIdent))
            return false;
        if (HasNaNum(origin) || HasNaNum(spin))
            return false;

        float normSquared = spin.LengthSquared();
        return !float.IsNaN(normSquared) && MathF.Abs(normSquared - 1f) <= UnitTolerance;
    }

    private static bool HasNaNum(Vector3 v) => float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z);

    private static bool HasNaNum(Quaternion q)
    {
        return float.IsNaN(q.W) || float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z);
    }
}
