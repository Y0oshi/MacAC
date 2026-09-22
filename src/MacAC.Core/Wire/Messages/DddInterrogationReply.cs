using MacAC.Wire.Packets;

namespace MacAC.Wire.Messages;

public static class DddInterrogationReply
{
    public const uint Opcode = 0xF7E6u;
    public const uint EnglishLanguage = 1u;
    private const int UpperIterations = 100_000;

    public static byte[] Build(DddVersions? versions = null)
    {
        DatagramScribe scribe = new DatagramScribe(16);
        scribe.EmitUInt32(Opcode);
        scribe.EmitUInt32(EnglishLanguage);
        scribe.EmitUInt32(versions.HasValue ? 3u : 0u);
        if (versions is { } have)
        {
            BuildBranch(scribe, have);
        }
        scribe.EmitUInt32(0u);
        scribe.EmitUInt32(0u);
        return scribe.ToArray();
    }

    private static void BuildBranch(DatagramScribe scribe, DddVersions have)
    {
        Iterations(scribe, 0, 1, have.Portal);
        Iterations(scribe, 1, 2, have.Cell);
        Iterations(scribe, 1, 3, have.Language);
    }

    // type, id, count, then either the explicit 1..count list or retail's compressed "-count, 1" range
    // form
    private static void Iterations(DatagramScribe scribe, uint kind, uint ident, int tally)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tally);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tally, UpperIterations);
        scribe.EmitUInt32(kind);
        scribe.EmitUInt32(ident);
        scribe.EmitUInt32((uint)tally);
        if (tally > 2)
        {
            scribe.EmitUInt32(unchecked((uint)-tally));
            scribe.EmitUInt32(1u);
            return;
        }
        for (uint idx = 1; idx <= tally; ++idx)
            scribe.EmitUInt32(idx);
    }
}
