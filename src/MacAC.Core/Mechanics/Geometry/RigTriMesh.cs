using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Realm;

namespace MacAC.Mechanics.Geometry;

/// <summary>Turns a multi-part setup into one MeshRef per part, posed at rest.</summary>
public static class RigTriMesh
{
    internal static readonly Pose PersonaCycle = new() { Origin = Vector3.Zero, Orientation = Quaternion.Identity };

    // Resting, then Default, then whichever placement frame comes first
    internal static MotionFrame? RestingCycle(RigSpec rig)
    {
        if (rig.Placements.TryGetValue(PlacementId.Resting, out MotionFrame? resting))
            return resting;
        if (rig.Placements.TryGetValue(PlacementId.Default, out MotionFrame? backup))
            return backup;
        foreach (MotionFrame any in rig.Placements.Values)
            return any;
        return null;
    }

    public static IReadOnlyList<TriMeshRef> Flatten(RigSpec rig, MotionFrame? locomotionCycleOverride = null)
    {
        MotionFrame? posture = locomotionCycleOverride ?? RestingCycle(rig);

        List<TriMeshRef> refs = new List<TriMeshRef>(rig.PartIds.Count);
        for (int idx = 0; idx < rig.PartIds.Count; ++idx)
        {
            Pose cycle = posture is not null && idx < posture.Poses.Count ? posture.Poses[idx] : PersonaCycle;
            Vector3 scaling = idx < rig.DefaultScale.Count ? rig.DefaultScale[idx] : Vector3.One;
            Matrix4x4 xform = Matrix4x4.CreateScale(scaling)
                * Matrix4x4.CreateFromQuaternion(cycle.Orientation)
                * Matrix4x4.CreateTranslation(cycle.Origin);
            refs.Add(new TriMeshRef((uint)rig.PartIds[idx], xform));
        }
        return refs;
    }
}
