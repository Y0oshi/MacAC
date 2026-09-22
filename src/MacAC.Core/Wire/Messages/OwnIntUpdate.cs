namespace MacAC.Wire.Messages;

/// <summary>0x02CD: one int trait on the player. opcode(4) + sequence(1) + property(4) + value(4).</summary>
public static class OwnIntUpdate
{
    public const uint Opcode = 0x02CDu;

    public readonly record struct Parsed(uint Property, int Value);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(13) || !cursor.Opcode(Opcode))
            return null;
        cursor.Skip(1); // quality sequence
        return new Parsed(cursor.U32(), cursor.I32());
    }
}
