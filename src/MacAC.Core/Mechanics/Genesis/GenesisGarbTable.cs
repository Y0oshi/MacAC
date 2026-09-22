using System.Collections.Frozen;

namespace MacAC.Mechanics.Genesis;

public readonly record struct GenesisGarbSubPaletteRange(uint Offset, uint NumColors);

public readonly record struct GenesisGarbSubPaletteChoice(
    uint PalSetId,
    IReadOnlyList<GenesisGarbSubPaletteRange> Ranges);

public sealed record GenesisGarbPaletteTemplate(IReadOnlyList<GenesisGarbSubPaletteChoice> Choices)
{
    public static GenesisGarbPaletteTemplate Empty { get; } = new([]);
}

public sealed record GenesisGarbBaseEffect(
    IReadOnlyList<GenesisAnimPartSwap> PartChanges,
    IReadOnlyList<GenesisTextureSwap> TextureChanges)
{
    public static GenesisGarbBaseEffect Empty { get; } = new([], []);
}

/// <summary>A clothing table: per-body-setup part/texture swaps plus colour templates.</summary>
public sealed record GenesisGarbTable(
    IReadOnlyDictionary<uint, GenesisGarbBaseEffect> BaseEffectsBySetupId,
    IReadOnlyDictionary<uint, GenesisGarbPaletteTemplate> PaletteTemplatesById)
{
    public static GenesisGarbTable Empty { get; } = new(
        FrozenDictionary<uint, GenesisGarbBaseEffect>.Empty,
        FrozenDictionary<uint, GenesisGarbPaletteTemplate>.Empty);
}

public interface IGenesisPalSetSource
{
    GenesisPalSet? TryFetchPalSet(uint palSetIdent);
}

public interface IGenesisGarbTableSource
{
    GenesisGarbTable? TryFetchClothingChart(uint clothingChartIdent);
}
