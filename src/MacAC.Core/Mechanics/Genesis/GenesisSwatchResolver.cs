namespace MacAC.Mechanics.Genesis;

/// <summary>Picks the representative colour the appearance page shows for each swatch.</summary>
public static class GenesisSwatchResolver
{
    public const int HairSpecimenOrdinal = 0xd0;

    public const int SkinClanSpecimenOrdinal = 0xb0;

    public const int EyePtSpecimenOrdinal = 0x103;

    public const int ClothingSpecimenOrdinal = 0x520;

    public static bool TryFetchPalSetAverageTint(
        IGenesisPalSetSource palSets,
        IGenesisPaletteColorSource tints,
        uint palSetIdent,
        int specimenOrdinal,
        out GenesisSwatchRgb tint)
    {
        ArgumentNullException.ThrowIfNull(palSets);
        ArgumentNullException.ThrowIfNull(tints);

        tint = default;
        if (palSets.TryFetchPalSet(palSetIdent) is not { } palSet)
            return false;
        if (palSet.PaletteIds.Count is 0)
            return true;

        int r = 0, g = 0, b = 0;
        foreach (uint swatchIdent in palSet.PaletteIds)
        {
            if (!tints.TryFetchTint(swatchIdent, specimenOrdinal, out GenesisSwatchRgb specimen))
                continue;
            r += specimen.R;
            g += specimen.G;
            b += specimen.B;
        }

        int num = palSet.PaletteIds.Count;
        tint = new GenesisSwatchRgb((byte)(r / num), (byte)(g / num), (byte)(b / num));
        return true;
    }

    public static bool TryFetchStraightTint(
        IGenesisPaletteColorSource tints,
        uint swatchIdent,
        int specimenOrdinal,
        out GenesisSwatchRgb tint)
    {
        ArgumentNullException.ThrowIfNull(tints);
        return tints.TryFetchTint(swatchIdent, specimenOrdinal, out tint);
    }

    public static bool TryFetchClothingSwatchPalSetIdent(
        IGenesisGarbTableSource clothingCharts,
        uint clothingChartIdent,
        uint swatchBlueprintIdent,
        out uint palSetIdent)
    {
        ArgumentNullException.ThrowIfNull(clothingCharts);

        palSetIdent = 0;
        if (clothingCharts.TryFetchClothingChart(clothingChartIdent) is not { } chart)
            return false;
        if (!chart.PaletteTemplatesById.TryGetValue(swatchBlueprintIdent, out GenesisGarbPaletteTemplate? blueprint))
            return false;
        if (blueprint.Choices.Count is 0)
            return false;

        palSetIdent = blueprint.Choices[0].PalSetId;
        return true;
    }
}
