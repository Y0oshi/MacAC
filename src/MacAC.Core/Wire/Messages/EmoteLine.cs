namespace MacAC.Wire.Messages;

/// <summary>0x01E0: a typed emote - sender guid, sender name, text.</summary>
public static class EmoteLine
{
    public const uint Opcode = 0x01E0u;

    public readonly record struct Parsed(uint SenderGuid, string SenderName, string Text);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        return EmoteWire.TryParse(corpus, Opcode) is { } e ? new Parsed(e.Guid, e.Name, e.Text) : null;
    }
}

/// <summary>0x01E2: a soul emote - same shape as <see cref="EmoteLine"/>.</summary>
public static class WireSoulEmote
{
    public const uint Opcode = 0x01E2u;

    public readonly record struct Parsed(uint SenderGuid, string SenderName, string Text);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        return EmoteWire.TryParse(corpus, Opcode) is { } e ? new Parsed(e.Guid, e.Name, e.Text) : null;
    }
}

// Shared layout of the two emote messages: guid, String16L name, String16L text
internal static class EmoteWire
{
    public static (uint Guid, string Name, string Text)? TryParse(ReadOnlySpan<byte> corpus, uint opcode)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(8) || !cursor.Opcode(opcode))
            return null;
        try
        {
            uint oid = cursor.U32();
            string label = cursor.String16L();
            return (oid, label, cursor.String16L());
        }
        catch
        {
            return null;
        }
    }
}
