namespace MacAC.Wire.Transport;

// Plain counters, bumped in place by the transport pieces and read by telemetry
internal sealed class LinkStats
{
    public long PacketsReceived;
    public long PacketsSent;

    // Datagrams re-emitted because the server NAKed them
    public long ResendsSent;

    public long NakReqsReceived;
    public long UncachedNakIdents;

    // Inbound ack values folded into the watermark, whether explicit or implied by a NAK's first id
    public long AcksConsumed;

    public long IncomingDupsDropped;
    public long IncomingSanityDrops;
    public long ChecksumMisses;
    public long TagsShelved;
    public long AcksSent;
    public long NaksSent;
    public long NakIdentsSent;
    public long RejectWordsReclaimed;
    public long RejectsReceived;

    // Live depth of the resend cache, or 0 before the outbound side is wired
    public int StashZDepth => StashZDepthSrc?.Invoke() ?? 0;

    internal Func<int>? StashZDepthSrc { get; set; }
}
