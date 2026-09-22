namespace MacAC.Wire.Messages;

public static class OwnVitalUpdate
{
    public const uint WholeOpcode = 0x02E7u;
    public const uint LatestOpcode = 0x02E9u;

    public readonly record struct DecodedWhole(byte Sequence, uint VitalId, uint Ranks, uint Start, uint Xp, uint Current);

    public readonly record struct DecodedLatest(byte Sequence, uint VitalId, uint Current);

    public static DecodedWhole? TryDecodeWhole(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(25) || !cursor.Opcode(WholeOpcode))
            return null;
        return new DecodedWhole(cursor.U8(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32(), cursor.U32());
    }

    public static DecodedLatest? TryDecodeLatest(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(13) || !cursor.Opcode(LatestOpcode))
            return null;
        return new DecodedLatest(cursor.U8(), cursor.U32(), cursor.U32());
    }
}
