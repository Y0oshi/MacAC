using System.Buffers.Binary;

namespace MacAC.Wire.Packets;

// One pass over the optional header area in flag order
internal struct OptionalLayout
{
    private const uint UpperIdentRoster = 1024;

    public int Consumed;
    public uint AckSequence;
    public double TimeSync;
    public float EchoReqClientTime;
    public uint FlowBytes;
    public ushort FlowInterval;
    public double LinkRequestServerTime;
    public ulong LinkRequestCookie;
    public uint LinkReqClientTag;
    public uint LinkRequestServerSeed;
    public uint LinkRequestClientSeed;
    public int RetransmitShift, RetransmitTally;
    public int RejectShift, RejectTally;

    // False when a flagged block does not fit in corpus
    public static bool TryScan(ReadOnlySpan<byte> corpus, DatagramHeaderFlags flagSet, out OptionalLayout arrangement)
    {
        arrangement = default;
        int at = 0;
        bool Has(DatagramHeaderFlags bit) => (flagSet & bit) != 0;

        if (Has(DatagramHeaderFlags.ServerSwitch) && !Step(ref at, 8, corpus))
            return false;

        if (Has(DatagramHeaderFlags.RequestRetransmit) && !IdentRoster(corpus, ref at, out arrangement.RetransmitShift, out arrangement.RetransmitTally))
            return false;

        if (Has(DatagramHeaderFlags.RejectRetransmit) && !IdentRoster(corpus, ref at, out arrangement.RejectShift, out arrangement.RejectTally))
            return false;

        if (Has(DatagramHeaderFlags.AckSequence))
        {
            if (corpus.Length - at < 4)
                return false;
            arrangement.AckSequence = BinaryPrimitives.ReadUInt32LittleEndian(corpus.Slice(at));
            at += 4;
        }

        if (Has(DatagramHeaderFlags.LoginRequest))
        {
            // The login body owns the rest of the datagram
            arrangement.Consumed = corpus.Length;
            return true;
        }

        if (Has(DatagramHeaderFlags.WorldLoginRequest) && !Step(ref at, 8, corpus))
            return false;

        if (Has(DatagramHeaderFlags.ConnectRequest))
        {
            if (corpus.Length - at < 32)
                return false;
            var chunk = corpus.Slice(at);
            arrangement.LinkRequestServerTime = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(chunk));
            arrangement.LinkRequestCookie = BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(8));
            arrangement.LinkReqClientTag = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(16));
            arrangement.LinkRequestServerSeed = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(20));
            arrangement.LinkRequestClientSeed = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(24));
            at += 32; // the last four bytes are padding
        }

        if (Has(DatagramHeaderFlags.ConnectResponse) && !Step(ref at, 8, corpus))
            return false;

        if (Has(DatagramHeaderFlags.CICMDCommand) && !Step(ref at, 8, corpus))
            return false;

        if (Has(DatagramHeaderFlags.TimeSync))
        {
            if (corpus.Length - at < 8)
                return false;
            arrangement.TimeSync = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(corpus.Slice(at)));
            at += 8;
        }

        if (Has(DatagramHeaderFlags.EchoRequest))
        {
            if (corpus.Length - at < 4)
                return false;
            arrangement.EchoReqClientTime = BinaryPrimitives.ReadSingleLittleEndian(corpus.Slice(at));
            at += 4;
        }

        if (Has(DatagramHeaderFlags.Flow))
        {
            if (corpus.Length - at < 6)
                return false;
            arrangement.FlowBytes = BinaryPrimitives.ReadUInt32LittleEndian(corpus.Slice(at));
            arrangement.FlowInterval = BinaryPrimitives.ReadUInt16LittleEndian(corpus.Slice(at + 4));
            at += 6;
        }

        arrangement.Consumed = at;
        return true;
    }

    public readonly uint[] Idents(ReadOnlySpan<byte> corpus, int shift, int tally)
    {
        uint[] idents = new uint[tally];
        for (int idx = 0; idx < tally; ++idx)
            idents[idx] = BinaryPrimitives.ReadUInt32LittleEndian(corpus.Slice(shift + idx * 4));
        return idents;
    }

    private static bool Step(ref int at, int num, ReadOnlySpan<byte> corpus)
    {
        if (corpus.Length - at < num)
            return false;
        at += num;
        return true;
    }

    // count:u32 then count ids; the ids are left in place and located by offset
    private static bool IdentRoster(ReadOnlySpan<byte> corpus, ref int at, out int shift, out int tally)
    {
        shift = 0;
        tally = 0;
        if (!Step(ref at, 4, corpus))
            return false;
        uint num = BinaryPrimitives.ReadUInt32LittleEndian(corpus.Slice(at - 4));
        if (num > UpperIdentRoster || corpus.Length - at < (int)num * 4)
            return false;
        shift = at;
        tally = checked((int)num);
        at += tally * 4;
        return true;
    }
}
