namespace MacAC.Wire.Messages;

/// <summary>GameActions 0x0048 (untargeted) and 0x004A (targeted) spell casts.</summary>
public static class CastAsk
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint UntargetedSubOpcode = 0x0048u;
    public const uint TargetedSubOpcode = 0x004Au;

    public static byte[] AssembleUntargeted(uint playActSeries, uint arcanumIdent)
    {
        return new GameActionScribe(playActSeries, UntargetedSubOpcode, 16).U32(arcanumIdent).Bytes();
    }

    public static byte[] AssembleTargeted(uint playActSeries, uint markOid, uint arcanumIdent)
    {
        return new GameActionScribe(playActSeries, TargetedSubOpcode, 20).U32(markOid).U32(arcanumIdent).Bytes();
    }
}
