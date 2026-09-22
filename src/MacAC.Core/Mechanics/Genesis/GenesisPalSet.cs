namespace MacAC.Mechanics.Genesis;

public sealed record GenesisPalSet(IReadOnlyList<uint> PaletteIds)
{
    public static GenesisPalSet Empty { get; } = new([]);
}

public static class GenesisPalSetRules
{
    public static int FetchSwatchOrdinal(int tally, double shade)
    {
        if (tally <= 0 || shade is < 0.0 or > 1.0)
            return -1;
        int choose = (int)((tally - 0.000001) * shade);
        return Math.Clamp(choose, 0, tally - 1);
    }
}
