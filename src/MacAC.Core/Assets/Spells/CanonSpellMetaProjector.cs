using System.Globalization;
using System.Text;
using MacAC.Mechanics.Arcana;
using MacAC.Dat;
using DatSchool =  MacAC.Dat.SpellSchool;
using CoreSchool = MacAC.Mechanics.Arcana.MechMagicSchool;

namespace MacAC.Assets;

public static class CanonSpellMetaProjector
{
    private const int EquationLen = 8;

    public static SpellMeta Project(uint arcanumIdent, SpellSpec arcanum, ComponentBook? moduleChart)
    {
        ArgumentNullException.ThrowIfNull(arcanum);

        uint[] equation = arcanum.Components.Take(EquationLen).ToArray();
        uint flagSet = (uint)arcanum.Bits;
        bool Mark(SpellBits bit) => (flagSet & (uint)bit) is not 0u;

        uint equationMarkKind = CanonSpellFormula.FetchTargetingKind(equation);
        int tier = CanonSpellFormula.InqArcanumTierByRoughHeuristic(equation);
        bool hostile = !Mark(SpellBits.Beneficial) && !Mark(SpellBits.SelfTargeted);

        return new SpellMeta(
            arcanumIdent,
            arcanum.Name,
            SchoolLabel(arcanum.School),
            (uint)arcanum.Category,
            arcanum.Icon,
            ArcanumWords(equation, moduleChart),
            checked((float)arcanum.Duration),
            checked((int)arcanum.BaseMana),
            Mark(SpellBits.Reversed),
            Mark(SpellBits.FellowshipSpell),
            arcanum.Description,
            unchecked((int)arcanum.DisplayOrder),
            checked((int)arcanum.Power),
            flagSet,
            tier,
            Mark(SpellBits.FastCast),
            hostile,
            equationMarkKind is 0u,
            Speed: 0f,
            (uint)arcanum.CasterEffect,
            (uint)arcanum.TargetEffect,
            equationMarkKind,
            checked((int)arcanum.Kind))
        {
            SchoolIdent = ToCoreSchool(arcanum.School),
            EquationModules = equation,
            Saying = Saying(arcanum.Name, equation, moduleChart),
            ComponentSet = ModuleSet(equation, moduleChart),
            EquationVer = arcanum.FormulaVersion,
            ModuleLoss = arcanum.ComponentLoss,
            BaseRangeConstant = arcanum.BaseRangeConstant,
            BaseRangeModifier = arcanum.BaseRangeMod,
            ArcanumEconomyModifier = arcanum.SpellEconomyMod,
            FizzleFx = (uint)arcanum.FizzleEffect,
            RecoveryInterval = arcanum.RecoveryInterval,
            RecoveryQuantity = arcanum.RecoveryAmount,
            NonModuleMarkKind = (uint)arcanum.NonComponentTargetType,
            EquationMarkKind = equationMarkKind,
            ManaModifier = arcanum.ManaMod,
            DowngradeModifier = arcanum.DegradeModifier,
            DowngradeThreshold = arcanum.DegradeLimit,
            GatewayLifespan = arcanum.PortalLifetime,
        };
    }

    private static string SchoolLabel(DatSchool school)
    {
        return school switch
        {
            DatSchool.WarMagic => "War Magic",
            DatSchool.LifeMagic => "Life Magic",
            DatSchool.ItemEnchantment => "Item Enchantment",
            DatSchool.CreatureEnchantment => "Creature Enchantment",
            DatSchool.VoidMagic => "Void Magic",
            _ => "None",
        };
    }

    private static CoreSchool ToCoreSchool(DatSchool school)
    {
        return school switch
        {
            DatSchool.WarMagic => CoreSchool.WarMagic,
            DatSchool.LifeMagic => CoreSchool.LifeMagic,
            DatSchool.ItemEnchantment => CoreSchool.ItemEnchantment,
            DatSchool.CreatureEnchantment => CoreSchool.CreatureEnchantment,
            DatSchool.VoidMagic => CoreSchool.VoidMagic,
            _ => CoreSchool.None,
        };
    }

    // The component words run together; one retail spell has a hand-written saying
    private static string Saying(string arcanumLabel, IReadOnlyList<uint> equation, ComponentBook? moduleChart)
    {
        if (arcanumLabel == "Curse of Raven Fury")
            return "tugakquati";
        if (moduleChart is null)
            return string.Empty;

        StringBuilder words = new StringBuilder();
        foreach (uint moduleIdent in equation)
        {
            if (moduleChart.Components.TryGetValue(moduleIdent, out ComponentSpec? component))
                words.Append(component.Text);
        }
        return words.ToString();
    }

    private static ComponentBundle ModuleSet(IReadOnlyList<uint> equation, ComponentBook? moduleChart)
    {
        if (moduleChart is null)
            return default;

        uint herb = 0u, powder = 0u, potion = 0u, talisman = 0u;
        foreach (uint moduleIdent in equation)
        {
            if (!moduleChart.Components.TryGetValue(moduleIdent, out ComponentSpec? component))
                continue;
            switch (component.Kind)
            {
                case ComponentKind.Herb: herb = moduleIdent; break;
                case ComponentKind.Powder: powder = moduleIdent; break;
                case ComponentKind.Potion: potion = moduleIdent; break;
                case ComponentKind.Talisman: talisman = moduleIdent; break;
            }
        }
        return new ComponentBundle(herb, powder, potion, talisman);
    }

    // "Herb Powderpotion": the herb word, then the powder and lower-cased potion words fused and
    // capitalised
    private static string ArcanumWords(IReadOnlyList<uint> equation, ComponentBook? moduleChart)
    {
        if (moduleChart is null)
            return string.Empty;

        string herb = string.Empty, powder = string.Empty, potion = string.Empty;
        foreach (uint moduleIdent in equation)
        {
            if (!moduleChart.Components.TryGetValue(moduleIdent, out ComponentSpec? component))
                continue;
            switch (component.Kind)
            {
                case ComponentKind.Herb: herb = component.Text; break;
                case ComponentKind.Powder: powder = component.Text; break;
                case ComponentKind.Potion: potion = component.Text; break;
            }
        }

        string rear = powder + potion.ToLower(CultureInfo.InvariantCulture);
        if (rear.Length is not 0)
            rear = char.ToUpperInvariant(rear[0]) + rear[1..];
        return $"{herb} {rear}".Trim();
    }
}
