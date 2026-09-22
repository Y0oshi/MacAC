using System.Diagnostics;

namespace MacAC.Wire.Transport;

// Retail's half-second interval counter (the header Time field) driven from an injectable
// timestamp source
internal sealed class LinkClock
{
    private const int IntervalsPerSecond = 2;

    private readonly Func<long> _instant;
    private readonly long _stride;
    private long _mooring;

    public LinkClock(Func<long>? stampSrc = null, long? frequency = null)
    {
        _instant = stampSrc ?? Stopwatch.GetTimestamp;
        Frequency = frequency ?? Stopwatch.Frequency;
        if (Frequency < IntervalsPerSecond)
            throw new ArgumentOutOfRangeException(nameof(frequency), "the interval clock needs no fewer than 2 ticks per second");

        _stride = Frequency / IntervalsPerSecond;
        _mooring = _instant();
        IntervalIdent = 1;
    }

    // Ticks per second of the injected source
    public long Frequency { get; }

    public ushort IntervalIdent { get; private set; }

    public long GetTimestamp() => _instant();

    public void Update()
    {
        long passed = _instant() - _mooring;
        if (passed < _stride)
            return;

        long whole = passed / _stride;
        IntervalIdent = unchecked((ushort)(IntervalIdent + whole));
        _mooring += whole * _stride;
    }
}
