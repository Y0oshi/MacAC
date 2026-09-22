namespace MacAC.Mechanics.Genesis;

public readonly record struct GenesisAttributeSpread(
    int Strength,
    int Endurance,
    int Coordination,
    int Quickness,
    int Focus,
    int Self)
{
    public int Total => Strength + Endurance + Coordination + Quickness + Focus + Self;

    public bool All(Func<int, bool> test)
    {
        return test(Strength) && test(Endurance) && test(Coordination)
        && test(Quickness) && test(Focus) && test(Self);
    }
}

public static class GenesisAttributeRules
{
    public const int AttrLower = 10;

    public const int AttrUpper = 100;

    public static int LeftoverCredits(uint attrCreditAllowance, GenesisAttributeSpread vals) =>
        checked((int)attrCreditAllowance) - vals.Total;

    public static bool IsFullySpent(uint attrCreditAllowance, GenesisAttributeSpread vals) =>
        LeftoverCredits(attrCreditAllowance, vals) is 0;

    public static bool IsWithinSpan(int val) => val is >= AttrLower and <= AttrUpper;

    public static bool AreAllWithinSpan(GenesisAttributeSpread vals) => vals.All(IsWithinSpan);
}
