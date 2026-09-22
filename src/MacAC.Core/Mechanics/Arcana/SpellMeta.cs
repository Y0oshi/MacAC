namespace MacAC.Mechanics.Arcana;

/// <summary>One spell's metadata, as merged from the CSV export and the DAT.</summary>
public sealed record SpellMeta(
    uint SpellId,
    string Name,
    string School,
    uint Family,
    uint IconId,
    string SpellWords,
    float Duration,
    int ManaCost,
    bool IsDebuff,
    bool IsFellowship,
    string Description,
    int SortKey,
    int Difficulty,
    uint Flags,
    int Generation,
    bool IsFastWindup,
    bool IsOffensive,
    bool IsUntargeted,
    float Speed,
    uint CasterEffect,
    uint TargetEffect,
    uint TargetMask,
    int SpellType)
{
    public string Saying { get; init; } = string.Empty;

    public ComponentBundle ComponentSet { get; init; }

    public MechMagicSchool SchoolIdent { get; init; }

    public IReadOnlyList<uint> EquationModules { get; init; } = [];

    public uint EquationVer { get; init; }

    public float ModuleLoss { get; init; }

    public float BaseRangeConstant { get; init; }

    public float BaseRangeModifier { get; init; }

    public float ArcanumEconomyModifier { get; init; }

    public uint FizzleFx { get; init; }

    public double RecoveryInterval { get; init; }

    public float RecoveryQuantity { get; init; }

    public uint NonModuleMarkKind { get; init; }

    public uint EquationMarkKind { get; init; }

    public uint ManaModifier { get; init; }

    public float DowngradeModifier { get; init; }

    public float DowngradeThreshold { get; init; }

    public double GatewayLifespan { get; init; }

    public bool IsSelfTargeted => Has(SpellBits.SelfTargeted);

    public bool IsBeneficial => Has(SpellBits.Beneficial);

    public bool IsProjectile => Has(SpellBits.Projectile);

    private bool Has(SpellBits bit) => (Flags & (uint)bit) is not 0;
}

/// <summary>The four non-scarab, non-taper components; ordered so spells sort by ingredients.</summary>
public readonly record struct ComponentBundle(
    uint Herb,
    uint Powder,
    uint Potion,
    uint Talisman) : IComparable<ComponentBundle>
{
    public int CompareTo(ComponentBundle another)
    {
        int c = Herb.CompareTo(another.Herb);
        if (c is 0) c = Powder.CompareTo(another.Powder);
        if (c is 0) c = Potion.CompareTo(another.Potion);
        if (c is 0) c = Talisman.CompareTo(another.Talisman);
        return c;
    }
}
