namespace MacAC.Mechanics.Arcana;

public static class EnchantmentRules
{
    private const uint MultiplyBin = 1u;
    private const uint AppendBin = 2u;
    private const uint VitaeBin = 4u;

    public readonly record struct VitalTweak(float Multiplier, float Additive)
    {
        /// <summary>(1.0, 0.0): nothing in force.</summary>
        public static readonly VitalTweak Persona = new(1.0f, 0.0f);
    }

    [Flags]
    public enum EnchantmentBit : uint
    {
        Attribute = 0x0000001,

        /// <summary>A vital maximum; EnchantAttribute2nd applies vitae first.</summary>
        SecondAtt = 0x0000002,

        /// <summary>A skill; EnchantSkill applies vitae first.</summary>
        Skill = 0x0000010,
    }

    public static class StatTag
    {
        public const uint MaxHealth = 1;
        public const uint MaxStamina = 3;
        public const uint MaxMana = 5;
    }

    public static VitalTweak FetchMod(
        IEnumerable<LiveEnchantmentRow> enchantments,
        ArcanumChart chart,
        uint statTag,
        EnchantmentBit? neededKind = null,
        bool includeVitae = true)
    {
        float multiplier = 1.0f;
        float additive = 0.0f;
        float vitae = 1.0f;

        foreach (LiveEnchantmentRow ench in StrongestPerClan(enchantments, chart))
        {
            if (ench.StatModValue is not { } val)
                continue;

            if (ench.Bucket == VitaeBin)
            {
                if (includeVitae)
                    vitae *= val;
                continue;
            }

            if (ench.StatModKey != statTag)
                continue;
            if (neededKind is { } need && (ench.StatModType.GetValueOrDefault() & (uint)need) is 0)
                continue;

            switch (ench.Bucket)
            {
                case MultiplyBin:
                    multiplier *= val;
                    break;
                case AppendBin:
                    additive += val;
                    break;
            }
        }

        multiplier *= vitae;
        return multiplier == 1.0f && additive == 0.0f ? VitalTweak.Persona : new VitalTweak(multiplier, additive);
    }

    public static int EnchantAttr(VitalTweak mod, uint baseVal)
    {
        float val = baseVal * mod.Multiplier + mod.Additive;
        float floor = baseVal < 10u ? 1f : 10f;
        return (int)MathF.Max(val, floor);
    }

    public static int EnchantAptitude(VitalTweak mod, uint baseVal)
    {
        float val = baseVal * mod.Multiplier + mod.Additive;
        return (int)(val < 0.5f ? 0f : val);
    }

    public static float FetchVitaeMultiplier(IEnumerable<LiveEnchantmentRow> enchantments)
    {
        ArgumentNullException.ThrowIfNull(enchantments);
        float vitae = 1.0f;
        foreach (LiveEnchantmentRow ench in enchantments)
        {
            if (ench.Bucket == VitaeBin && ench.StatModValue is { } val)
                vitae *= val;
        }
        return vitae;
    }

    public static int SkillVitaeModifier(IEnumerable<LiveEnchantmentRow> enchantments, uint baseVal) =>
        SkillVitaeModifier(FetchVitaeMultiplier(enchantments), baseVal);

    public static int SkillVitaeModifier(float vitaeMultiplier, uint baseVal)
    {
        return vitaeMultiplier == 1.0f ? 0 : (int)(baseVal * vitaeMultiplier) - (int)baseVal;
    }

    public static VitalTweak FetchAptitudeMod(IEnumerable<LiveEnchantmentRow> enchantments, ArcanumChart chart, uint aptitudeIdent) =>
        FetchMod(enchantments, chart, aptitudeIdent, EnchantmentBit.Skill);

    // Keeps the highest spell id in each family
    private static IEnumerable<LiveEnchantmentRow> StrongestPerClan(
        IEnumerable<LiveEnchantmentRow> enchantments,
        ArcanumChart chart)
    {
        var strongest = new Dictionary<uint, LiveEnchantmentRow>();
        foreach (LiveEnchantmentRow ench in enchantments)
        {
            if (!chart.TryGet(ench.SpellId, out SpellMeta meta))
            {
                if (ench.Bucket == VitaeBin)
                    AnnounceDroppedVitae(ench);
                continue;
            }
            uint clan = meta.Family is 0 ? ench.LayerId | 0x80000000u : meta.Family;
            if (!strongest.TryGetValue(clan, out LiveEnchantmentRow pinned) || ench.SpellId > pinned.SpellId)
                strongest[clan] = ench;
        }
        return strongest.Values;
    }

    private static void AnnounceDroppedVitae(in LiveEnchantmentRow ench)
    {
        string val = ench.StatModValue is { } v
            ? v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
            : "NULL";
        Console.WriteLine(FormattableString.Invariant(
            $"[stat-chain] VITAE DROPPED by spell-table lookup: spell={ench.SpellId} value={val}"));
    }
}
