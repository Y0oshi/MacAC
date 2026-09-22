namespace MacAC.Wire.Messages;

public static class StackSizeSet
{
    public const uint Opcode = 0x0197u;

    public readonly record struct Parsed(uint Guid, int StackSize, int Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(17) || !cursor.Opcode(Opcode))
            return null;
        cursor.Skip(1); // quality sequence
        return new Parsed(cursor.U32(), cursor.I32(), cursor.I32());
    }
}
