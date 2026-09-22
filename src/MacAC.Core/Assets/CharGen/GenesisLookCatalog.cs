using MacAC.Dat;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using MacAC.Mechanics.Genesis;
using DatClothingTable =  MacAC.Dat.WardrobeTable;
using DatPalette =  MacAC.Dat.ColorTable;
using DatPalSet =  MacAC.Dat.ColorTableSet;
using DatCloObjectEffect =  MacAC.Dat.WardrobePartEffect;
using DatCloSubPalette =  MacAC.Dat.WardrobeColorSwap;
using DatColor =  MacAC.Dat.Argb;

namespace MacAC.Assets.CharGen;

public sealed class GenesisLookCatalog(IDatAccess dats) : IGenesisPalSetSource, IGenesisGarbTableSource, IGenesisPaletteColorSource
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly ConcurrentDictionary<uint, GenesisPalSet?> _palSets = new();
    private readonly ConcurrentDictionary<uint, GenesisGarbTable?> _garbCharts = new();
    private readonly ConcurrentDictionary<uint, DatPalette?> _swatches = new();

    public GenesisPalSet? TryFetchPalSet(uint palSetIdent) => _palSets.GetOrAdd(palSetIdent, ScanPalSet);

    public GenesisGarbTable? TryFetchClothingChart(uint clothingChartIdent) => _garbCharts.GetOrAdd(clothingChartIdent, ScanGarbChart);

    public bool TryFetchTint(uint swatchIdent, int ordinal, out GenesisSwatchRgb tint)
    {
        tint = default;
        DatPalette? swatch = _swatches.GetOrAdd(swatchIdent, ident => _datFiles.Get<DatPalette>(ident));
        if (swatch is null || (uint)ordinal >= (uint)swatch.Colors.Count)
            return false;

        DatColor aRGB = swatch.Colors[ordinal];
        tint = new GenesisSwatchRgb(aRGB.Red, aRGB.Green, aRGB.Blue);
        return true;
    }

    private GenesisPalSet? ScanPalSet(uint ident)
    {
        DatPalSet? palSet = _datFiles.Get<DatPalSet>(ident);
        if (palSet is null)
            return null;

        uint[] idents = new uint[palSet.ColorTableIds.Count];
        for (int idx = 0; idx < idents.Length; ++idx)
            idents[idx] = palSet.ColorTableIds[idx];
        return new GenesisPalSet(Array.AsReadOnly(idents));
    }

    private GenesisGarbTable? ScanGarbChart(uint ident)
    {
        var chart = _datFiles.Get<DatClothingTable>(ident);
        if (chart is null)
            return null;

        var baseFxList = new Dictionary<uint, GenesisGarbBaseEffect>(chart.BaseEffects.Count);
        foreach (var duo in chart.BaseEffects)
            baseFxList[duo.Key] = ProjectBaseFx(duo.Value.Parts);

        var blueprints = new Dictionary<uint, GenesisGarbPaletteTemplate>(chart.ColorEffects.Count);
        foreach (var duo in chart.ColorEffects)
            blueprints[duo.Key] = ProjectSwatchBlueprint(duo.Value.Swaps);

        return new GenesisGarbTable(baseFxList.ToFrozenDictionary(), blueprints.ToFrozenDictionary());
    }

    // Part model swaps plus every texture swap those parts carry
    private static GenesisGarbBaseEffect ProjectBaseFx(IReadOnlyList<DatCloObjectEffect> fxList)
    {
        var pieceSwaps = new List<GenesisAnimPartSwap>(fxList.Count);
        var textureSwaps = new List<GenesisTextureSwap>();
        foreach (DatCloObjectEffect fx in fxList)
        {
            byte piece = (byte)fx.Index;
            pieceSwaps.Add(new GenesisAnimPartSwap(piece, fx.PartMeshId));
            foreach (WardrobeTextureSwap bmp in fx.TextureSwaps)
                textureSwaps.Add(new GenesisTextureSwap(piece, bmp.OldTextureId, bmp.NewTextureId));
        }
        return new GenesisGarbBaseEffect(Array.AsReadOnly(pieceSwaps.ToArray()), Array.AsReadOnly(textureSwaps.ToArray()));
    }

    private static GenesisGarbPaletteTemplate ProjectSwatchBlueprint(IReadOnlyList<DatCloSubPalette> subSwatches)
    {
        var choices = new GenesisGarbSubPaletteChoice[subSwatches.Count];
        for (int idx = 0; idx < choices.Length; ++idx)
        {
            WardrobeColorSwap sub = subSwatches[idx];
            var spans = new GenesisGarbSubPaletteRange[sub.Ranges.Count];
            for (int jdx = 0; jdx < spans.Length; ++jdx)
                spans[jdx] = new GenesisGarbSubPaletteRange(sub.Ranges[jdx].Offset, sub.Ranges[jdx].Count);
            choices[idx] = new GenesisGarbSubPaletteChoice(sub.ColorTableSetId, Array.AsReadOnly(spans));
        }
        return new GenesisGarbPaletteTemplate(Array.AsReadOnly(choices));
    }
}
