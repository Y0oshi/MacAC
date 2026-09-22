namespace MacAC.Client.Graphics;

// Frame-deadline arithmetic shared by the platform waiters
internal static class CyclePacingInterval
{
    private const long NanosecondsPerSecond = 1_000_000_000L;

    internal static long TranslateBeatsToNanoseconds(
        long durationTicks,
        long clockFrequency)
    {
        if (durationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationTicks));
        if (clockFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(clockFrequency));

        long wholeSecs = Math.DivRem(
            durationTicks,
            clockFrequency,
            out long remainder);
        if (wholeSecs >= long.MaxValue / NanosecondsPerSecond)
            return long.MaxValue;

        long wholeNanoseconds = wholeSecs * NanosecondsPerSecond;
        long fractionalNanoseconds = checked((long)Math.Ceiling(
            remainder * (double)NanosecondsPerSecond / clockFrequency));
        return wholeNanoseconds > long.MaxValue - fractionalNanoseconds
            ? long.MaxValue
            : Math.Max(1L, wholeNanoseconds + fractionalNanoseconds);
    }
}
