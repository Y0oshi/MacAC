using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public enum CanonActivityOutcome
{
    Suspended,
    Inactive,
    Reactivated,
    Active,
}

public static class CanonActivityGate
{
    public const float UpperKineticsGap = 96f;

    public static CanonActivityOutcome Evaluate(
        CanonQuantumClock timer,
        KineticBody? corpus,
        bool lifecycleEligible,
        bool hasPieceArr,
        bool isStatic,
        Vector3 objectLocus,
        Vector3? avatarLocus,
        double passedSecs)
    {
        ArgumentNullException.ThrowIfNull(timer);

        if (!lifecycleEligible)
        {
            Sleep(timer, corpus);
            return CanonActivityOutcome.Suspended;
        }

        if (avatarLocus is not { } avatar)
        {
            if (timer.IsActive)
                return CanonActivityOutcome.Active;
            timer.Advance(passedSecs);
            return CanonActivityOutcome.Inactive;
        }

        bool inBubble = !hasPieceArr || Vector3.Distance(objectLocus, avatar) <= UpperKineticsGap;
        if (!inBubble)
        {
            Sleep(timer, corpus);
            timer.Advance(passedSecs);
            return CanonActivityOutcome.Inactive;
        }

        bool woke = false;
        if (!isStatic)
        {
            woke = timer.Engage();
            if (corpus is not null)
                corpus.TransientState |= TransientPhaseFlagSet.Active;
        }

        if (!timer.IsActive)
        {
            timer.Advance(passedSecs);
            return CanonActivityOutcome.Inactive;
        }

        return woke ? CanonActivityOutcome.Reactivated : CanonActivityOutcome.Active;
    }

    private static void Sleep(CanonQuantumClock timer, KineticBody? corpus)
    {
        timer.Deactivate();
        if (corpus is not null)
            corpus.TransientState &= ~TransientPhaseFlagSet.Active;
    }
}
