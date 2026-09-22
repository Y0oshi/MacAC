namespace MacAC.Wire.Messages;

/// <summary>GameActions 0x0008 (melee), 0x000A (missile) and 0x01B7 (cancel).</summary>
public static class AttackAsk
{
    public const uint GameActEnvelope = GameActionScribe.Envelope;
    public const uint TargetedMeleeAssaultOpcode = 0x0008u;
    public const uint TargetedMissileAssaultOpcode = 0x000Au;
    public const uint AbortAssaultOpcode = 0x01B7u;

    public static byte[] AssembleMelee(uint playActSeries, uint markOid, uint assaultHeight, float strengthTier)
    {
        return Swing(playActSeries, TargetedMeleeAssaultOpcode, markOid, assaultHeight, strengthTier);
    }

    public static byte[] AssembleMissile(uint playActSeries, uint markOid, uint assaultHeight, float accuracyTier)
    {
        return Swing(playActSeries, TargetedMissileAssaultOpcode, markOid, assaultHeight, accuracyTier);
    }

    public static byte[] AssembleAbort(uint playActSeries) => new GameActionScribe(playActSeries, AbortAssaultOpcode, 12).Bytes();

    private static byte[] Swing(uint series, uint act, uint mark, uint height, float tier)
    {
        return new GameActionScribe(series, act, 24).U32(mark).U32(height).F32(tier).Bytes();
    }
}
