namespace MacAC.Mechanics.Genesis;

public readonly record struct GenesisSubPalette(uint SubPaletteId, byte Offset, byte NumColors);

public readonly record struct GenesisTextureSwap(byte PartIndex, uint OldTextureId, uint NewTextureId);

public readonly record struct GenesisAnimPartSwap(byte PartIndex, uint PartId);

/// <summary>A partial ObjDesc: the palette, texture and part overrides one choice contributes.</summary>
public sealed record GenesisObjDesc(
    uint PaletteId,
    IReadOnlyList<GenesisSubPalette> SubPalettes,
    IReadOnlyList<GenesisTextureSwap> TextureChanges,
    IReadOnlyList<GenesisAnimPartSwap> AnimPartChanges)
{
    public static GenesisObjDesc Empty { get; } = new(0u, [], [], []);
}
