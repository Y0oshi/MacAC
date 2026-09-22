namespace MacAC.Mechanics.Genesis;

public sealed record GenesisTemplate(
    string Name,
    uint IconId,
    uint TitleStringId,
    GenesisAttributeSpread Attributes,
    IReadOnlyList<uint> NormalSkills,
    IReadOnlyList<uint> PrimarySkills);
