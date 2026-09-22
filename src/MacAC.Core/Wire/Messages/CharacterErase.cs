using MacAC.Wire.Packets;
namespace MacAC.Wire.Messages;

/// <summary>0xF655 both ways: delete-by-slot request; the bare opcode back is the acknowledgement.</summary>
public static class CharacterErase
{
    public const uint Opcode = 0xF655u;

    public static byte[] AssembleReqCorpus(string acctLabel, uint toonSocket)
    {
        ArgumentNullException.ThrowIfNull(acctLabel);
        DatagramScribe scribe = new DatagramScribe(32);
        scribe.EmitUInt32(Opcode);
        scribe.EmitString16L(acctLabel);
        scribe.EmitUInt32(toonSocket);
        return scribe.ToArray();
    }

    public static bool IsAcknowledgement(ReadOnlySpan<byte> corpus)
    {
        return new WireCursor(corpus) is { Length: sizeof(uint) } cursor && cursor.Opcode(Opcode);
    }
}
