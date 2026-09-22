using MacAC.Wire.Packets;
namespace MacAC.Wire.Messages;

public static class CharacterEntry
{
    public const uint JoinRealmReqOpcode = 0xF7C8u;
    public const uint JoinRealmOpcode = 0xF657u;

    public static byte[] AssembleJoinRealmReqCorpus()
    {
        DatagramScribe scribe = new DatagramScribe(8);
        scribe.EmitUInt32(JoinRealmReqOpcode);
        return scribe.ToArray();
    }

    public static byte[] AssembleJoinRealmCorpus(uint toonOid, string acctLabel)
    {
        ArgumentNullException.ThrowIfNull(acctLabel);
        DatagramScribe scribe = new DatagramScribe(32);
        scribe.EmitUInt32(JoinRealmOpcode);
        scribe.EmitUInt32(toonOid);
        scribe.EmitString16L(acctLabel);
        return scribe.ToArray();
    }
}
