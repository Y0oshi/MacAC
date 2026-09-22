namespace MacAC.Wire.Messages;

/// <summary>0xF7E0: a server-authored line for the chat window, with its text type.</summary>
public static class ServerLine
{
    public const uint Opcode = 0xF7E0u;

    public readonly record struct Parsed(string Message, uint ChatType);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(8) || !cursor.Opcode(Opcode))
            return null;
        try
        {
            string msg = cursor.String16L();
            return cursor.Has(4) ? new Parsed(msg, cursor.U32()) : null;
        }
        catch
        {
            return null;
        }
    }
}
