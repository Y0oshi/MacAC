namespace MacAC.Wire.Messages;

/// <summary>0xF755: play a physics script by type with an intensity. Exactly 16 bytes.</summary>
public readonly record struct PlayKineticsProgramKind(uint Guid, uint RawScriptType, float Intensity)
{
    public const uint Opcode = 0xF755u;
    public const int WireSize = 16;

    public static PlayKineticsProgramKind? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (cursor.Length != WireSize || !cursor.Opcode(Opcode))
            return null;
        return new PlayKineticsProgramKind(cursor.U32(), cursor.U32(), cursor.F32());
    }
}
