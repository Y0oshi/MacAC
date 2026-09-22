using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class MotionDeltaPose
{
    public Vector3 Origin;

    public Quaternion Orientation = Quaternion.Identity;

    public void Reset()
    {
        Origin = Vector3.Zero;
        Orientation = Quaternion.Identity;
    }

    /// <summary>Applies a local-space step on top of what has accumulated so far.</summary>
    public void Combine(Vector3 ownOrigin, Quaternion ownFacing, float originScaling = 1f)
    {
        Origin += Vector3.Transform(ownOrigin * originScaling, Orientation);
        Orientation = PoseOps.AssignSpin(Origin, Orientation, Orientation * ownFacing);
    }

    public void Combine(MotionDeltaPose diff, float originScaling = 1f)
    {
        ArgumentNullException.ThrowIfNull(diff);
        Combine(diff.Origin, diff.Orientation, originScaling);
    }

    public float ObtainBearing() => ApproachMath.FetchBearing(Orientation);

    public void AssignHeading(float bearingDeg) => Orientation = ApproachMath.ApplyBearing(Orientation, bearingDeg);
}
