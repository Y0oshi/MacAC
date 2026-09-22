namespace MacAC.Wire.Messages;

public static class ObjDescNotice
{
    public const uint Opcode = 0xF625u;

    public readonly record struct Parsed(uint Guid, ObjectCreation.SchemeBlob ModelData, ushort InstanceSequence, ushort ObjDescSequence);

    public static Parsed? TryParse(ReadOnlySpan<byte> corpus)
    {
        try
        {
            var cursor = new WireCursor(corpus);
            if (!cursor.Opcode(Opcode) || !cursor.Has(4))
                return null;
            uint oid = cursor.U32();
            var model = ObjectCreation.ScanModelBlob(ref cursor);
            if (cursor.Left is not 4)
                return null;
            return new Parsed(oid, model, cursor.U16(), cursor.U16());
        }
        catch
        {
            return null;
        }
    }
}
