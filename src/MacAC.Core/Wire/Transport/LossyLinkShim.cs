using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Transport;

// Test-only carrier wrapper that drops a percentage of datagrams in one or both directions
internal sealed class LossyLinkShim : IRealmCarrier
{
    // One direction's dice plus its dropped/forwarded tallies
    private sealed class Gate(bool turnedOn, int seed, int discardPct)
    {
        private readonly Random _dice = new(seed);
        private int _dropped;
        private int _forwarded;

        public int Dropped => Volatile.Read(ref _dropped);
        public int Forwarded => Volatile.Read(ref _forwarded);

        // True when this datagram should be eaten (only once the shim is armed)
        public bool Eats(bool loaded)
        {
            if (!loaded || !turnedOn || _dice.Next(100) >= discardPct)
                return false;
            Interlocked.Increment(ref _dropped);
            return true;
        }

        public void Passed() => Interlocked.Increment(ref _forwarded);
    }

    private readonly IRealmCarrier _interior;
    private readonly Gate _out;
    private readonly Gate _in;
    private volatile bool _loaded;

    public LossyLinkShim(IRealmCarrier interior, int discardPct, int seed, DropSide dir)
    {
        ArgumentNullException.ThrowIfNull(interior);
        ArgumentOutOfRangeException.ThrowIfLessThan(discardPct, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(discardPct, 100);

        _interior = interior;
        _out = new Gate(dir is DropSide.Out or DropSide.Both, seed, discardPct);
        _in = new Gate(dir is DropSide.In or DropSide.Both, ~seed, discardPct);
        Console.WriteLine($"[net-loss] active pct={discardPct} seed={seed} dir={dir}");
    }

    public int OutgoingDropped => _out.Dropped;

    public int IncomingDropped => _in.Dropped;

    // True once the first encrypted outbound datagram has been forwarded
    public bool IsArmed => _loaded;

    public static IRealmCarrier EncloseIfConfigured(IRealmCarrier interior)
    {
        return WireTelemetry.NetDiscardPct > 0
            ? new LossyLinkShim(interior, WireTelemetry.NetDiscardPct, WireTelemetry.NetDiscardSeed, WireTelemetry.NetDiscardDirection)
            : interior;
    }

    public void Send(ReadOnlySpan<byte> datagram)
    {
        if (_out.Eats(_loaded))
            return;
        _interior.Send(datagram);
        Forwarded(datagram);
    }

    public void Send(IPEndPoint distant, ReadOnlySpan<byte> datagram)
    {
        if (_out.Eats(_loaded))
            return;
        _interior.Send(distant, datagram);
        Forwarded(datagram);
    }

    public int Take(Span<byte> dest, TimeSpan timeout, out IPEndPoint? from)
    {
        long deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        TimeSpan pause = timeout;
        while (true)
        {
            int len = _interior.Take(dest, pause, out from);
            if (len < 0)
                return len;
            if (!_in.Eats(_loaded))
            {
                _in.Passed();
                return len;
            }

            // Still eating when the budget runs out: report a timeout ourselves,
            // because a 0 ms socket timeout would mean "wait forever" below.
            double leftMsec = (deadline - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (leftMsec < 1.0)
            {
                from = null;
                return -1;
            }
            pause = TimeSpan.FromMilliseconds(leftMsec);
        }
    }

    public async ValueTask<InboundVerdict> TakeAsync(Memory<byte> dest, CancellationToken abortTicket)
    {
        while (true)
        {
            var verdict = await _interior.TakeAsync(dest, abortTicket).ConfigureAwait(false);
            if (_in.Eats(_loaded))
                continue;
            _in.Passed();
            return verdict;
        }
    }

    public void Dispose()
    {
        Console.WriteLine(
            $"[net-loss] dropped out={OutgoingDropped} in={IncomingDropped}"
            + $" forwarded out={_out.Forwarded}"
            + $" in={_in.Forwarded}"
            + $" armed={_loaded}");
        _interior.Dispose();
    }

    private void Forwarded(ReadOnlySpan<byte> datagram)
    {
        _out.Passed();
        if (_loaded)
            return;
        bool encrypted = datagram.Length > DatagramHeader.Size
            && (BinaryPrimitives.ReadUInt32LittleEndian(datagram.Slice(4)) & (uint)DatagramHeaderFlags.EncryptedChecksum) is not 0;
        if (encrypted)
        {
            _loaded = true;
            Console.WriteLine("[net-loss] armed");
        }
    }
}
