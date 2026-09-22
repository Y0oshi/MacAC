namespace MacAC.Client.Graphics.Batching;

internal enum BatchTextureResolutionKind : byte
{
    SharedAtlas,
    OriginalTextureOverride,
    PaletteComposite,
}

internal static class BatchTextureResolutionRule
{
    public static BatchTextureResolutionKind Select(
        bool hasOriginalTextureOverride,
        bool hasSwatchOverride,
        bool srcIsSwatchIndexed)
    {
        return hasSwatchOverride && srcIsSwatchIndexed
            ? BatchTextureResolutionKind.PaletteComposite
            : hasOriginalTextureOverride
            ? BatchTextureResolutionKind.OriginalTextureOverride
            : BatchTextureResolutionKind.SharedAtlas;
    }
}
