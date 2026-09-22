using MacAC.Wire.Packets;
namespace MacAC.Wire.Messages;

public static class CharacterRevive
{
    public const uint ReqOpcode = 0xF7D9u;
    public const uint ResponseOpcode = 0xF643u;

    public readonly record struct Parsed(uint VerificationFlag, uint? Guid, string? Name, uint? SecondsGreyedOut)
    {
        public bool IsOk => VerificationFlag is 1u;
    }

    public static byte[] BuildReqBody(uint toonOid)
    {
        DatagramScribe scribe = new DatagramScribe(8);
        scribe.EmitUInt32(ReqOpcode);
        scribe.EmitUInt32(toonOid);
        return scribe.ToArray();
    }

    public static Parsed Parse(ReadOnlySpan<byte> corpus)
    {
        var verdict = GenesisVerdict.Parse(corpus);
        return new Parsed(verdict.RawCode, verdict.Guid, verdict.Name, verdict.SecondsGreyedOut);
    }
}
