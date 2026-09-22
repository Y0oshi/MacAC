namespace MacAC.Wire.Messages;

public readonly record struct GameEventParcel(uint PlayerGuid, uint Sequence, GameEventKind EventType, ReadOnlyMemory<byte> Payload)
{
    public const uint Opcode = 0xF7B0u;
    public const int PreambleDims = 16;

    public static GameEventParcel? TryParse(byte[] corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        return TryDecodeBorrowed(corpus);
    }

    // Receive-path parser: the payload is a view into corpus and must not outlive the dispatch
    internal static GameEventParcel? TryDecodeBorrowed(ReadOnlyMemory<byte> corpus)
    {
        WireCursor cursor = new WireCursor(corpus.Span);
        if (!cursor.Has(PreambleDims) || !cursor.Opcode(Opcode))
            return null;
        uint oid = cursor.U32();
        uint series = cursor.U32();
        GameEventKind sort = (GameEventKind)cursor.U32();
        return new GameEventParcel(oid, series, sort, corpus.Slice(PreambleDims));
    }
}
