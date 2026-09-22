using System.Buffers.Binary;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Transport;

// Emits the periodic ack, or - while anything is missing - the periodic NAK instead
internal sealed class AckNakPacer
{
    public const double AckLatchSecs = 2.0;
    public const double NakLatchSecs = 0.6;
    public const int UpperNakIdentsPerPacket = 114;

    private readonly LinkClock _clock;
    private readonly InboundSequenceLedger _incoming;
    private readonly OutboundLane _outgoing;
    private readonly LinkStats _stats;
    private readonly DatagramSendDelegate _transmit;
    private readonly ushort _clientIdent;
    private readonly ushort _iteration;
    private readonly long _ackLatch;
    private readonly long _nakLatch;
    private readonly List<uint> _absent = [];
    private long _previousEmit;

    public AckNakPacer(
        LinkClock timer,
        InboundSequenceLedger incoming,
        OutboundLane outgoing,
        ushort sessClientIdent,
        ushort sessIteration,
        LinkStats stats,
        DatagramSendDelegate transmit)
    {
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(transmit);

        _clock = timer;
        _incoming = incoming;
        _outgoing = outgoing;
        _clientIdent = sessClientIdent;
        _iteration = sessIteration;
        _stats = stats;
        _transmit = transmit;
        _ackLatch = (long)Math.Round(AckLatchSecs * timer.Frequency);
        _nakLatch = (long)Math.Round(NakLatchSecs * timer.Frequency);
        _previousEmit = timer.GetTimestamp();
    }

    public void Sweep(long instant)
    {
        long since = instant - _previousEmit;
        if (_incoming.NakTally > 0)
        {
            if (since <= _nakLatch)
                return;
            TransmitNak();
        }
        else
        {
            if (since < _ackLatch)
                return;
            TransmitAck();
        }
        _previousEmit = instant;
    }

    private DatagramHeader PreambleFor(DatagramHeaderFlags flagSet)
    {
        return new()
        {
            Sequence = _outgoing.HighestIdentSent,
            Flags = flagSet,
            Id = _clientIdent,
            Time = _clock.IntervalIdent,
            Iteration = _iteration,
        };
    }

    // Seals cycle (header + body) as a header-only datagram whose body is entirely optional-header
    // bytes
    private void Close(DatagramHeaderFlags flagSet, Span<byte> cycle, int corpusLen)
    {
        int len = DatagramCodec.FinalizeInPlace(PreambleFor(flagSet), cycle, corpusLen, optionalLen: corpusLen, outgoingIsaac: null);
        _transmit(cycle.Slice(0, len));
    }

    private void TransmitAck()
    {
        Span<byte> cycle = stackalloc byte[DatagramHeader.Size + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(cycle.Slice(DatagramHeader.Size), _incoming.HighestIdentReceived);
        Close(DatagramHeaderFlags.AckSequence, cycle, sizeof(uint));
        _stats.AcksSent++;
    }

    private void TransmitNak()
    {
        _incoming.DuplicateNakkedSequencesAscending(_absent, UpperNakIdentsPerPacket);
        int tally = _absent.Count;

        Span<byte> cycle = stackalloc byte[DatagramHeader.Size + sizeof(uint) + UpperNakIdentsPerPacket * sizeof(uint)];
        Span<byte> corpus = cycle.Slice(DatagramHeader.Size);
        BinaryPrimitives.WriteUInt32LittleEndian(corpus, (uint)tally);
        for (int idx = 0; idx < tally; ++idx)
            BinaryPrimitives.WriteUInt32LittleEndian(corpus.Slice(sizeof(uint) * (idx + 1)), _absent[idx]);

        Close(DatagramHeaderFlags.RequestRetransmit, cycle, sizeof(uint) * (tally + 1));
        _stats.NaksSent++;
        _stats.NakIdentsSent += tally;
    }
}
