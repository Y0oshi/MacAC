using MacAC.Wire.Packets;
namespace MacAC.Wire.Messages;

public static class CharacterExit
{
    public const uint Opcode = 0xF653u;

    public static byte[] ConstructReqCorpus(uint toonIdent)
    {
        DatagramScribe scribe = new DatagramScribe(8);
        scribe.EmitUInt32(Opcode);
        scribe.EmitUInt32(toonIdent);
        return scribe.ToArray();
    }

    public static bool IsAck(ReadOnlySpan<byte> corpus)
    {
        return new WireCursor(corpus) is { Length: sizeof(uint) } cursor && cursor.Opcode(Opcode);
    }
}
