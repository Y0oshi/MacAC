namespace MacAC.Wire.Messages;

public static class SpeechLine
{
    public const uint OwnOpcode = 0x02BBu;
    public const uint RangedOpcode = 0x02BCu;

    public readonly record struct Parsed(string Text, string SenderName, uint SenderGuid, uint ChatType, bool IsRanged, float Range);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus);
        if (!cursor.Has(16))
            return null;
        bool ranged;
        if (cursor.Opcode(OwnOpcode))
            ranged = false;
        else if (cursor.Opcode(RangedOpcode))
            ranged = true;
        else
            return null;

        try
        {
            string phrase = cursor.String16L();
            string sender = cursor.String16L();
            if (!cursor.Has(ranged ? 12 : 8))
                return null;
            uint oid = cursor.U32();
            float span = ranged ? cursor.F32() : 0f;
            return new Parsed(phrase, sender, oid, cursor.U32(), ranged, span);
        }
        catch
        {
            return null;
        }
    }
}
