namespace MacAC.Wire.Messages;

/// <summary>0x02CF: one int64 trait on the player. opcode(4) + sequence(1) + property(4) + value(8).</summary>
public static class OwnInt64Update
{
    public const uint Opcode = 0x02CFu;
    public const int CorpusDims = 17;

    public readonly record struct Parsed(uint Property, long Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(CorpusDims) || !cursor.Opcode(Opcode))
            return null;
        cursor.Skip(1); // quality sequence
        return new Parsed(cursor.U32(), cursor.Idx64());
    }
}
