using MacAC.Sim.Play;

namespace MacAC.Sim;

public readonly record struct SimToonCapture(
    long CharacterRevision,
    long SpellbookRevision,
    SimToonOptionsCapture Options,
    SimLocomotionSkillCapture MovementSkills,
    int LearnedSpellCount,
    int ActiveEnchantmentCount,
    int DesiredComponentCount,
    int SkillCount,
    uint SpellbookFilters,
    SimToonTitleCapture Titles = default);

public readonly record struct SimVitalCapture(int Kind, uint Ranks, uint Start, uint Experience, uint Current, uint Maximum);

public readonly record struct SimTraitCapture(int Kind, uint Ranks, uint Start, uint Experience, uint Current);

public readonly record struct SimSkillCapture(
    uint SkillId,
    uint Ranks,
    uint Status,
    uint Experience,
    uint Initial,
    uint Resistance,
    double LastUsed,
    uint FormulaBonus,
    uint CurrentLevel);

/// <summary>The local character sheet: vitals, attributes, skills, spellbook and options.</summary>
public interface ISimToonLens
{
    SimToonCapture Snapshot { get; }

    bool TryFetchVital(int sort, out SimVitalCapture vital);

    bool TryFetchAttr(int sort, out SimTraitCapture attr);

    bool TryFetchSkill(uint aptitudeIdent, out SimSkillCapture aptitude);

    bool KnowsArcanum(uint arcanumIdent);

    bool TryFetchFavorite(int tabOrdinal, int locus, out uint arcanumIdent);

    bool TryFetchWantedModule(uint moduleIdent, out uint quantity);
}
