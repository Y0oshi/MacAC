using System.Globalization;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public static partial class CreatureAssayRows
{
    public static IReadOnlyList<CreatureAssayRow> Build(
        AppraisalReader.BeastSheet profile,
        bool success)
    {
        return
        [
            Primary("Strength", profile.Strength, 0, profile, success),
            Primary("Endurance", profile.Endurance, 1, profile, success),
            Primary("Coordination", profile.Coordination, 3, profile, success),
            Primary("Quickness", profile.Quickness, 2, profile, success),
            Primary("Focus", profile.Focus, 4, profile, success),
            Primary("Self", profile.Self, 5, profile, success),
            Secondary(
                "Health",
                profile.Health,
                profile.HealthMax,
                unhidePct: true,
                enchantmentBit: 6,
                profile,
                success),
            Secondary(
                "Stamina",
                profile.Stamina,
                profile.StaminaMax,
                unhidePct: false,
                enchantmentBit: 7,
                profile,
                success),
            Secondary(
                "Mana",
                profile.Mana,
                profile.ManaMax,
                unhidePct: false,
                enchantmentBit: 8,
                profile,
                success),
        ];
    }

    public static IReadOnlyList<CreatureAssayRow> AssembleExtra(
        TraitBundle props,
        AppraisalReader.ArmorTier? armorTiers,
        bool toon,
        int ownFactionBitset = 0)
    {
        ArgumentNullException.ThrowIfNull(props);

        var ranks = new List<CreatureAssayRow>();

        if (toon)
        {
            AppendSocietyRank(ranks, props, ownFactionBitset);
            if (Get(props, AllegianceGradeProp) >= 1)
                AppendAllegianceCascade(ranks, props);
        }

        if (toon && armorTiers is { } tiers && HasAnyArmorTier(tiers))
        {
            AssembleExtraBranch(ranks, tiers);
        }

        int harm = Get(props, DamageRating);
        int harmResist = Get(props, DamageResistRating);
        int crit = Get(props, CritRating);
        int critHarm = Get(props, CritDamageRating);
        int critResist = Get(props, CritResistRating);
        int critHarmResist = Get(props, CritDamageResistRating);
        _ = Get(props, HealingBoostRating);
        int dotResist = Get(props, DotResistRating);
        int lifeResist = Get(props, LifeResistRating);

        bool unhideRating = harm > 0 || crit > 0 || critHarm > 0;
        bool unhideResist =
            harmResist > 0 || critResist > 0 || critHarmResist > 0;
        bool unhideDotLife = dotResist > 0 || lifeResist > 0;
        if (unhideRating || unhideResist || unhideDotLife)
        {
            ranks.Add(Blank());
            if (unhideRating)
            {
                ranks.Add(new CreatureAssayRow(
                    "Dmg/CritDmg",
                    $"Rating: {Number(harm)}/{Number(critHarm)}",
                    CreatureAssayValueStyle.Normal));
            }
            if (unhideResist)
            {
                ranks.Add(new CreatureAssayRow(
                    "Dmg/CritDmg",
                    $"Resist: {Number(harmResist)}/{Number(critHarmResist)}",
                    CreatureAssayValueStyle.Normal));
            }
            if (unhideDotLife)
            {
                ranks.Add(new CreatureAssayRow(
                    "DoT/Life:",
                    $"Resist: {Number(dotResist)}/{Number(lifeResist)}",
                    CreatureAssayValueStyle.Normal));
            }
            ranks.Add(Blank());
        }

        if (toon)
        {
            AppendConfigurableExtras(ranks, props);

            ranks.Add(new CreatureAssayRow(
                "* = Unenchantable",
                string.Empty,
                CreatureAssayValueStyle.Normal));
        }

        return ranks;
    }

    private static void AssembleExtraBranch(List<CreatureAssayRow> ranks, AppraisalReader.ArmorTier tiers)
    {
        ranks.Add(Blank());
        ranks.Add(ArmorTierRank(
                        "Head/Chest/Groin", tiers.Head, tiers.Chest, tiers.Abdomen));
        ranks.Add(ArmorTierRank(
                        "Bicep/Wrist/Hand", tiers.UpperArm, tiers.LowerArm, tiers.Hand));
        ranks.Add(ArmorTierRank(
                        "Thigh/Shin/Foot", tiers.UpperLeg, tiers.LowerLeg, tiers.Foot));
    }

    private static string SocietyGradeSuffix(int grade)
    {
        return grade switch
        {
            >= 1 and <= 100 => " ~ Initiate",
            >= 101 and <= 300 => " ~ Adept",
            >= 301 and <= 600 => " ~ Knight",
            >= 601 and <= 1000 => " ~ Lord",
            >= 1001 and <= 1500 => " ~ Master",
            _ => string.Empty,
        };
    }

    private static CreatureAssayValueStyle SocietyTint(
        int markBit, int ownFactionBitset)
    {
        if ((ownFactionBitset & markBit) is not 0)
            return CreatureAssayValueStyle.Positive;
        return (ownFactionBitset & (SocietyBitsetBitmask & ~markBit)) is not 0 ? CreatureAssayValueStyle.Negative : CreatureAssayValueStyle.Normal;
    }

    private static CreatureAssayRow ArmorTierRank(
        string caption, int a, int b, int c)
    {
        return new(
                caption,
                $"AL: {ArmorTierPiece(a)}/{ArmorTierPiece(b)}/{ArmorTierPiece(c)}",
                CreatureAssayValueStyle.Normal);
    }

    private static string ArmorTierPiece(int val)
    {
        return val >= UnenchantableArmorTier
                ? $"*{Number(val - UnenchantableArmorTier)}"
                : Number(val);
    }

    private static CreatureAssayRow Blank() =>
        new(string.Empty, string.Empty, CreatureAssayValueStyle.Normal);

    private static string Number(int val) =>
        val.ToString(CultureInfo.InvariantCulture);

    private static CreatureAssayRow Primary(
        string caption,
        uint? val,
        int enchantmentBit,
        AppraisalReader.BeastSheet profile,
        bool success)
    {
        return new(
                caption,
                val is > 0
                    ? val.Value.ToString(CultureInfo.InvariantCulture)
                    : Unknown,
                Style(enchantmentBit, profile, success));
    }

    private static CreatureAssayRow Secondary(
        string caption,
        uint? latest,
        uint? ceiling,
        bool unhidePct,
        int enchantmentBit,
        AppraisalReader.BeastSheet profile,
        bool success)
    {
        string val = Unknown;
        if (latest is > 0 && ceiling is > 0)
        {
            int pct = RoundedPct(latest.Value, ceiling.Value);
            if (success)
            {
                val = unhidePct
                    ? $"{latest.Value.ToString(CultureInfo.InvariantCulture)}/{ceiling.Value.ToString(CultureInfo.InvariantCulture)} ({pct.ToString(CultureInfo.InvariantCulture)} %)"
                    : $"{latest.Value.ToString(CultureInfo.InvariantCulture)}/{ceiling.Value.ToString(CultureInfo.InvariantCulture)}";
            }
            else if (unhidePct)
            {
                val = $"{pct.ToString(CultureInfo.InvariantCulture)} %";
            }
        }

        return new CreatureAssayRow(
            caption,
            val,
            Style(enchantmentBit, profile, success));
    }

    private static int RoundedPct(uint numerator, uint denominator)
    {
        return denominator is 0u
                ? 0
                : (int)Math.Min(
                    int.MaxValue,
                    ((100L * numerator) + denominator / 2L) / denominator);
    }

    private static CreatureAssayValueStyle Style(
        int bit,
        AppraisalReader.BeastSheet profile,
        bool success)
    {
        if (!success)
            return CreatureAssayValueStyle.Incomplete;

        ushort highlight = profile.AttributeHighlights ?? 0;
        if ((highlight & (1 << bit)) is 0)
            return CreatureAssayValueStyle.Normal;

        ushort tint = profile.AttributeColors ?? 0;
        return (tint & (1 << bit)) is not 0
            ? CreatureAssayValueStyle.Positive
            : CreatureAssayValueStyle.Negative;
    }

    private static bool HasAnyArmorTier(AppraisalReader.ArmorTier tiers)
    {
        return tiers.Head > 0 || tiers.Chest > 0 || tiers.Abdomen > 0
            || tiers.UpperArm > 0 || tiers.LowerArm > 0 || tiers.Hand > 0
            || tiers.UpperLeg > 0 || tiers.LowerLeg > 0 || tiers.Foot > 0;
    }

    private static int Get(TraitBundle props, uint ident) =>
        props.Ints.TryGetValue(ident, out int val) ? val : 0;
}
