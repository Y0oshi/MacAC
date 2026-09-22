namespace MacAC.Wire.Messages;

/// <summary>GameActions that spend XP or credits on the character, and the combat-mode switch.</summary>
public static class ToonActs
{
    public const uint GameActionEnvelope = GameActionScribe.Envelope;
    public const uint EmitAttrOpcode = 0x0045u;
    public const uint EmitVitalOpcode = 0x0044u;
    public const uint EmitAptitudeOpcode = 0x0046u;
    public const uint TrainAptitudeOpcode = 0x0047u;
    public const uint EditFightingMannerOpcode = 0x0053u;

    [Flags]
    public enum FightingMode : uint
    {
        Undef = 0,
        NonCombat = 0x01,
        Melee = 0x02,
        Missile = 0x04,
        Magic = 0x08,
        ValidCombat = NonCombat | Melee | Missile | Magic,
        CombatCombat = Melee | Missile | Magic,
    }

    public static byte[] AssembleEmitAttr(uint seq, uint attrIdent, ulong xpSpent) => Spend(seq, EmitAttrOpcode, attrIdent, xpSpent);

    public static byte[] AssembleEmitVital(uint seq, uint vitalIdent, ulong xpSpent) => Spend(seq, EmitVitalOpcode, vitalIdent, xpSpent);

    public static byte[] AssembleEmitAptitude(uint seq, uint aptitudeIdent, ulong xpSpent) => Spend(seq, EmitAptitudeOpcode, aptitudeIdent, xpSpent);

    public static byte[] AssembleTrainAptitude(uint seq, uint aptitudeIdent, uint credits)
    {
        return new GameActionScribe(seq, TrainAptitudeOpcode, 20).U32(aptitudeIdent).U32(credits).Bytes();
    }

    public static byte[] AssembleEditFightingManner(uint seq, FightingMode manner)
    {
        return new GameActionScribe(seq, EditFightingMannerOpcode, 16).U32((uint)manner).Bytes();
    }

    // The wire only carries the low 32 bits of the XP amount
    private static byte[] Spend(uint seq, uint act, uint ident, ulong xp) =>
        new GameActionScribe(seq, act, 20).U32(ident).U32((uint)xp).Bytes();
}
