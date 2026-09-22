namespace MacAC.Wire.Messages;

public static class ObjectDeletion
{
    public const uint Opcode = 0xF747u;

    public readonly record struct Parsed(uint Guid, ushort InstanceSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(10) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U32(), cursor.U16());
    }
}
