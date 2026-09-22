using System.Buffers;
using MacAC.Wire.Cryptography;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Transport;

internal sealed class ReliableLink : IDisposable
{
    public const double AssemblerSweepSecs = 5.0;

    private readonly FragmentStitcher? _stitcher;
    private readonly CanonLossAverager _loss;
    private readonly long _stitcherSweepBeats;
    private long _stitcherSweptAt;

    public ReliableLink(
        IsaacStream outgoingIsaac,
        IsaacStream incomingIsaac,
        ushort sessClientIdent,
        ushort sessIteration,
        DatagramSendDelegate transmit,
        LinkClock? timer = null,
        ArrayPool<byte>? reservoir = null,
        FragmentStitcher? assembler = null)
    {
        Clock = timer ?? new LinkClock();
        Stats = new LinkStats();
        _stitcher = assembler;
        // Rounded the same way as the pacer gates so a fractional frequency cannot drift the sweep.
        _stitcherSweepBeats = (long)Math.Round(AssemblerSweepSecs * Clock.Frequency);
        _stitcherSweptAt = Clock.GetTimestamp();

        LinkStats stats = Stats;
        void Counted(ReadOnlySpan<byte> datagram)
        {
            transmit(datagram);
            stats.PacketsSent++;
        }

        Outgoing = new OutboundLane(outgoingIsaac, sessClientIdent, sessIteration, Clock, Stats, Counted, reservoir);
        Inbound = new InboundSequenceLedger(incomingIsaac, Stats);
        Scheduler = new AckNakPacer(Clock, Inbound, Outgoing, sessClientIdent, sessIteration, Stats, Counted);
        Stats.StashZDepthSrc = () => Outgoing.StashDepth;
        _loss = new CanonLossAverager(Clock, Stats);
    }

    public LinkClock Clock { get; }

    public OutboundLane Outgoing { get; }

    // Inbound ISAAC, the received-id watermark and the NAK set
    public InboundSequenceLedger Inbound { get; }

    public AckNakPacer Scheduler { get; }

    public LinkStats Stats { get; }

    public double PacketLossPercentage => _loss.Percentage;

    public uint HighestTagSent => Outgoing.HighestIdentSent;

    public void Sweep()
    {
        Clock.Update();
        long instant = Clock.GetTimestamp();
        _loss.Sweep(instant, Stats);
        Scheduler.Sweep(instant);
        Outgoing.TransmitQueuedResends();

        if (_stitcher is { } stitcher && instant - _stitcherSweptAt >= _stitcherSweepBeats)
        {
            _stitcherSweptAt = instant;
            stitcher.SweepExpired();
        }
    }

    public void Dispose() => Outgoing.Dispose();
}
