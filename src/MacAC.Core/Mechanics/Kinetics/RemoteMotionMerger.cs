using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

public sealed class RemoteMotionMerger
{
    public bool ConstructShift(
        double dt,
        Vector3 latestCorpusLocus,
        Quaternion ori,
        MotionDeltaPose trunkLocomotionOwnCycle,
        LerpKeeper lerp,
        float upperPace,
        MotionDeltaPose product,
        bool inLink = true,
        bool isSticky = false)
    {
        ArgumentNullException.ThrowIfNull(trunkLocomotionOwnCycle);
        ArgumentNullException.ThrowIfNull(lerp);
        ArgumentNullException.ThrowIfNull(product);

        product.Origin = trunkLocomotionOwnCycle.Origin;
        product.Orientation = trunkLocomotionOwnCycle.Orientation;
        return lerp.AdjustOffset(dt, latestCorpusLocus, ori, upperPace, product, inLink, isSticky);
    }

    /// <summary>Convenience for callers with only a translation: the blended step in world space.</summary>
    public Vector3 CalculateShift(
        double dt,
        Vector3 latestCorpusLocus,
        Vector3 trunkLocomotionOwnDiff,
        Quaternion ori,
        LerpKeeper lerp,
        float upperPace)
    {
        MotionDeltaPose trunk = new MotionDeltaPose { Origin = trunkLocomotionOwnDiff };
        MotionDeltaPose blended = new MotionDeltaPose();
        ConstructShift(dt, latestCorpusLocus, ori, trunk, lerp, upperPace, blended);
        return Vector3.Transform(blended.Origin, ori);
    }
}
