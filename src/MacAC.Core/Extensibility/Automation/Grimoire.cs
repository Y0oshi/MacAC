namespace MacAC.Extensibility.Automation;

public readonly record struct ComponentQuartet(
    uint Herb,
    uint Powder,
    uint Potion,
    uint Talisman);

public readonly record struct SpellFacts(
    uint SpellId,
    string Name,
    uint Family,
    int Tier,
    int Difficulty,
    int ManaCost,
    float DurationSeconds,
    uint School,
    string Description,
    bool IsSelfTargeted,
    bool IsBeneficial)
{
    public bool IsDebuff { get; init; }

    public bool IsOffensive { get; init; }

    public bool IsFellowship { get; init; }

    public bool IsUntargeted { get; init; }

    public bool RequiresPivotTo { get; init; }

    public bool IsMissile { get; init; }

    public bool IsHarmOverTime { get; init; }

    public uint RawFlagSet { get; init; }

    public int ArcanumKind { get; init; }

    public uint TargetMask { get; init; }

    public float BaseSpanConstant { get; init; }

    public float BaseSpanModifier { get; init; }

    public IReadOnlyList<uint> EquationModuleIdents { get; init; } = Array.Empty<uint>();

    public int? FidelityOverride { get; init; }

    public int Quality => FidelityOverride ?? Difficulty;

    public uint IconId { get; init; }

    public string Saying { get; init; } = string.Empty;

    public ComponentQuartet ModuleSet { get; init; }
}

public readonly record struct SpellComponentFacts(
    uint ComponentId,
    uint WeenieClassId,
    string Name,
    double BurnRate,
    uint GestureId,
    double GestureSpeed,
    uint IconId,
    uint SortKey,
    string Type,
    string Word);

/// <summary>The spells the local player knows, plus component and cooldown facts.</summary>
public interface ISpellbook
{
    IReadOnlyList<SpellFacts> RecognizedSelfBuffs { get; }

    IReadOnlyList<SpellFacts> RecognizedAssaultArcana => Array.Empty<SpellFacts>();

    IReadOnlyList<SpellFacts> RecognizedFightingArcana => Array.Empty<SpellFacts>();

    bool IsRecognized(uint arcanumIdent) => false;

    bool TryGet(uint arcanumIdent, out SpellFacts details);

    bool TryFetchModule(uint moduleIdent, out SpellComponentFacts details)
    {
        details = default;
        return false;
    }

    double FetchCooldownLeftover(uint cooldownIdent) => 0d;
}
