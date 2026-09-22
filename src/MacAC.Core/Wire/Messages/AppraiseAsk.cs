namespace MacAC.Wire.Messages;

/// <summary>GameAction 0x00C8: appraise (identify) an object.</summary>
public static class AppraiseAsk
{
    public const uint PlayActionEnvelope = GameActionScribe.Envelope;
    public const uint SubOpcode = 0x00C8u;

    public static byte[] Build(uint playActSeries, uint markOid)
    {
        return new GameActionScribe(playActSeries, SubOpcode, 16).U32(markOid).Bytes();
    }
}
