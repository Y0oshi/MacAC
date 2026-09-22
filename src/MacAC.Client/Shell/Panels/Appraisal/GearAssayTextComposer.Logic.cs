using System.Globalization;
using System.Text;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public static partial class GearAssayTextComposer
{
    private static readonly (uint Requirement, uint Stat, uint Difficulty)[]
        WieldRequirements =
        [
            (158u, 159u, 160u),
            (270u, 271u, 272u),
            (273u, 274u, 275u),
            (276u, 277u, 278u),
        ];

    public static string Build(
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal,
        Func<uint, SpellMeta?> locateArcanum,
        CanonAssayNamePicker? labels = null)
        => AssembleDossier(objRef, appraisal, locateArcanum, labels).ToString();

    public static GearAssayDigest AssembleDossier(
        ClientThing objRef,
        AppraisalReader.WireParsed appraisal,
        Func<uint, SpellMeta?> locateArcanum,
        CanonAssayNamePicker? labels = null)
    {
        ArgumentNullException.ThrowIfNull(objRef);
        ArgumentNullException.ThrowIfNull(locateArcanum);
        labels ??= CanonAssayNamePicker.Empty;

        TraitBundle props = appraisal.Properties;
        CanonDigestAssembler dossier = new CanonDigestAssembler();

        RevealValAndBurden(dossier, props);
        RevealTinkering(dossier, props);
        RevealSetAndRatings(dossier, props);
        RevealWeaponAndArmor(dossier, objRef, appraisal);
        RevealDefenseModifiers(dossier, appraisal);
        RevealArmorModifiers(dossier, appraisal);
        RevealShortMagicDetails(dossier, appraisal, locateArcanum);
        RevealSpecialProps(dossier, props, labels);
        RevealUsage(dossier, props);
        RevealTierThresholds(dossier, props);
        RevealWieldRequirements(dossier, props, labels);
        RevealUsageThresholds(dossier, props);
        RevealGearTier(dossier, props);
        RevealActivationRequirements(
            dossier,
            props,
            appraisal.Success,
            labels);
        RevealInvokerBlob(dossier, appraisal);
        RevealBoostAndHealing(dossier, objRef, appraisal);
        RevealCapAndMutex(dossier, objRef, appraisal);
        RevealManaStone(dossier, appraisal);
        RevealLeftoverUses(dossier, objRef, appraisal);
        RevealCraftsman(dossier, props);
        RevealSaleAndRareDetails(dossier, props);
        RevealMagicDetails(dossier, appraisal, locateArcanum);
        RevealBlurb(dossier, props, labels);

        return dossier.Build();
    }

    internal static string AptitudeLabel(int aptitude)
    {
        return aptitude switch
        {
            1 => "Axe",
            2 => "Bow",
            3 => "Crossbow",
            4 => "Dagger",
            5 => "Mace",
            6 => "Melee Defense",
            7 => "Missile Defense",
            8 => "Sling",
            9 => "Spear",
            10 => "Staff",
            11 => "Sword",
            12 => "Thrown Weapon",
            13 => "Unarmed Combat",
            14 => "Arcane Lore",
            15 => "Magic Defense",
            16 => "Mana Conversion",
            17 => "Spellcraft",
            18 => "Item Tinkering",
            19 => "Person Appraisal",
            20 => "Deception",
            21 => "Healing",
            22 => "Jump",
            23 => "Lockpick",
            24 => "Run",
            25 => "Awareness",
            26 => "Armor Repair",
            27 => "Creature Appraisal",
            28 => "Weapon Tinkering",
            29 => "Armor Tinkering",
            30 => "Magic Item Tinkering",
            31 => "Creature Enchantment",
            32 => "Item Enchantment",
            33 => "Life Magic",
            34 => "War Magic",
            35 => "Leadership",
            36 => "Loyalty",
            37 => "Fletching",
            38 => "Alchemy",
            39 => "Cooking",
            40 => "Salvaging",
            41 => "Two Handed Combat",
            42 => "Gearcraft",
            43 => "Void Magic",
            44 => "Heavy Weapons",
            45 => "Light Weapons",
            46 => "Finesse Weapons",
            47 => "Missile Weapons",
            49 => "Dual Wield",
            50 => "Recklessness",
            51 => "Sneak Attack",
            52 => "Dirty Fighting",
            53 => "Challenge",
            54 => "Summoning",
            _ => $"Skill {aptitude.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static void AffixImbuedFxList(
        List<string> props,
        uint imbuedFxList)
    {
        (uint Flag, string Text)[] labels =
        [
            (0x0000_0001u, "Critical Strike"),
            (0x0000_0002u, "Crippling Blow"),
            (0x0000_0004u, "Armor Rending"),
            (0x0000_0008u, "Slash Rending"),
            (0x0000_0010u, "Pierce Rending"),
            (0x0000_0020u, "Bludgeon Rending"),
            (0x0000_0040u, "Acid Rending"),
            (0x0000_0080u, "Cold Rending"),
            (0x0000_0100u, "Lightning Rending"),
            (0x0000_0200u, "Fire Rending"),
            (0x0000_0400u, "+1 Melee Defense"),
            (0x0000_0800u, "+1 Missile Defense"),
            (0x0000_1000u, "+1 Magic Defense"),
            (0x0000_4000u, "Nether Rending"),
            (0x8000_0000u, "Phantasmal"),
        ];
        foreach ((uint bit, string phrase) in labels)
            if ((imbuedFxList & bit) is not 0)
                props.Add(phrase);
    }

    private static string RequirementFidelity(
        int requirement,
        int stat,
        int difficulty,
        CanonAssayNamePicker labels)
    {
        string baseStem = requirement is 2 or 4 or 6 ? "base " : string.Empty;
        return requirement switch
        {
            1 or 2 or 8 => baseStem + AptitudeLabel(stat),
            3 or 4 => baseStem + PrimaryAttrLabel(stat),
            5 or 6 => baseStem + SecondaryAttrLabel(stat),
            7 => "level",
            9 or 10 => stat switch
            {
                0x11F => "Standing with the Celestial Hand",
                0x120 => "Standing with the Eldrytch Web",
                0x121 => "Standing with the Radiant Blood",
                _ => "unknown quality",
            },
            11 => labels.LocateBeast(difficulty),
            12 => labels.LocateLineage(difficulty),
            _ => string.Empty,
        };
    }

    private static string ClothingCoverage(uint precedence)
    {
        List<string> coverage = new List<string>();
        if ((precedence & 0x4000u) is not 0u)
            coverage.Add("Head");
        if ((precedence & (0x0008u | 0x0400u)) is not 0u)
            coverage.Add("Chest");
        if ((precedence & (0x0010u | 0x0800u)) is not 0u)
            coverage.Add("Abdomen");
        if ((precedence & (0x0020u | 0x1000u)) is not 0u)
            coverage.Add("Upper Arms");
        if ((precedence & (0x0040u | 0x2000u)) is not 0u)
            coverage.Add("Lower Arms");
        if ((precedence & 0x0080u) is not 0u)
            coverage.Add("Hands");
        if ((precedence & (0x0002u | 0x0100u)) is not 0u)
            coverage.Add("Upper Legs");
        if ((precedence & (0x0004u | 0x0200u)) is not 0u)
            coverage.Add("Lower Legs");
        if ((precedence & 0x10000u) is not 0u)
            coverage.Add("Feet");
        return string.Join(", ", coverage);
    }

    private static long GearTierToSumXp(
        int tier,
        long baseExperience,
        int ceilingTier,
        int styling)
    {
        int cappedTier = Math.Clamp(tier, 0, ceilingTier);
        return styling switch
        {
            1 => baseExperience * cappedTier,
            2 => TotalGeometricExperience(baseExperience, cappedTier),
            3 => baseExperience * cappedTier * (cappedTier + 1L) / 2L,
            _ => 0L,
        };
    }

    private static int GearSumXpToTier(
        long sumExperience,
        long baseExperience,
        int ceilingTier,
        int styling)
    {
        if (sumExperience <= 0 || baseExperience <= 0 || ceilingTier <= 0)
            return 0;
        if (styling is 1)
        {
            return (int)Math.Min(
                ceilingTier,
                sumExperience / baseExperience);
        }

        long leftover = sumExperience;
        long upcomingPrice = baseExperience;
        int tier = 0;
        while (tier < ceilingTier && leftover >= upcomingPrice)
        {
            leftover -= upcomingPrice;
            ++tier;
            if (styling is 2)
                upcomingPrice *= 2L;
            else if (styling is 3)
                upcomingPrice = baseExperience * (tier + 1L);
            else
                return 0;
        }
        return tier;
    }

    private static long TotalGeometricExperience(
        long baseExperience,
        int tiers)
    {
        long sum = 0;
        long price = baseExperience;
        for (int idx = 0; idx < tiers; ++idx)
        {
            sum += price;
            price *= 2L;
        }
        return sum;
    }

    private static string PluralizedGemLabel(int matlKind, string matl)
    {
        if (matlKind == 0x26)
            return "Rubies";
        if (matlKind is 0x0B or 0x18 or 0x1B or 0x1D or 0x20
            or 0x25 or 0x28 or 0x2E or 0x24 or 0x2D)

            return $"pieces of {matl}";
        return matlKind is 0x1A or 0x31 ? matl + "es" : matlKind == 0x1C ? matl : matl + "s";
    }

    private static string? EquipmentSetLabel(int setIdent)
    {
        if (setIdent is >= 94 and <= 129)
        {
            string tier = ((setIdent - 94) / 12) switch
            {
                0 => "Minor",
                1 => "Major",
                _ => "Blackfire",
            };
            int participant = (setIdent - 94) % 12;
            string fx = (participant % 4) switch
            {
                0 => "Stinging",
                1 => "Sparking",
                2 => "Smoldering",
                _ => "Shivering",
            };
            string soul = (participant / 4) switch
            {
                0 => "Shrouded Soul",
                1 => "Darkened Mind",
                _ => "Clouded Spirit",
            };
            return $"{tier} {fx} {soul}";
        }

        return setIdent switch
        {
            4 => "Carraida's Benediction",
            5 => "Noble Relic",
            6 => "Ancient Relic",
            7 => "Alduressa Relic",
            8 => "Shou-jen",
            9 => "Empyrean Rings",
            10 => "Arm, Mind, Heart",
            11 => "Coat of Perfect Light",
            12 => "Leggings of Perfect Light",
            13 => "Soldier's",
            14 => "Adept's",
            15 => "Archer's",
            16 => "Defender's",
            17 => "Tinker's",
            18 => "Crafter's",
            19 => "Hearty",
            20 => "Dexterous",
            21 => "Wise",
            22 => "Swift",
            23 => "Hardened",
            24 => "Reinforced",
            25 => "Interlocking",
            26 => "Flame Proof",
            27 => "Acid Proof",
            28 => "Cold Proof",
            29 => "Lightning Proof",
            30 => "Dedication",
            31 => "Gladiatorial Clothing",
            32 => "Ceremonial Clothing",
            33 => "Protective Clothing",
            35 => "Sigil of Defense",
            36 => "Sigil of Destruction",
            37 => "Sigil of Fury",
            38 => "Sigil of Growth",
            39 => "Sigil of Vigor",
            40 => "Heroic Protector",
            41 => "Heroic Destroyer",
            49 => "Weave of Alchemy",
            50 => "Weave of Arcane Lore",
            51 => "Weave of Armor Tinkering",
            52 => "Weave of Assess Person",
            53 or 67 or 74 or 75 or 79 => "Weave of Light Weapons",
            54 or 57 or 77 => "Weave of Missile Weapons",
            55 => "Weave of Cooking",
            56 => "Weave of Creature Enchantment",
            58 => "Weave of Finesse Weapons",
            59 => "Weave of Deception",
            60 => "Weave of Fletching",
            61 => "Weave of Healing",
            62 => "Weave of Item Enchantment",
            63 => "Weave of Item Tinkering",
            64 => "Weave of Leadership",
            65 => "Weave of Life Magic",
            66 => "Weave of Loyalty",
            68 => "Weave of Magic Defense",
            69 => "Weave of Magic Item Tinkering",
            70 => "Weave of Mana Conversion",
            71 => "Weave of Melee Defense",
            72 => "Weave of Missile Defense",
            73 => "Weave of Salvaging",
            76 => "Weave of Heavy Weapons",
            78 => "Weave of Two Handed Combat",
            80 => "Weave of Void Magic",
            81 => "Weave of War Magic",
            82 => "Weave of Weapon Tinkering",
            83 => "Weave of Assess Creature",
            84 => "Weave of Dirty Fighting",
            85 => "Weave of Dual Wield",
            86 => "Weave of Recklessness",
            87 => "Weave of Shield",
            88 => "Weave of Sneak Attack",
            89 => "Shou-jen Shozoku",
            90 => "Weave of Summoning",
            91 => "Shrouded Soul",
            92 => "Darkened Mind",
            93 => "Clouded Spirit",
            130 => "Shimmering Shadows",
            _ => null,
        };
    }

    private static string WeaponMomentLabel(int weaponMoment)
    {
        return weaponMoment switch
        {
            < 11 => "Very Fast",
            < 31 => "Fast",
            < 50 => "Average",
            < 80 => "Slow",
            _ => "Very Slow",
        };
    }

    private static string WorkmanshipAdjective(int workmanship)
    {
        return workmanship switch
        {
            <= 0 => string.Empty,
            1 => "Poorly crafted",
            2 => "Well-crafted",
            3 => "Finely crafted",
            4 => "Exquisitely crafted",
            5 => "Magnificent",
            6 => "Nearly flawless",
            7 => "Flawless",
            8 => "Utterly flawless",
            9 => "Incomparable",
            _ => "Priceless",
        };
    }

    private static string? LockpickDifficulty(int successPct)
    {
        return successPct switch
        {
            < 0 => null,
            0 => "impossible",
            < 5 => "ridiculously difficult",
            < 15 => "extremely difficult",
            < 35 => "quite difficult",
            < 50 => "difficult",
            < 70 => "challenging",
            < 85 => "mildly challenging",
            < 95 => "easy",
            _ => "trivial",
        };
    }

    private static GearAssayFontStyle EnchantmentStyling(
        (ushort Highlight, ushort Color)? encoded,
        uint loBit)
    {
        if (encoded is not { } halves)
            return GearAssayFontStyle.Normal;

        uint bitfield = halves.Highlight | ((uint)halves.Color << 16);
        return (bitfield & loBit) is 0u
            ? GearAssayFontStyle.Normal
            : (bitfield & (loBit << 16)) is not 0u
            ? GearAssayFontStyle.Beneficial
            : GearAssayFontStyle.Detrimental;
    }

    private static string HarmKindLabel(uint kind)
    {
        return TryHarmKindLabel(kind, out string? label)
                ? label!
                : $"type {kind.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string WeaponSubtype(int kind)
    {
        return kind switch
        {
            1 => " (Unarmed Weapon)",
            2 => " (Sword)",
            3 => " (Axe)",
            4 => " (Mace)",
            5 => " (Spear)",
            6 => " (Dagger)",
            7 => " (Staff)",
            8 => " (Bow)",
            9 => " (Crossbow)",
            10 => " (Thrown)",
            _ => string.Empty,
        };
    }

    private static string UsageAptitudeLabel(int aptitude)
    {
        string label = AptitudeLabel(aptitude);
        return label.StartsWith("Skill ", StringComparison.Ordinal)
            ? "Unknown Skill"
            : label;
    }

    private static string PrimaryAttrLabel(int attr)
    {
        return attr switch
        {
            1 => "Strength",
            2 => "Endurance",
            3 => "Quickness",
            4 => "Coordination",
            5 => "Focus",
            6 => "Self",
            _ => $"Attribute {attr.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string SecondaryAttrLabel(int attr)
    {
        return attr switch
        {
            1 => "Max Health",
            2 => "Health",
            3 => "Max Stamina",
            4 => "Stamina",
            5 => "Max Mana",
            6 => "Mana",
            _ => $"Vital {attr.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private static string AssembleArcanumBlurbChunk(
        string bearing,
        IReadOnlyList<(uint Id, SpellMeta? Metadata)> arcana)
    {
        StringBuilder phrase = new StringBuilder(bearing);
        foreach ((uint ident, SpellMeta? metadata) in arcana)
        {
            string label = !string.IsNullOrWhiteSpace(metadata?.Name)
                ? metadata.Name
                : $"Spell {ident.ToString(CultureInfo.InvariantCulture)}";
            phrase.Append("\n~ ").Append(label).Append(": ");
            if (!string.IsNullOrWhiteSpace(metadata?.Description))
                phrase.Append(metadata.Description);
        }
        return phrase.ToString();
    }

    private static void AppendRequirement(
        List<string> requirements,
        string label,
        int val)
    {
        if (val > 0)
            requirements.Add(
                $"{label}: {val.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string ComposeCanonHarm(double harm)
    {
        return harm.ToString(
                harm > 10d ? "G4" : "G3",
                CultureInfo.InvariantCulture);
    }

    private static string ComposeExpiry(int leftoverSecs)
    {
        int leftover = Math.Max(0, leftoverSecs);
        StringBuilder phrase = new StringBuilder();
        if (leftover > 31_536_000)
        {
            phrase.Append(leftover / 31_536_000).Append(" years, ");
            leftover %= 31_536_000;
        }
        if (leftover > 86_400)
        {
            phrase.Append(leftover / 86_400).Append(" days, ");
            leftover %= 86_400;
        }
        if (leftover > 3_600)
        {
            phrase.Append(leftover / 3_600).Append(" hours, ");
            leftover %= 3_600;
        }
        if (leftover > 60)
        {
            phrase.Append(leftover / 60).Append(" minutes, ");
            leftover %= 60;
        }
        phrase.Append(leftover).Append(" seconds.");
        return phrase.ToString();
    }

    private static string ComposeDiffMoment(double secs)
    {
        int leftover = Math.Max(0, (int)Math.Truncate(secs));
        int months = leftover / 2_592_000;
        leftover %= 2_592_000;
        int days = leftover / 86_400;
        leftover %= 86_400;
        int hours = leftover / 3_600;
        leftover %= 3_600;
        int minutes = leftover / 60;
        int finalSecs = leftover % 60;

        StringBuilder outcome = new StringBuilder();
        if (months is not 0)
            outcome.Append(months).Append("mo ");
        if (days is not 0)
            outcome.Append(days).Append("d ");
        if (hours is not 0)
            outcome.Append(hours).Append("h ");
        if (minutes is not 0)
            outcome.Append(minutes).Append("m ");
        outcome.Append(finalSecs).Append("s ");
        return outcome.ToString();
    }

    private static string ComposeModifier(double modifier)
        => ComposeSignedPct(modifier - 1d);

    private static string ComposeSignedPct(double modifier)
        => modifier.ToString("+0%;-0%;0%", CultureInfo.InvariantCulture);

    private static bool TryHarmKindLabel(uint kind, out string? label)
    {
        label = kind switch
        {
            1u => "Slashing",
            2u => "Piercing",
            4u => "Bludgeoning",
            8u => "Cold",
            16u => "Fire",
            32u => "Acid",
            64u => "Electric",
            128u => "Health",
            256u => "Stamina",
            512u => "Mana",
            1024u => "Nether",
            _ => null,
        };
        return label is not null;
    }

    private static string DropLead(string val, string drop)
    {
        int ordinal = val.IndexOf(drop, StringComparison.Ordinal);
        return ordinal < 0
            ? val
            : val.Remove(ordinal, drop.Length);
    }
}
