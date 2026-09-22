using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Traits;

namespace MacAC.Mechanics.Avatar;

public sealed class SelfState
{
    public enum VitalSort
    {
        Health,
        Stamina,
        Mana,
    }

    public enum StatKind
    {
        Strength,
        Endurance,
        Quickness,
        Coordination,
        Focus,
        Self,
    }

    public readonly record struct StatFrame(uint Ranks, uint Start, uint Xp)
    {
        public uint Current => Ranks + Start;
    }

    public readonly record struct VitalFrame(uint Ranks, uint Start, uint Xp, uint Current);

    public readonly record struct SkillFrame(
        uint SkillId,
        uint Ranks,
        uint Status,
        uint Xp,
        uint Init,
        uint Resistance,
        double LastUsed,
        uint FormulaBonus)
    {
        public uint BaseTier => FormulaBonus + Init + Ranks;

        public uint LatestTier => BaseTier;
    }

    private const uint LeapAptitudeIdent = 22u;
    private const uint ExecAptitudeIdent = 24u;

    private readonly VitalFrame?[] _vitals = new VitalFrame?[3];
    private readonly Dictionary<StatKind, StatFrame> _stats = [];
    private readonly Dictionary<uint, SkillFrame> _aptitudes = [];
    private readonly Dictionary<uint, Locus> _loci = [];
    private readonly Grimoire? _grimoire;
    private TraitBundle _traits = new();

    public SelfState(Grimoire? grimoire = null)
    {
        _grimoire = grimoire;
    }

    public event Action<VitalSort>? Changed;

    public event Action<StatKind>? AttributeChanged;

    public event Action? CharacterChanged;

    public Func<uint, IReadOnlyDictionary<uint, uint>, uint>? AptitudeEquationBonusLocator { get; set; }

    public Grimoire? Spellbook => _grimoire;

    public TraitBundle Properties => _traits;

    public IReadOnlyDictionary<uint, SkillFrame> Skills => _aptitudes;

    public IReadOnlyDictionary<uint, Locus> Loci => _loci;

    public float? HealthPercent => Ratio(VitalSort.Health);

    public float? StaminaPercent => Ratio(VitalSort.Stamina);

    public float? ManaPercent => Ratio(VitalSort.Mana);

    public static VitalSort? VitalIdentToSort(uint vitalIdent)
    {
        return vitalIdent switch
        {
            1u or 2u or 7u => VitalSort.Health,
            3u or 4u or 8u => VitalSort.Stamina,
            5u or 6u or 9u => VitalSort.Mana,
            _ => null,
        };
    }

    public static StatKind? AttrIdentToSort(uint atKind) =>
        atKind is >= 1u and <= 6u ? (StatKind)(atKind - 1u) : null;

    public static uint AttrSortToIdent(StatKind sort) =>
        Enum.IsDefined(sort) ? (uint)sort + 1u : 0u;

    public VitalFrame? Get(VitalSort sort) =>
        Enum.IsDefined(sort) ? _vitals[(int)sort] : null;

    public StatFrame? FetchAttr(StatKind sort) =>
        _stats.TryGetValue(sort, out StatFrame cycle) ? cycle : null;

    public SkillFrame? FetchAptitude(uint aptitudeIdent)
    {
        return _aptitudes.TryGetValue(aptitudeIdent, out SkillFrame cycle) ? cycle : null;
    }

    public Locus? FetchLocus(uint locusKind) =>
        _loci.TryGetValue(locusKind, out Locus position) ? position : null;

    public int? FetchNetAttr(StatKind sort)
    {
        if (FetchAttr(sort) is not { Current: var raw })
            return null;
        if (_grimoire is null)
            return (int)raw;
        return EnchantmentRules.EnchantAttr(_grimoire.FetchAttrMod(AttrSortToIdent(sort)), raw);
    }

    public int? FetchNetAptitude(uint aptitudeIdent) => FetchAptitudeVal(aptitudeIdent)?.EffectiveLevel;

    public int FetchAptitudeVitaeModifier(uint aptitudeIdent) => FetchAptitudeVal(aptitudeIdent)?.VitaeModifier ?? 0;

    public SkillRules.Val? FetchAptitudeVal(uint aptitudeIdent, TraitBundle? props = null)
    {
        if (FetchAptitude(aptitudeIdent) is not { } aptitude)
            return null;

        SkillRules.AugBonuses augs = SkillRules.AugBonuses.FromProps(props ?? _traits);
        EnchantmentRules.VitalTweak tweak = _grimoire?.ObtainAptitudeMod(aptitudeIdent) ?? EnchantmentRules.VitalTweak.Persona;
        float vitae = _grimoire is null ? 1f : EnchantmentRules.FetchVitaeMultiplier(_grimoire.ActiveEnchantments);
        int intrinsic = ToInt(aptitude.LatestTier);

        return SkillRules.Derive(
            intrinsic,
            intrinsic + AttrEnchantmentAptitudeDiff(aptitudeIdent),
            aptitudeIdent,
            aptitude.Status,
            augs,
            tweak,
            vitae);
    }

