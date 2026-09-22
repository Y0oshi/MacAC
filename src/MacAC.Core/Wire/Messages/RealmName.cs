namespace MacAC.Wire.Messages;

/// <summary>0xF7E1: the world's name and connection counts, sent at character select.</summary>
public static class RealmName
{
    public const uint Opcode = 0xF7E1u;

    public readonly record struct Parsed(int CurrentConnections, int MaxConnections, string WorldName);

    public static Parsed Parse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        uint opcode = cursor.Word();
        if (opcode != Opcode)
            throw new FormatException($"wanted RealmName opcode 0x{Opcode:X4}, got 0x{opcode:X8}");
        int latest = unchecked((int)cursor.Word());
        int upper = unchecked((int)cursor.Word());
        return new Parsed(latest, upper, cursor.String16L());
    }
}
