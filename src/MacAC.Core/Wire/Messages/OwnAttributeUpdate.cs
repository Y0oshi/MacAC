namespace MacAC.Wire.Messages;

/// <summary>0x02E3: one of the player's primary attributes. opcode(4) + sequence(1) + 4 × u32.</summary>
public static class OwnAttributeUpdate
{
    public const uint Opcode = 0x02E3u;

    public readonly record struct Parsed(byte Sequence, uint AttributeId, uint Ranks, uint Start, uint Xp);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(21) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U8(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32());
    }
}
