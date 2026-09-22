namespace MacAC.Wire.Messages;

/// <summary>Secure-trade GameActions (0x01F6-0x0204).</summary>
public static class TradeAsks
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint OpenBarterNegotiationsOpcode = 0x01F6u;
    public const uint ShutBarterNegotiationsOpcode = 0x01F7u;
    public const uint AppendToBarterOpcode = 0x01F8u;
    public const uint AdmitBarterOpcode = 0x01FAu;
    public const uint DeclineBarterOpcode = 0x01FBu;
    public const uint RestartBarterOpcode = 0x0204u;

    public static byte[] AssembleOpenBarterNegotiations(uint playActSeries, uint partnerOid)
    {
        return new GameActionScribe(playActSeries, OpenBarterNegotiationsOpcode, 16).U32(partnerOid).Bytes();
    }

    public static byte[] AssembleShutBarterNegotiations(uint playActSeries) => Bare(playActSeries, ShutBarterNegotiationsOpcode);

    public static byte[] AssembleAppendToBarter(uint playActSeries, uint gearOid, uint barterSocket = 0u)
    {
        return new GameActionScribe(playActSeries, AppendToBarterOpcode, 20).U32(gearOid).U32(barterSocket).Bytes();
    }

    public static byte[] AssembleAdmitBarter(uint playActSeries, uint partnerOid, double barterStamp, uint barterCondition, uint initiatorOid, bool initiatorAccepts, bool partnerAccepts)
    {
        return new GameActionScribe(playActSeries, AdmitBarterOpcode, 48)
            .U32(partnerOid)
            .F64(barterStamp)
            .U32(barterCondition)
            .U32(initiatorOid)
            .Bool32(initiatorAccepts)
            .Bool32(partnerAccepts)
            .U32(0u)
            .U32(0u)
            .Bytes();
    }

    public static byte[] AssembleDeclineBarter(uint playActSeries) => Bare(playActSeries, DeclineBarterOpcode);

    public static byte[] AssembleRestartBarter(uint playActSeries) => Bare(playActSeries, RestartBarterOpcode);

    private static byte[] Bare(uint series, uint act) => new GameActionScribe(series, act, 12).Bytes();
}
