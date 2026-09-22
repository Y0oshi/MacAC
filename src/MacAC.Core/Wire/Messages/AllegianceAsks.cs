namespace MacAC.Wire.Messages;

public static class AllegianceAsks
{
    public const uint PlayActEnvelope = GameActionScribe.Envelope;
    public const uint SwearOpcode = 0x001Du;
    public const uint BreakOpcode = 0x001Eu;
    public const uint AllegianceRefreshReqOpcode = 0x001Fu;

    public static byte[] AssembleSwear(uint playActSeries, uint patronOid) => One(playActSeries, SwearOpcode, patronOid);

    public static byte[] AssembleBreak(uint playActSeries, uint markOid) => One(playActSeries, BreakOpcode, markOid);

    /// <summary>Kicking a vassal is a break issued against them.</summary>
    public static byte[] AssembleKick(uint playActSeries, uint vassalOid) => One(playActSeries, BreakOpcode, vassalOid);

    public static byte[] AssembleAllegianceRefreshReq(uint playActSeries, bool on) => One(playActSeries, AllegianceRefreshReqOpcode, on ? 1u : 0u);

    private static byte[] One(uint series, uint act, uint word) => new GameActionScribe(series, act, 16).U32(word).Bytes();
}
