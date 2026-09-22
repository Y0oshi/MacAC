namespace MacAC.Wire.Messages;

/// <summary>0x0024: a guid left the player's inventory view.</summary>
public static class InventoryDrop
{
    public const uint Opcode = 0x0024u;

    public readonly record struct Parsed(uint Guid);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(8) || !cursor.Opcode(Opcode))
            return null;
        return new Parsed(cursor.U32());
    }
}
