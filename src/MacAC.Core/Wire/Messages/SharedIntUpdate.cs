namespace MacAC.Wire.Messages;

public static class SharedIntUpdate
{
    public const uint Opcode = 0x02CEu;

    public readonly record struct Parsed(uint Guid, uint Property, int Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(17) || !cursor.Opcode(Opcode))
            return null;
        cursor.Skip(1); // quality sequence
        return new Parsed(cursor.U32(), cursor.U32(), cursor.I32());
    }
}
