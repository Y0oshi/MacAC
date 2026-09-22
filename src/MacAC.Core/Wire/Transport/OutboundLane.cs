using System.Buffers;
using System.Buffers.Binary;
using MacAC.Wire.Cryptography;
using MacAC.Wire.Messages;
using MacAC.Wire.Packets;

namespace MacAC.Wire.Transport;

// Sends one finalized datagram to the wire
internal delegate void DatagramSendDelegate(ReadOnlySpan<byte> datagram);

internal sealed class OutboundLane : IDisposable
{
    private readonly IsaacStream _isaac;
    private readonly LinkClock _clock;
    private readonly LinkStats _stats;
    private readonly DatagramSendDelegate _transmit;
    private readonly SentDatagramShelf _shelf;
    private readonly ArrayPool<byte> _reservoir;
    private readonly ushort _clientIdent;
    private readonly ushort _iteration;

    // NAKed ids waiting for the next sweep, kept in ascending wrap-safe order without duplicates
    private readonly List<uint> _resendFifo = [];

    public OutboundLane(
        IsaacStream outgoingIsaac,
        ushort sessClientIdent,
        ushort sessIteration,
        LinkClock timer,
        LinkStats stats,
        DatagramSendDelegate transmit,
        ArrayPool<byte>? reservoir = null,
        uint highestIdentSent = 1,
        uint fragmentSeries = 1)
    {
        ArgumentNullException.ThrowIfNull(outgoingIsaac);
        ArgumentNullException.ThrowIfNull(timer);
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(transmit);

        _isaac = outgoingIsaac;
        _clientIdent = sessClientIdent;
        _iteration = sessIteration;
        _clock = timer;
        _stats = stats;
        _transmit = transmit;
        _reservoir = reservoir ?? ArrayPool<byte>.Shared;
        _shelf = new SentDatagramShelf(_reservoir);
        HighestIdentSent = highestIdentSent;
        FragmentSeries = fragmentSeries;
    }

    public uint HighestIdentSent { get; private set; }

    // Fragment sequence the next reliable message takes; retail starts at 1
    public uint FragmentSeries { get; private set; }

    public uint AckWatermark { get; private set; }

    public int StashDepth => _shelf.Count;

    public int QueuedResendTally => _resendFifo.Count;

    // Sequence numbers skip zero on wrap
    public uint GlimpseUpcomingPacketSeries => HighestIdentSent == uint.MaxValue ? 1u : HighestIdentSent + 1u;

    public void TransmitPlayMsg(ReadOnlySpan<byte> playMsgCorpus, GameQueueGroup fifo)
    {
        byte[] buf = _reservoir.Rent(DatagramHeader.Size + WireFragmentHeader.Size + playMsgCorpus.Length);
        try
        {
            int corpusLen = GameFragment.EmitSingleFragment(buf.AsSpan(DatagramHeader.Size), FragmentSeries, fifo, playMsgCorpus);
            DatagramHeader preamble = new DatagramHeader
            {
                Sequence = GlimpseUpcomingPacketSeries,
                Flags = DatagramHeaderFlags.BlobFragments | DatagramHeaderFlags.EncryptedChecksum,
                Id = _clientIdent,
                Time = _clock.IntervalIdent,
                Iteration = _iteration,
            };
            int len = DatagramCodec.FinalizeInPlace(preamble, buf, corpusLen, optionalLength: 0, _isaac, out uint tag, out uint sealedChecksum);

            ++FragmentSeries;
            HighestIdentSent = preamble.Sequence;
            _transmit(buf.AsSpan(0, len));

            _shelf.Add(new SentDatagramShelf.ShelvedDatagram(preamble.Sequence, buf, corpusLen, sealedChecksum, tag, hasFragments: true), optionalLen: 0);
        }
        catch
        {
            _reservoir.Return(buf);
            throw;
        }
    }

    public void OnAckSeries(uint ackSeries)
    {
        _stats.AcksConsumed++;
        AckWatermark = SequenceArith.Max(AckWatermark, ackSeries);
    }

    // A NAK's first id doubles as an ack; every id we still hold is queued for resend
    public void OnRetransmitReq(ReadOnlySpan<byte> identOctets, int tally)
    {
        if (tally <= 0 || identOctets.Length < tally * 4)
            return;

        _stats.NakReqsReceived++;
        for (int idx = 0; idx < tally; ++idx)
        {
            uint ident = BinaryPrimitives.ReadUInt32LittleEndian(identOctets.Slice(idx * 4));
            if (idx is 0)
                OnAckSeries(ident);

            if (_shelf.Contains(ident))
                EnqueueResend(ident);
            else
                _stats.UncachedNakIdents++;
        }
    }

    public void TransmitQueuedResends()
    {
        if (_resendFifo.Count > 0)
        {
            foreach (uint ident in _resendFifo)
            {
                // Anything the watermark has passed since the NAK is no longer wanted
                if (AckWatermark is not 0 && SequenceArith.IsNewer(AckWatermark, ident))
                    continue;
                if (_shelf.TryGet(ident, out SentDatagramShelf.ShelvedDatagram shelved))
                    Resend(in shelved);
            }
            _resendFifo.Clear();
        }

        _shelf.DrainOlderThan(AckWatermark);
    }

    public void Dispose() => _shelf.Dispose();

    // The original header re-flagged as a retransmission with a fresh interval stamp and re-derived
    // checksum
    internal static DatagramHeader AssembleResendPreamble(in SentDatagramShelf.ShelvedDatagram shelved, ushort intervalIdent)
    {
        var preamble = DatagramHeader.Unpack(shelved.Buffer);
        preamble.Flags = DatagramHeaderFlags.Retransmission
            | DatagramHeaderFlags.EncryptedChecksum
            | (shelved.HasFragments ? DatagramHeaderFlags.BlobFragments : 0);
        preamble.Time = intervalIdent;
        preamble.Checksum = preamble.DerivePreambleHash32() + shelved.SealedChecksum;
        return preamble;
    }

    private void EnqueueResend(uint ident)
    {
        int at = 0;
        while (at < _resendFifo.Count && SequenceArith.IsNewer(ident, _resendFifo[at]))
            ++at;
        if (at < _resendFifo.Count && _resendFifo[at] == ident)
            return;
        _resendFifo.Insert(at, ident);
    }

    private void Resend(in SentDatagramShelf.ShelvedDatagram shelved)
    {
        AssembleResendPreamble(in shelved, _clock.IntervalIdent).Pack(shelved.Buffer);
        _transmit(shelved.Buffer.AsSpan(0, DatagramHeader.Size + shelved.CorpusLen));
        _stats.ResendsSent++;
    }
}
