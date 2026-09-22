namespace MacAC.Client.Graphics;

internal readonly record struct ClientRollingTimingPercentiles(
    long MedianHundredthsMicroseconds,
    long Percentile95HundredthsMicroseconds);

internal sealed class RollingTimingSpecimenPane
{
    private readonly long[] _specimens;
    private int _cur;

    public RollingTimingSpecimenPane(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _specimens = new long[capacity];
    }

    public int Capacity => _specimens.Length;

    public void PushHundredthsMicroseconds(long specimen)
    {
        _specimens[_cur] = specimen;
        _cur = (_cur + 1) % _specimens.Length;
    }

    public void PushStopwatchBeats(long beats)
    {
        PushHundredthsMicroseconds(
            (long)(beats * 100_000_000.0 / System.Diagnostics.Stopwatch.Frequency));
    }

    public ClientRollingTimingPercentiles Freeze()
    {
        long[] duplicate = (long[])_specimens.Clone();
        Array.Sort(duplicate);
        int populated = 0;
        foreach (long specimen in duplicate)
        {
            if (specimen > 0)
                ++populated;
        }

        if (populated is 0)
            return default;

        long median = duplicate[duplicate.Length - 1 - (populated - 1) / 2];
        int p95Shift = (int)((populated - 1) * 0.05);
        long percentile95 = duplicate[duplicate.Length - 1 - p95Shift];
        return new ClientRollingTimingPercentiles(median, percentile95);
    }
}
