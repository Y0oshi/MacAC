namespace MacAC.Wire.Transport;

internal sealed class CanonLossAverager
{
    public const int PaneDims = 40;
    public const double CaptureSecs = 2.0;

    private readonly record struct Sample(ushort Sent, ushort Resent, ushort Received, ushort Nakked);

    // A counter's last-seen value, so each sweep can take a delta
    private struct Tap(long observed)
    {
        private long _observed = observed;

        public ushort Take(long latest)
        {
            long diff = latest - _observed;
            _observed = latest;
            return unchecked((ushort)Math.Max(0L, diff));
        }
    }

    private readonly Sample[] _loop = new Sample[PaneDims];
    private readonly long _period;
    private long _previousCapture;
    private Tap _sent, _resent, _received, _nakked;
    private long _sentTotal, _resentTotal, _receivedTotal, _nakkedTotal;
    private int _front;
    private int _filled;

    public CanonLossAverager(LinkClock timer, LinkStats stats)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stats);

        _period = (long)Math.Round(CaptureSecs * timer.Frequency);
        _previousCapture = timer.GetTimestamp();
        _sent = new Tap(stats.PacketsSent);
        _resent = new Tap(stats.ResendsSent);
        _received = new Tap(stats.PacketsReceived);
        _nakked = new Tap(stats.NakIdentsSent);
    }

    public double Percentage
    {
        get
        {
            long traffic = _receivedTotal + _sentTotal;
            return traffic <= 0 ? 0d : 100d * (_nakkedTotal + _resentTotal) / traffic;
        }
    }

    public void Sweep(long instant, LinkStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        if (instant - _previousCapture < _period)
            return;

        // One sample per heartbeat that fires; skipped frames get no filler
        _previousCapture = instant;
        Sample specimen = new Sample(
            _sent.Take(stats.PacketsSent),
            _resent.Take(stats.ResendsSent),
            _received.Take(stats.PacketsReceived),
            _nakked.Take(stats.NakIdentsSent));

        if (_filled == PaneDims)
            Fold(_loop[_front], -1);
        else
            ++_filled;

        _loop[_front] = specimen;
        _front = (_front + 1) % PaneDims;
        Fold(specimen, +1);
    }

    private void Fold(Sample specimen, int sign)
    {
        _sentTotal += sign * specimen.Sent;
        _resentTotal += sign * specimen.Resent;
        _receivedTotal += sign * specimen.Received;
        _nakkedTotal += sign * specimen.Nakked;
    }
}
