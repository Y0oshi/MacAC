using System.Globalization;
using MacAC.Wire.Messages;

namespace MacAC.Wire;

/// <summary>Retail's salvage-result wording and the material/skill name tables it draws on.</summary>
public static partial class GameEventBindings
{
    private static string ComposeSalvageOutcomes(PlaySignals.SalvageOperationsOutcome outcome)
    {
        string matls = string.Join(", ", outcome.Results.Select(ComposeSalvageMatl));
        string augmentation = outcome.AugmentationBonusPercent is 0
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                " Your augmentation has given you a return bonus of {0}%!",
                outcome.AugmentationBonusPercent);
        return string.Format(
            CultureInfo.InvariantCulture,
            "You obtain {0} using your knowledge of {1}.{2}",
            matls,
            SalvageAptitudeLabel(outcome.SkillId),
            augmentation);
    }

    private static string ComposeSalvageMatl(PlaySignals.SalvageLine outcome)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0} {1} (ws {2:F2})",
            outcome.Units,
            SalvageMatlLabel(outcome.MaterialType),
            outcome.Workmanship);
    }

    private static string SalvageMatlLabel(uint matlKind)
    {
        return matlKind switch
        {
            1u => "Ceramic",
            2u => "Porcelain",
            3u => "Cloth",
            4u => "Linen",
            5u => "Satin",
            6u => "Silk",
            7u => "Velvet",
            8u => "Wool",
            9u => "Gem",
            10u => "Agate",
            11u => "Amber",
            12u => "Amethyst",
            13u => "Aquamarine",
            14u => "Azurite",
            15u => "Black Garnet",
            16u => "Black Opal",
            17u => "Bloodstone",
            18u => "Carnelian",
            19u => "Citrine",
            20u => "Diamond",
            21u => "Emerald",
            22u => "Fire Opal",
            23u => "Green Garnet",
            24u => "Green Jade",
            25u => "Hematite",
            26u => "Imperial Topaz",
            27u => "Jet",
            28u => "Lapis Lazuli",
            29u => "Lavender Jade",
            30u => "Malachite",
            31u => "Moonstone",
            32u => "Onyx",
            33u => "Opal",
            34u => "Peridot",
            35u => "Red Garnet",
            36u => "Red Jade",
            37u => "Rose Quartz",
            38u => "Ruby",
            39u => "Sapphire",
            40u => "Smokey Quartz",
            41u => "Sunstone",
            42u => "Tiger Eye",
            43u => "Tourmaline",
            44u => "Turquoise",
            45u => "White Jade",
            46u => "White Quartz",
            47u => "White Sapphire",
            48u => "Yellow Garnet",
            49u => "Yellow Topaz",
            50u => "Zircon",
            51u => "Ivory",
            52u => "Leather",
            53u => "Armoredillo Hide",
            54u => "Gromnie Hide",
            55u => "Reed Shark Hide",
            56u => "Metal",
            57u => "Brass",
            58u => "Bronze",
            59u => "Copper",
            60u => "Gold",
            61u => "Iron",
            62u => "Pyreal",
            63u => "Silver",
            64u => "Steel",
            65u => "Stone",
            66u => "Alabaster",
            67u => "Granite",
            68u => "Marble",
            69u => "Obsidian",
            70u => "Sandstone",
            71u => "Serpentine",
            72u => "Wood",
            73u => "Ebony",
            74u => "Mahogany",
            75u => "Oak",
            76u => "Pine",
            77u => "Teak",
            _ => "Unknown",
        };
    }

    private static string SalvageAptitudeLabel(uint aptitudeIdent)
    {
        return aptitudeIdent switch
        {
            1u => "Axe",
            2u => "Bow",
            3u => "Crossbow",
            4u => "Dagger",
            5u => "Mace",
            6u => "Melee Defense",
            7u => "Missile Defense",
            8u => "Sling",
            9u => "Spear",
            10u => "Staff",
            11u => "Sword",
            12u => "Thrown Weapon",
            13u => "Unarmed Combat",
            14u => "Arcane Lore",
            15u => "Magic Defense",
            16u => "Mana Conversion",
            17u => "Spellcraft",
            18u => "Item Tinkering",
            19u => "Assess Person",
            20u => "Deception",
            21u => "Healing",
            22u => "Jump",
            23u => "Lockpick",
            24u => "Run",
            25u => "Awareness",
            26u => "Arms And Armor Repair",
            27u => "Assess Creature",
            28u => "Weapon Tinkering",
            29u => "Armor Tinkering",
            30u => "Magic Item Tinkering",
            31u => "Creature Enchantment",
            32u => "Item Enchantment",
            33u => "Life Magic",
            34u => "War Magic",
            35u => "Leadership",
            36u => "Loyalty",
            37u => "Fletching",
            38u => "Alchemy",
            39u => "Cooking",
            40u => "Salvaging",
            41u => "Two Handed Combat",
            42u => "Gearcraft",
            43u => "Void Magic",
            44u => "Heavy Weapons",
            45u => "Light Weapons",
            46u => "Finesse Weapons",
            47u => "Missile Weapons",
            48u => "Shield",
            49u => "Dual Wield",
            50u => "Recklessness",
            51u => "Sneak Attack",
            52u => "Dirty Fighting",
            53u => "Challenge",
            54u => "Summoning",
            _ => "Unknown",
        };
    }
}
