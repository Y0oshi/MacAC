using MacAC.Dat;
using MacAC.Mechanics.Genesis;

namespace MacAC.Client.Link;

internal static class CanonSkillFormula
{
    public static bool TryCalculate(
        SkillFormula equation,
        uint attribute1,
        uint attribute2,
        out uint outcome)
    {
        ArgumentNullException.ThrowIfNull(equation);

        return TryDerive(
            equation.AdditiveBonus,
            equation.Attribute1Multiplier,
            equation.Attribute2Multiplier,
            equation.Divisor,
            attribute1,
            attribute2,
            out outcome);
    }

    public static uint CalculateChargenScore(
        SkillSpec aptitudeBase,
        uint attribute1,
        uint attribute2,
        GenesisSkillTrack tier)
    {
        ArgumentNullException.ThrowIfNull(aptitudeBase);

        return !TryCalculate(aptitudeBase.Formula, attribute1, attribute2, out uint outcome)
            ? 0u
            : tier switch
            {
                GenesisSkillTrack.Trained => outcome + 5u,
                GenesisSkillTrack.Specialized => outcome + 10u,
                _ => outcome,
            };
    }

    public static uint CalculateChargenScore(
        GenesisSkillDetail aptitudeSpecifics,
        uint attribute1,
        uint attribute2,
        GenesisSkillTrack tier)
    {
        var equation = aptitudeSpecifics.Formula;
        return !TryDerive(
                equation.AdditiveBonus,
                equation.Attribute1Multiplier,
                equation.Attribute2Multiplier,
                equation.Divisor,
                attribute1,
                attribute2,
                out uint outcome)
            ? 0u
            : tier switch
            {
                GenesisSkillTrack.Trained => outcome + 5u,
                GenesisSkillTrack.Specialized => outcome + 10u,
                _ => outcome,
            };
    }

    public static string AttrLabel(AttributeId attr)
    {
        return attr switch
        {
            AttributeId.Strength => "Strength",
            AttributeId.Endurance => "Endurance",
            AttributeId.Quickness => "Quickness",
            AttributeId.Coordination => "Coordination",
            AttributeId.Focus => "Focus",
            AttributeId.Self => "Self",
            _ => string.Empty,
        };
    }

    public static string? ComposeEquation(SkillFormula equation)
    {
        ArgumentNullException.ThrowIfNull(equation);

        uint x = unchecked((uint)equation.Attribute1Multiplier);
        uint y = unchecked((uint)equation.Attribute2Multiplier);
        uint w = unchecked((uint)equation.AdditiveBonus);
        uint divisor = unchecked((uint)equation.Divisor);

        bool hasAttr1 = x >= 1 && equation.Attribute1 != 0;
        bool hasAttr2 = y >= 1 && equation.Attribute2 != 0;
        if (!hasAttr1 && !hasAttr2)
            return null;

        var phrase = new System.Text.StringBuilder("( ");
        if (hasAttr1 && hasAttr2)
            phrase.Append('(');

        if (hasAttr1)
        {
            ComposeEquationBranch(equation, phrase, x, hasAttr2);
        }

        if (hasAttr2)
        {
            string name2 = AttrLabel(equation.Attribute2);
            phrase.Append(y <= 1
                ? name2
                : $"({y} x {name2})");
        }

        if (hasAttr1 && hasAttr2)
            phrase.Append(')');
        if (divisor is not 1)
            phrase.Append($" / {divisor}");
        if (w is not 0)
            phrase.Append($"+{w}");
        phrase.Append(" )");
        return phrase.ToString();
    }

    private static void ComposeEquationBranch(SkillFormula equation, System.Text.StringBuilder phrase, uint x, bool hasAttr2)
    {
        string name1 = AttrLabel(equation.Attribute1);
        phrase.Append(x <= 1
                        ? name1
                        : $"({x} x {name1})");
        if (hasAttr2)
            phrase.Append(" + ");
    }

    public static string? AssembleHint(SkillSpec aptitudeBase)
    {
        ArgumentNullException.ThrowIfNull(aptitudeBase);

        string? equation = ComposeEquation(aptitudeBase.Formula);
        string blurb = aptitudeBase.Description ?? string.Empty;
        string hint = (equation is null ? string.Empty : equation + "\n") + blurb;
        return hint.Length is 0 ? null : hint;
    }

    private static bool TryDerive(
        int additiveBonus,
        int attribute1Multiplier,
        int attribute2Multiplier,
        int divisorDepot,
        uint attribute1,
        uint attribute2,
        out uint outcome)
    {
        uint divisor = unchecked((uint)divisorDepot);
        if (divisor is 0u)
        {
            outcome = 0u;
            return false;
        }

        uint x = unchecked((uint)attribute1Multiplier);
        uint y = unchecked((uint)attribute2Multiplier);
        uint w = unchecked((uint)additiveBonus);
        uint numerator = unchecked(x * attribute1 + y * attribute2 + w);
        outcome = (uint)Math.Floor((double)numerator / divisor + 0.5d);
        return true;
    }
}

internal sealed class OnlineSkillCreditPicker(SkillBook? aptitudeChart)
{
    public uint Resolve(
        uint aptitudeIdent,
        IReadOnlyDictionary<uint, uint> attrCurrents)
    {
        ArgumentNullException.ThrowIfNull(attrCurrents);

        if (aptitudeChart?.Skills is null
            || !aptitudeChart.Skills.TryGetValue(
                (SkillId)aptitudeIdent,
                out var aptitudeBase))

            return 0u;

        SkillFormula equation = aptitudeBase.Formula;
        attrCurrents.TryGetValue(
            (uint)equation.Attribute1,
            out uint attribute1);
        attrCurrents.TryGetValue(
            (uint)equation.Attribute2,
            out uint attribute2);
        return CanonSkillFormula.TryCalculate(
            equation,
            attribute1,
            attribute2,
            out uint outcome)
            ? outcome
            : 0u;
    }
}

internal sealed class ChargenSkillScorePicker(GenesisOptions knobs)
{
    public uint Resolve(
        uint aptitudeIdent,
        GenesisAttributeSpread attrs,
        GenesisSkillTrack tier)
    {
        if (!knobs.TryFetchAptitudeSpecifics(aptitudeIdent, out GenesisSkillDetail aptitudeSpecifics))

            return 0u;

        uint attribute1 = LocateAttr(aptitudeSpecifics.Formula.Attribute1, attrs);
        uint attribute2 = LocateAttr(aptitudeSpecifics.Formula.Attribute2, attrs);
        return CanonSkillFormula.CalculateChargenScore(aptitudeSpecifics, attribute1, attribute2, tier);
    }

    private static uint LocateAttr(
        uint attrIdent,
        GenesisAttributeSpread attrs)
    {
        return attrIdent switch
        {
            1u => (uint)Math.Max(0, attrs.Strength),
            2u => (uint)Math.Max(0, attrs.Endurance),
            3u => (uint)Math.Max(0, attrs.Quickness),
            4u => (uint)Math.Max(0, attrs.Coordination),
            5u => (uint)Math.Max(0, attrs.Focus),
            6u => (uint)Math.Max(0, attrs.Self),
            _ => 0u,
        };
    }
}
