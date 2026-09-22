using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace MacAC.Mechanics.Genesis;

/// <summary>The whole character-generation table from the portal DAT.</summary>
public sealed record GenesisOptions(
    IReadOnlyList<GenesisStartArea> StarterAreas,
    IReadOnlyDictionary<uint, GenesisHeritageOptions> HeritagesById,
    IReadOnlyDictionary<uint, GenesisSkillPrice> GlobalSkillCostsBySkillId,
    IReadOnlyDictionary<uint, GenesisSkillDetail>? GlobalSkillDetailsBySkillId = null)
{
    public static GenesisOptions Empty { get; } = new(
        [],
        FrozenDictionary<uint, GenesisHeritageOptions>.Empty,
        FrozenDictionary<uint, GenesisSkillPrice>.Empty);

    public bool TryFetchLineage(uint lineageIdent, [MaybeNullWhen(false)] out GenesisHeritageOptions lineage) =>
        HeritagesById.TryGetValue(lineageIdent, out lineage);

    public bool TryFetchStarterArea(int ordinal, [MaybeNullWhen(false)] out GenesisStartArea area)
    {
        bool recognized = (uint)ordinal < (uint)StarterAreas.Count;
        area = recognized ? StarterAreas[ordinal] : null;
        return recognized;
    }

    public bool TryFetchAptitudeSpecifics(uint aptitudeIdent, [MaybeNullWhen(false)] out GenesisSkillDetail specifics)
    {
        if (GlobalSkillDetailsBySkillId is { } particulars)
            return particulars.TryGetValue(aptitudeIdent, out specifics);
        specifics = default;
        return false;
    }
}
