namespace MacAC.Mechanics.Genesis;

public readonly record struct GenesisSwatchRgb(byte R, byte G, byte B);

public interface IGenesisPaletteColorSource
{
    bool TryFetchTint(uint swatchIdent, int ordinal, out GenesisSwatchRgb tint);
}
