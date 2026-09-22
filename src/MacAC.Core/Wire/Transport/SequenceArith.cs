namespace MacAC.Wire.Transport;

internal static class SequenceArith
{
    public static bool IsNewer(uint contender, uint reference) => unchecked((int)(contender - reference)) > 0;

    // Whichever of the two is newer; the second one on a tie
    public static uint Max(uint a, uint b) => IsNewer(a, b) ? a : b;
}
