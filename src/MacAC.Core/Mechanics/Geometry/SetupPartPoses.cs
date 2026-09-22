using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Geometry;

/// <summary>Rigid per-part transforms for a setup in its resting (or given) frame.</summary>
public static class SetupPartPoses
{
    public static IReadOnlyList<Matrix4x4> Compute(RigSpec rig, MotionFrame? locomotionCycleOverride = null, float objectScaling = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(rig);
        MotionFrame? src = locomotionCycleOverride ?? RigTriMesh.RestingCycle(rig);
        if (src is null)
            return [];

        Matrix4x4[] postures = new Matrix4x4[rig.PartIds.Count];
        for (int idx = 0; idx < postures.Length; ++idx)
        {
            Pose cycle = idx < src.Poses.Count ? src.Poses[idx] : RigTriMesh.PersonaCycle;
            postures[idx] = Matrix4x4.CreateFromQuaternion(cycle.Orientation)
                * Matrix4x4.CreateTranslation(cycle.Origin * objectScaling);
        }
        return postures;
    }
}