    /// <summary>How much a skill's formula bonus moves once attribute buffs are applied.</summary>
    public int AttrEnchantmentAptitudeDiff(uint aptitudeIdent)
    {
        if (AptitudeEquationBonusLocator is not { } locate
            || _grimoire is null
            || !_aptitudes.TryGetValue(aptitudeIdent, out SkillFrame aptitude))

            return 0;
        uint enchanted = locate(aptitudeIdent, EnchantedAttrCurrentsByIdent());
        return ToInt(enchanted) - ToInt(aptitude.FormulaBonus);
    }

    public IReadOnlyDictionary<uint, uint> AttrCurrentsByIdent()
    {
        var byIdent = new Dictionary<uint, uint>(_stats.Count);
        foreach ((StatKind sort, StatFrame cycle) in _stats)
            byIdent[AttrSortToIdent(sort)] = cycle.Current;
        return byIdent;
    }

    public IReadOnlyDictionary<uint, uint> EnchantedAttrCurrentsByIdent()
    {
        var byIdent = new Dictionary<uint, uint>(_stats.Count);
        foreach (StatKind sort in _stats.Keys)
            byIdent[AttrSortToIdent(sort)] = (uint)Math.Max(0, FetchNetAttr(sort) ?? 0);
        return byIdent;
    }

    /// <summary>Maximum after every enchantment, with the retail floor of 5 (or 1 for tiny pools).</summary>
    public uint? FetchUpperApprox(VitalSort sort)
    {
        if (UpperPriorVitalEnchantments(sort) is not { } unbuffed)
            return null;
        if (unbuffed is 0)
            return 0;

        EnchantmentRules.VitalTweak tweak =
            _grimoire?.FetchVitalMod(UpperVitalStatTag(sort)) ?? EnchantmentRules.VitalTweak.Persona;
        float buffed = unbuffed * tweak.Multiplier + tweak.Additive;
        uint floor = unbuffed >= 5 ? 5u : 1u;
        return (uint)MathF.Max(buffed, floor);
    }

    public uint? FetchBaseUpperApprox(VitalSort sort) => UpperFrom(sort, enchantedAttrs: false);

    public int FetchVitalVitaeModifier(VitalSort sort)
    {
        if (_grimoire is null || FetchBaseUpperApprox(sort) is not { } baseUpper)
            return 0;
        return EnchantmentRules.SkillVitaeModifier(
            EnchantmentRules.FetchVitaeMultiplier(_grimoire.ActiveEnchantments),
            baseUpper);
    }

    public (int RunSkill, int JumpSkill) TravelAptitudeSums()
    {
        return (RawAptitudeTier(ExecAptitudeIdent), RawAptitudeTier(LeapAptitudeIdent));
    }

    public void OnVitalRefresh(uint vitalIdent, uint ranks, uint begin, uint xp, uint latest)
    {
        if (VitalIdentToSort(vitalIdent) is not { } sort)
            return;
        _vitals[(int)sort] = new VitalFrame(ranks, begin, xp, latest);
        Changed?.Invoke(sort);
    }

    public void OnVitalLatest(uint vitalIdent, uint latest)
    {
        if (VitalIdentToSort(vitalIdent) is not { } sort || _vitals[(int)sort] is not { } recognized)
            return;
        _vitals[(int)sort] = recognized with { Current = latest };
        Changed?.Invoke(sort);
    }

    public void OnAttrRefresh(uint atKind, uint ranks, uint begin, uint xp)
    {
        if (AttrIdentToSort(atKind) is not { } sort)
            return;
        _stats[sort] = new StatFrame(ranks, begin, xp);
        RecomputeAptitudeEquationBonuses();
        AttributeChanged?.Invoke(sort);
        switch (sort)
        {
            case StatKind.Endurance:
                Changed?.Invoke(VitalSort.Health);
                Changed?.Invoke(VitalSort.Stamina);
                break;
            case StatKind.Self:
                Changed?.Invoke(VitalSort.Mana);
                break;
        }
        CharacterChanged?.Invoke();
    }

    /// <summary>Replaces the top-level property set from PlayerDescription.</summary>
    public void OnProps(TraitBundle props)
    {
        _traits = props.Clone();
        CharacterChanged?.Invoke();
    }

    public void OnInt64PropRefresh(uint propIdent, long val)
    {
        _traits.Int64s[propIdent] = val;
        CharacterChanged?.Invoke();
    }

    public void OnLoci(IReadOnlyDictionary<uint, Locus> loci)
    {
        _loci.Clear();
        foreach ((uint kind, Locus position) in loci)
            _loci[kind] = position;
        CharacterChanged?.Invoke();
    }

