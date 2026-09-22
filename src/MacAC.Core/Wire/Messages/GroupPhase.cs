namespace MacAC.Wire.Messages;

/// <summary>0xF74B: an object's physics-state word changed.</summary>
public static class GroupPhase
{
    public const uint Opcode = 0xF74Bu;

    public readonly record struct Parsed(uint Guid, uint PhysicsState, ushort InstanceSequence, ushort StateSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(16) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U32(), cursor.U32(), cursor.U16(), cursor.U16());
    }
}
