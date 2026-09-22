namespace MacAC.Client.Sound;

internal sealed class AlBufferAllowanceLedger
{
    private sealed class Entry
    {
        public required uint WaveIdent { get; init; }
        public required uint BufIdent { get; init; }
        public required long Bytes { get; init; }
        public long PreviousUseBeat { get; set; }
    }

    private readonly Dictionary<uint, Entry> _byWaveIdent = [];
    private long _beat;

    public AlBufferAllowanceLedger(long upperOctets)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(upperOctets, 1);
        UpperOctets = upperOctets;
    }

    public long UpperOctets { get; }

    public long ResidentOctets { get; private set; }

    public int Count => _byWaveIdent.Count;

    public bool TryFetchBufIdent(uint waveIdent, out uint bufIdent)
    {
        if (_byWaveIdent.TryGetValue(waveIdent, out var listing))
        {
            bufIdent = listing.BufIdent;
            return true;
        }
        bufIdent = 0;
        return false;
    }

    // Record a freshly-allocated, freshly-uploaded buffer as resident and most-recently-used
    public void CaptureBuilt(uint waveIdent, uint bufIdent, long octets)
    {
        long charged = Math.Max(1L, octets);
        _byWaveIdent[waveIdent] = new Entry
        {
            WaveIdent = waveIdent,
            BufIdent = bufIdent,
            Bytes = charged,
            PreviousUseBeat = ++_beat,
        };
        ResidentOctets += charged;
    }

    public void Touch(uint waveIdent)
    {
        if (_byWaveIdent.TryGetValue(waveIdent, out var listing))
            listing.PreviousUseBeat = ++_beat;
    }

    public bool TryEvictOldestUnprotected(
        Func<uint, bool> isProtected,
        out uint evictedWaveIdent,
        out uint evictedBufIdent)
    {
        ArgumentNullException.ThrowIfNull(isProtected);

        Entry? victim = null;
        foreach (Entry contender in _byWaveIdent.Values)
        {
            if (isProtected(contender.BufIdent))
                continue;
            if (victim is null || contender.PreviousUseBeat < victim.PreviousUseBeat)
                victim = contender;
        }

        if (victim is null)
        {
            evictedWaveIdent = 0;
            evictedBufIdent = 0;
            return false;
        }

        _byWaveIdent.Remove(victim.WaveIdent);
        ResidentOctets -= victim.Bytes;
        evictedWaveIdent = victim.WaveIdent;
        evictedBufIdent = victim.BufIdent;
        return true;
    }
}
