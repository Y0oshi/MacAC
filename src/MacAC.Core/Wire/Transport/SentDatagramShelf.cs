using System.Buffers;

namespace MacAC.Wire.Transport;

internal sealed class SentDatagramShelf : IDisposable
{
    internal readonly struct ShelvedDatagram(uint series, byte[] buf, int corpusLen, uint sealedChecksum, uint isaacTag, bool hasFragments)
    {
        public uint Sequence { get; } = series;

        // Rented wire buffer: header in [0, 20), body in [20, 20 + BodyLength)
        public byte[] Buffer { get; } = buf;

        public int CorpusLen { get; } = corpusLen;
        public uint SealedChecksum { get; } = sealedChecksum;
        public uint IsaacTag { get; } = isaacTag;
        public bool HasFragments { get; } = hasFragments;
    }

    private readonly ArrayPool<byte> _reservoir;
    private readonly Queue<ShelvedDatagram> _oldestLead = new();
    private readonly Dictionary<uint, ShelvedDatagram> _ordinal = new();

    public SentDatagramShelf(ArrayPool<byte>? reservoir = null) => _reservoir = reservoir ?? ArrayPool<byte>.Shared;

    public int Count => _oldestLead.Count;

    public void Add(in ShelvedDatagram datagram, int optionalLen)
    {
        if (optionalLen is not 0)
        {
            throw new InvalidOperationException(
                "reliable packets must not carry optional headers; the sent-packet "
                + "store keeps only the headerless body for retransmission");
        }

        _ordinal.Add(datagram.Sequence, datagram);
        _oldestLead.Enqueue(datagram);
    }

    public bool Contains(uint series) => _ordinal.ContainsKey(series);

    public bool TryGet(uint series, out ShelvedDatagram datagram) => _ordinal.TryGetValue(series, out datagram);

    // Releases everything strictly older than watermark
    public void DrainOlderThan(uint watermark)
    {
        while (_oldestLead.TryPeek(out ShelvedDatagram front) && SequenceArith.IsNewer(watermark, front.Sequence))
        {
            _oldestLead.Dequeue();
            _ordinal.Remove(front.Sequence);
            _reservoir.Return(front.Buffer);
        }
    }

    public void Dispose()
    {
        while (_oldestLead.TryDequeue(out ShelvedDatagram shelved))
            _reservoir.Return(shelved.Buffer);
        _ordinal.Clear();
    }
}
