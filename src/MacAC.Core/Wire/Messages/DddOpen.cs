namespace MacAC.Wire.Messages;

public static class DddOpen
{
    public const uint Opcode = 0xF7E7;

    public readonly record struct Parsed(uint ExpectedBytes, uint IterationCount)
    {
        public bool RequiresRefresh => ExpectedBytes is not 0 || IterationCount is not 0;
    }

    public static Parsed Parse(ReadOnlySpan<byte> msg)
    {
        var cursor = new WireCursor(msg);
        if (cursor.Length < 12 || !cursor.Opcode(Opcode))
            throw new InvalidDataException("Not valid data-update announcement");
        uint octets = cursor.U32();
        uint iterations = cursor.U32();
        if (iterations > (uint)(cursor.Left / 20))
            throw new InvalidDataException("Truncated data-update announcement");

        for (uint idx = 0; idx < iterations; ++idx)
        {
            cursor.Skip(12);
            SkipIdents(ref cursor);
            SkipIdents(ref cursor);
        }
        if (cursor.Left is not 0)
            throw new InvalidDataException("Not valid data-update announcement length");
        return new Parsed(octets, iterations);
    }

    private static void SkipIdents(ref WireCursor cursor)
    {
        if (!cursor.Has(4))
            throw new InvalidDataException("Truncated data-update item list");
        uint tally = cursor.U32();
        if (tally > (uint)(cursor.Left / 4))
            throw new InvalidDataException("Truncated data-update item list");
        cursor.Skip((int)tally * 4);
    }
}
