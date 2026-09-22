namespace MacAC.Mechanics.Genesis;

public enum GenesisHeritage : uint
{
    Invalid = 0,
    Aluvian = 1,
    Gharundim = 2,
    Sho = 3,
    Viamontian = 4,
    Shadowbound = 5,
    Gearknight = 6,
    Tumerok = 7,
    Lugian = 8,
    Empyrean = 9,
    Penumbraen = 10,
    Undead = 11,
    Olthoi = 12,
    OlthoiAcid = 13,
}

public sealed record GenesisHeritageOptions(
    uint HeritageId,
    string Name,
    uint IconId,
    uint SetupId,
    uint EnvironmentSetupId,
    uint AttributeCredits,
    uint SkillCredits,
    IReadOnlyList<int> PrimaryStartAreaIndices,
    IReadOnlyList<int> SecondaryStartAreaIndices,
    IReadOnlyDictionary<uint, GenesisSkillPrice> SkillCostsBySkillId,
    IReadOnlyList<GenesisTemplate> Templates,
    IReadOnlyDictionary<int, GenesisSexOptions> GendersByKey)
{
    public bool IsOlthoi
    {
        get
        {
            return (GenesisHeritage)HeritageId is GenesisHeritage.Olthoi or GenesisHeritage.OlthoiAcid;
        }
    }
}
