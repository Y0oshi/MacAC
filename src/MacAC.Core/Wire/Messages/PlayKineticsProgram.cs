namespace MacAC.Wire.Messages;

/// <summary>0xF754: play a physics script (by DID) on an object. Exactly 12 bytes.</summary>
public readonly record struct PlayKineticsProgram(uint Guid, uint ScriptDid)
{
    public const uint Opcode = 0xF754u;
    public const int WireDims = 12;

    public static PlayKineticsProgram? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (cursor.Length != WireDims || !cursor.Opcode(Opcode))
            return null;
        return new PlayKineticsProgram(cursor.U32(), cursor.U32());
    }
}
