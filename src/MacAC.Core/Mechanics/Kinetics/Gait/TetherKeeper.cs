using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class TetherKeeper(IKineticObjHost host)
{
    private readonly IKineticObjHost _hub = host ?? throw new ArgumentNullException(nameof(host));

    public bool IsConstrained { get; private set; }

    public float ConstraintSpotShift { get; private set; }

    public Locus ConstraintSpot { get; private set; }

    public float ConstraintGapBegin { get; private set; }

    public float ConstraintGapUpper { get; private set; }

    public void ConstrainTo(Locus mooring, float beginGap, float upperGap)
    {
        IsConstrained = true;
        ConstraintSpot = mooring;
        ConstraintGapBegin = beginGap;
        ConstraintGapUpper = upperGap;
        ConstraintSpotShift = Vector3.Distance(mooring.Frame.Origin, _hub.Position.Frame.Origin);
    }

    public void UnConstrain() => IsConstrained = false;

    public bool IsFullyConstrained() => ConstraintGapUpper * 0.9f < ConstraintSpotShift;

    public void AdjustOffset(MotionDeltaPose shift, double quantum)
    {
        _ = quantum;
        if (!IsConstrained)
            return;

        if (_hub.InContact)
        {
            if (ConstraintSpotShift >= ConstraintGapUpper)
            {
                shift.Origin = Vector3.Zero; // past max - fully pinned
            }
            else if (ConstraintSpotShift > ConstraintGapBegin)
            {
                float taper = (ConstraintGapUpper - ConstraintSpotShift) / (ConstraintGapUpper - ConstraintGapBegin);
                shift.Origin *= taper;
            }
        }

        // Unconditional (grounded OR airborne): track this tick's step length
        ConstraintSpotShift = shift.Origin.Length();
    }
}
