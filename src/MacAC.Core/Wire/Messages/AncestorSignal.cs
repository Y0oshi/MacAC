namespace MacAC.Wire.Messages;

/// <summary>0xF749: a child object was attached to a parent at a location/placement.</summary>
public static class AncestorSignal
{
    public const uint Opcode = 0xF749u;

    public readonly record struct Parsed(uint ParentGuid, uint ChildGuid, uint ParentLocation, uint PlacementId, ushort ParentInstanceSequence, ushort ChildPositionSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(24) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U16(), cursor.U16());
    }
}
