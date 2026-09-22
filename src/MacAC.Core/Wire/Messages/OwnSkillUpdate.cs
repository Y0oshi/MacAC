namespace MacAC.Wire.Messages;

public static class OwnSkillUpdate
{
    public const uint Opcode = 0x02DDu;

    /// <summary>Ranks are widened from the wire's u16.</summary>
    public readonly record struct Parsed(byte Sequence, uint SkillId, uint Ranks, ushort AdjustPP, uint AdvancementClass, uint Xp, uint Init, uint Resistance, double LastUsed);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(37) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U8(), cursor.U32(), cursor.U16(), cursor.U16(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.F64());
    }
}
