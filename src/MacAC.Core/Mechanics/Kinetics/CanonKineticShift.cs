namespace MacAC.Mechanics.Kinetics;

public readonly record struct CanonKineticShift(
    KineticStateFlags PreviousState,
    KineticStateFlags RequestedState,
    KineticStateFlags FinalState,
    bool LightingChanged,
    bool NoDrawChanged,
    CanonHiddenShift HiddenTransition)
{
    public bool HasEdits => PreviousState != FinalState;
}

public enum CanonHiddenShift
{
    None,
    BecameHidden,
    BecameVisible,
}

public static class CanonKineticShifts
{
    public const KineticStateFlags ConstructorPhase =
        KineticStateFlags.EdgeSlide
        | KineticStateFlags.Lighting
        | KineticStateFlags.Gravity
        | KineticStateFlags.ReportCollisions;

    private const KineticStateFlags ConcealedDuo = KineticStateFlags.Hidden | KineticStateFlags.IgnoreCollisions;

    public static CanonKineticShift Apply(KineticStateFlags earlierPhase, KineticStateFlags askedPhase)
    {
        uint flipped = ((uint)earlierPhase ^ (uint)askedPhase) & 0xFFFFu;
        bool Flipped(KineticStateFlags bit) => (flipped & (uint)bit) is not 0;

        var final = askedPhase;
        var concealed = CanonHiddenShift.None;
        if (Flipped(KineticStateFlags.Hidden))
        {
            // Hiding also stops collision reporting and starts ignoring
            // collisions; unhiding restores both.
            if ((askedPhase & KineticStateFlags.Hidden) != 0)
            {
                final = (final & ~KineticStateFlags.ReportCollisions) | ConcealedDuo;
                concealed = CanonHiddenShift.BecameHidden;
            }
            else
            {
                final = (final & ~ConcealedDuo) | KineticStateFlags.ReportCollisions;
                concealed = CanonHiddenShift.BecameVisible;
            }
        }

        return new CanonKineticShift(
            earlierPhase,
            askedPhase,
            final,
            Flipped(KineticStateFlags.Lighting),
            Flipped(KineticStateFlags.NoDraw),
            concealed);
    }
}
