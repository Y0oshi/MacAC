namespace MacAC.Extensibility.Automation;

public enum SkillTraining
{
    Unknown = 0,
    Untrained,
    Trained,
    Specialized,
}

public readonly record struct SkillFacts(
    uint SkillId,
    string Name,
    SkillTraining Training,
    uint Current)
{
    public uint Base { get; init; } = Current;

    public uint IconId { get; init; }
}

/// <summary>One of the six primary attributes; <paramref name="Kind"/> is 0..5.</summary>
public readonly record struct AttributeFacts(
    int Kind,
    string Name,
    uint Current)
{
    /// <summary>The value before any enchantment.</summary>
    public uint Base { get; init; } = Current;
}

public readonly record struct ActiveEnchantmentFacts(
    uint SpellId,
    uint Family,
    int Tier,
    double SecondsRemaining);

/// <summary>The local player, as the host currently understands them.</summary>
public interface ISelfSheet
{
    bool IsInWorld { get; }

    string Name => string.Empty;

    /// <summary>Server-advertised world name; scopes global variables.</summary>
    string RealmLabel => string.Empty;

    /// <summary>Authenticated account name; expression surfaces only ever see its hash.</summary>
    string AcctLabel => string.Empty;

    int ToonOrdinal => -1;

    int Level => 0;

    int PrimaryBundleSpareSockets => 0;

    uint ObjectId { get; }

    uint LatestHealth { get; }

    uint MaxHealth { get; }

    uint LatestStamina { get; }

    uint UpperStamina { get; }

    uint LatestMana { get; }

    uint UpperMana { get; }

    int SummoningMastery => 0;

    IReadOnlyList<SkillFacts> Skills { get; }

    IReadOnlyList<AttributeFacts> Attributes { get; }

    IReadOnlyList<ActiveEnchantmentFacts> EngagedEnchantments { get; }

    bool TryFetchAptitude(uint aptitudeIdent, out SkillFacts aptitude);
}
