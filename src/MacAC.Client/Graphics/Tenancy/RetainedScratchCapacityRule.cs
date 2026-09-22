namespace MacAC.Client.Graphics.Tenancy;

internal readonly record struct AlphaScratchAllowanceProfile(
    long QueueBytes,
    long DispatcherBytes,
    long ParticleBytes)
{
    public long SumOctets
    {
        get
        {
            return checked(
        QueueBytes + DispatcherBytes + ParticleBytes);
        }
    }

    public static AlphaScratchAllowanceProfile Create(long sumOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sumOctets);
        long fifo = sumOctets / 4;
        long motes = sumOctets / 4;
        long router = checked(sumOctets - fifo - motes);
        return new AlphaScratchAllowanceProfile(fifo, router, motes);
    }
}

internal sealed class RetainedScratchCapacityRule(
    long budgetBytes,
    int lowDemandSamplesBeforeShrink = 3)
{
    private readonly int _loDemandSpecimensPriorContract =
        lowDemandSamplesBeforeShrink > 0
            ? lowDemandSamplesBeforeShrink
            : throw new ArgumentOutOfRangeException(
                nameof(lowDemandSamplesBeforeShrink));
    private int _loDemandSpecimens;

    public long AllowanceBytes { get; } = budgetBytes > 0
            ? budgetBytes
            : throw new ArgumentOutOfRangeException(nameof(budgetBytes));

    public int WatchAndPickCap(
        int latestCap,
        int neededCap,
        int octetsPerUnit,
        int floorCap,
        int growthQuantum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(latestCap);
        ArgumentOutOfRangeException.ThrowIfNegative(neededCap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(octetsPerUnit);
        ArgumentOutOfRangeException.ThrowIfNegative(floorCap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthQuantum);

        if (neededCap > latestCap)
        {
            _loDemandSpecimens = 0;
            return RoundUp(neededCap, growthQuantum);
        }

        int allowanceCap = checked((int)Math.Min(
            int.MaxValue,
            Math.Max(floorCap, AllowanceBytes / octetsPerUnit)));
        bool demandHasFallen =
            latestCap > allowanceCap
            && neededCap <= allowanceCap
            && (neededCap is 0
                || (long)latestCap >= (long)neededCap * 4);
        if (!demandHasFallen)
        {
            _loDemandSpecimens = 0;
            return latestCap;
        }

        ++_loDemandSpecimens;
        if (_loDemandSpecimens < _loDemandSpecimensPriorContract)
            return latestCap;

        _loDemandSpecimens = 0;
        long warmedDemand = Math.Max(
            floorCap,
            Math.Min(int.MaxValue, (long)neededCap * 2));
        int mark = RoundUp(
            checked((int)Math.Min(warmedDemand, allowanceCap)),
            growthQuantum);
        mark = Math.Max(neededCap, Math.Min(mark, allowanceCap));
        return Math.Min(latestCap, mark);
    }

    private static int RoundUp(int val, int quantum)
    {
        if (val is 0)
            return 0;
        long rounded = ((long)val + quantum - 1) / quantum * quantum;
        return checked((int)Math.Min(int.MaxValue, rounded));
    }
}