    /// <summary>Applies one PlayerDescription skill row, formula bonus included.</summary>
    public void OnAptitudeRefresh(
        uint aptitudeIdent,
        uint ranks,
        uint condition,
        uint xp,
        uint prime,
        uint resistance,
        double previousConsumed,
        uint equationBonus)
    {
        _aptitudes[aptitudeIdent] = new SkillFrame(aptitudeIdent, ranks, condition, xp, prime, resistance, previousConsumed, equationBonus);
        CharacterChanged?.Invoke();
    }

    /// <summary>Applies a live skill update, deriving the formula bonus locally.</summary>
    public void OnAptitudeWireRefresh(
        uint aptitudeIdent,
        uint ranks,
        uint condition,
        uint xp,
        uint prime,
        uint resistance,
        double previousConsumed)
    {
        uint bonus = AptitudeEquationBonusLocator is { } locate
            ? locate(aptitudeIdent, AttrCurrentsByIdent())
            : _aptitudes.TryGetValue(aptitudeIdent, out SkillFrame recognized) ? recognized.FormulaBonus : 0u;
        OnAptitudeRefresh(aptitudeIdent, ranks, condition, xp, prime, resistance, previousConsumed, bonus);
    }

    public void RecomputeAptitudeEquationBonuses()
    {
        if (AptitudeEquationBonusLocator is not { } locate || _aptitudes.Count is 0)
            return;
        var currents = AttrCurrentsByIdent();
        foreach (uint aptitudeIdent in _aptitudes.Keys.ToArray())
        {
            SkillFrame cycle = _aptitudes[aptitudeIdent];
            uint bonus = locate(aptitudeIdent, currents);
            if (bonus != cycle.FormulaBonus)
                _aptitudes[aptitudeIdent] = cycle with { FormulaBonus = bonus };
        }
    }

    public void Clear()
    {
        Array.Clear(_vitals);
        _stats.Clear();
        _aptitudes.Clear();
        _loci.Clear();
        _traits = new TraitBundle();

        foreach (VitalSort vital in Enum.GetValues<VitalSort>())
            Changed?.Invoke(vital);
        foreach (StatKind stat in Enum.GetValues<StatKind>())
            AttributeChanged?.Invoke(stat);
        CharacterChanged?.Invoke();
    }

    private static uint UpperVitalStatTag(VitalSort sort)
    {
        return sort switch
        {
            VitalSort.Health => EnchantmentRules.StatTag.MaxHealth,
            VitalSort.Stamina => EnchantmentRules.StatTag.MaxStamina,
            VitalSort.Mana => EnchantmentRules.StatTag.MaxMana,
            _ => 0u,
        };
    }

    private uint? UpperPriorVitalEnchantments(VitalSort sort) => UpperFrom(sort, enchantedAttrs: true);

    private uint? UpperFrom(VitalSort sort, bool enchantedAttrs)
    {
        if (Get(sort) is not { } vital)
            return null;
        return vital.Ranks + vital.Start + AttrPortion(sort, enchantedAttrs) + GearBonus(sort);
    }

    private int RawAptitudeTier(uint aptitudeIdent)
    {
        return _aptitudes.TryGetValue(aptitudeIdent, out SkillFrame frame) ? (int)(frame.FormulaBonus + frame.Init + frame.Ranks) : -1;
    }

    private float? Ratio(VitalSort sort)
    {
        if (Get(sort) is not { Current: var latest })
            return null;
        if (FetchUpperApprox(sort) is not { } upper || upper is 0)
            return null;
        return Math.Clamp((float)latest / upper, 0f, 1f);
    }

    private uint AttrPortion(VitalSort sort, bool enchanted)
    {
        switch (sort)
        {
            case VitalSort.Health:
                // SecondaryAttributeTable's formula is floor(Endurance / 2 + 0.5)
                uint endurance = StatLatest(StatKind.Endurance, enchanted);
                return endurance / 2u + (endurance & 1u);
            case VitalSort.Stamina:
                return StatLatest(StatKind.Endurance, enchanted);
            case VitalSort.Mana:
                return StatLatest(StatKind.Self, enchanted);
            default:
                return 0u;
        }
    }

    private uint StatLatest(StatKind sort, bool enchanted)
    {
        if (!_stats.TryGetValue(sort, out StatFrame cycle))
            return 0u;
        if (!enchanted || _grimoire is null)
            return cycle.Current;
        return (uint)Math.Max(0, FetchNetAttr(sort) ?? 0);
    }

    private uint GearBonus(VitalSort sort)
    {
        return sort == VitalSort.Health
            ? (uint)Math.Max(0, _traits.FetchInt((uint)TraitInt.GearMaxHealth))
            : 0u;
    }

    private static int ToInt(uint val) => (int)Math.Min(int.MaxValue, val);
}
