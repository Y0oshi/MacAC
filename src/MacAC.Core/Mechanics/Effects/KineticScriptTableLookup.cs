using MacAC.Dat;

namespace MacAC.Mechanics.Effects;

public sealed class KineticScriptTableLookup(Func<uint, EffectBook?> loadTable)
{
    private const uint ProgramChunk = 0x33000000u;
    private const uint ChartChunk = 0x34000000u;
    private const uint ChunkBitmask = 0xFF000000u;

    private readonly Func<uint, EffectBook?> _pullChart = loadTable ?? throw new ArgumentNullException(nameof(loadTable));

    public uint? Resolve(uint chartDid, uint rawProgramKind, float intensity) =>
        Resolve(chartDid, rawProgramKind, intensity, out _);

    /// <summary>Null for a missing table or type, an out-of-range intensity, or a malformed script id.</summary>
    public uint? Resolve(uint chartDid, uint rawProgramKind, float intensity, out Exception? pullMiss)
    {
        pullMiss = null;
        if (!IsKineticsProgramChartDid(chartDid))
            return null;

        EffectBook? chart;
        try
        {
            chart = _pullChart(chartDid);
        }
        catch (Exception problem)
        {
            pullMiss = problem;
            return null;
        }

        if (chart is null || chart.Id != chartDid
            || !chart.Entries.TryGetValue(unchecked((EffectId)rawProgramKind), out EffectChoices? blob))
            return null;

        foreach (EffectChoice listing in blob.Choices)
        {
            if (intensity <= listing.Mod)
            {
                uint programDid = listing.ScriptId;
                return IsKineticsProgramDid(programDid) ? programDid : null;
            }
        }
        return null;
    }

    /// <summary>DAT ids carry the record type in the high byte; the other 24 bits are the index.</summary>
    public static bool IsKineticsProgramDid(uint did) => (did & ChunkBitmask) == ProgramChunk;

    public static bool IsKineticsProgramChartDid(uint did) => (did & ChunkBitmask) == ChartChunk;
}
