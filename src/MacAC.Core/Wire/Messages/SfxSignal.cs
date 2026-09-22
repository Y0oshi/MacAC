namespace MacAC.Wire.Messages;

/// <summary>0xF750: play a sound on an object. At least 16 bytes.</summary>
public readonly record struct SfxSignal(uint Guid, uint SoundType, float Volume)
{
    public const uint Opcode = 0xF750u;
    public const int WireSize = 16;

    public static SfxSignal? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(WireSize) || !cursor.Opcode(Opcode))
            return null;
        return new SfxSignal(cursor.U32(), cursor.U32(), cursor.F32());
    }
}
