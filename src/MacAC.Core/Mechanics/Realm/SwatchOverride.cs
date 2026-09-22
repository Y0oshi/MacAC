namespace MacAC.Mechanics.Realm;

/// <summary>An ObjDesc palette: the base palette plus sub-palette spans that replace ranges of it.</summary>
public sealed record SwatchOverride(
    uint BasePaletteId,
    IReadOnlyList<SwatchOverride.SubPaletteSpan> SubPalettes)
{
    public readonly record struct SubPaletteSpan(uint SubPaletteId, byte Offset, byte Length);
}
