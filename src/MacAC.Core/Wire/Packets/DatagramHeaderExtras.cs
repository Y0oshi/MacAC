namespace MacAC.Wire.Packets;

public sealed class DatagramHeaderExtras
{
    public byte[] RawOctets { get; private set; } = [];
    public uint AckSequence { get; private set; }
    public IReadOnlyList<uint> RetransmitReqs { get; private set; } = [];
    public IReadOnlyList<uint> RejectRetransmits { get; private set; } = [];
    public double TimeSync { get; private set; }
    public float EchoReqClientMoment { get; private set; }
    public uint FlowOctets { get; private set; }
    public ushort FlowInterval { get; private set; }
    public double LinkReqSrvMoment { get; private set; }
    public ulong LinkReqCookie { get; private set; }
    public uint LinkReqClientIdent { get; private set; }

    /// <summary>Seed for the ISAAC that verifies INBOUND packets (the server's outgoing stream).</summary>
    public uint LinkReqSrvSeed { get; private set; }

    /// <summary>Seed for the ISAAC that seals OUTBOUND packets (the server's incoming stream).</summary>
    public uint LinkReqClientSeed { get; private set; }

    /// <summary>Returns the bytes consumed, or -1 when a flagged block is truncated.</summary>
    public int Parse(ReadOnlySpan<byte> corpus, DatagramHeaderFlags flagSet)
    {
        if (!OptionalLayout.TryScan(corpus, flagSet, out OptionalLayout arrangement))
            return -1;

        if (arrangement.RetransmitTally > 0)
            RetransmitReqs = arrangement.Idents(corpus, arrangement.RetransmitShift, arrangement.RetransmitTally);
        if (arrangement.RejectTally > 0)
            RejectRetransmits = arrangement.Idents(corpus, arrangement.RejectShift, arrangement.RejectTally);
        AckSequence = arrangement.AckSequence;
        TimeSync = arrangement.TimeSync;
        EchoReqClientMoment = arrangement.EchoReqClientTime;
        FlowOctets = arrangement.FlowBytes;
        FlowInterval = arrangement.FlowInterval;
        LinkReqSrvMoment = arrangement.LinkRequestServerTime;
        LinkReqCookie = arrangement.LinkRequestCookie;
        LinkReqClientIdent = arrangement.LinkReqClientTag;
        LinkReqSrvSeed = arrangement.LinkRequestServerSeed;
        LinkReqClientSeed = arrangement.LinkRequestClientSeed;
        RawOctets = corpus.Slice(0, arrangement.Consumed).ToArray();
        return arrangement.Consumed;
    }

    public uint DeriveHash32() => Cryptography.CanonHash32.Work(RawOctets);
}
