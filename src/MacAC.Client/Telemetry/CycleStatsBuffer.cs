namespace MacAC.Client.Telemetry;

public sealed class CycleStatsBuffer
{
    private readonly long[] _loop;
    private readonly long[] _sorted;
    private int _front;

    public CycleStatsBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _loop = new long[capacity];
        _sorted = new long[capacity];
    }

    public int Count { get; private set; }

    public void Push(long val)
    {
        _loop[_front] = val;
        _front = (_front + 1) % _loop.Length;
        if (Count < _loop.Length)
            ++Count;
    }

    public void Reset()
    {
        _front = 0;
        Count = 0;
    }

    /// <summary>The 1-based nearest-rank percentile of the held samples; 0 when empty.</summary>
    public long Percentile(double q)
    {
        if (Count is 0)
            return 0;
        Array.Copy(_loop, _sorted, Count);
        Array.Sort(_sorted, 0, Count);
        int grade = Math.Clamp((int)Math.Ceiling(q * Count), 1, Count);
        return _sorted[grade - 1];
    }

    /// <summary>The largest held sample; 0 when empty, matching <see cref="Percentile"/>.</summary>
    public long Max()
    {
        if (Count is 0)
            return 0;
        long upper = _loop[0];
        for (int idx = 1; idx < Count; ++idx)
            upper = Math.Max(upper, _loop[idx]);
        return upper;
    }
}
