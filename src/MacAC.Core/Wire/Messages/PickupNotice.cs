namespace MacAC.Wire.Messages;

/// <summary>0xF74A: an object was picked up - it leaves the 3-D world but stays a logical object.</summary>
public static class PickupNotice
{
    public const uint Opcode = 0xF74Au;

    public readonly record struct Parsed(uint Guid, ushort InstanceSequence, ushort PositionSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(12) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U32(), cursor.U16(), cursor.U16());
    }
}
