namespace MacAC.Wire.Messages;

/// <summary>0x019E: a death message with the victim and killer guids.</summary>
public static class PlayerDeath
{
    public const uint Opcode = 0x019Eu;

    public readonly record struct Parsed(string DeathMessage, uint VictimGuid, uint KillerGuid);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Opcode(Opcode))
            return null;
        try
        {
            string msg = cursor.String16L();
            if (!cursor.Has(8))
                return null;
            uint victim = cursor.U32();
            return new Parsed(msg, victim, cursor.U32());
        }
        catch
        {
            return null;
        }
    }
}
