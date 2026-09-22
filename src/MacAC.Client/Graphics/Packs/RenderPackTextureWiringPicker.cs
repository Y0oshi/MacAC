using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct RasterizeBundleBitmapFeed(
    SemanticInput? Semantic,
    string? ResourceId)
{
    internal static RasterizeBundleBitmapFeed FromSemantic(SemanticInput val) =>
        new(val, null);

    internal static RasterizeBundleBitmapFeed FromAsset(string val) =>
        new(null, val);
}

internal static class RenderPackTextureWiringPicker
{
    internal static IReadOnlyList<RasterizeBundleBitmapFeed> Resolve(
        RenderPassSpec pass,
        IReadOnlyDictionary<string, RenderResourceSpec> assetList)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(assetList);
        var outcome = new List<RasterizeBundleBitmapFeed>(4);
        foreach (SemanticInput semantic in pass.SemanticInputs)
        {
            if (semantic is SemanticInput.WorldColor
                or SemanticInput.SceneDepth
                or SemanticInput.SceneNormals)
                outcome.Add(RasterizeBundleBitmapFeed.FromSemantic(semantic));
        }
        foreach (string assetIdent in pass.ResourceReads)
        {
            if (!assetList.TryGetValue(assetIdent, out RenderResourceSpec? asset))
                throw new InvalidOperationException($"Unrecognized render-pack resource '{assetIdent}'.");
            if (asset.Format == PixelFormatKind.DirectionalDepth
                && pass.SemanticInputs.Contains(SemanticInput.DirectionalShadowMaps))
                continue;
            outcome.Add(RasterizeBundleBitmapFeed.FromAsset(assetIdent));
        }
        return outcome.Count > 4
            ? throw new InvalidOperationException(
                $"Render-pack pass '{pass.Id}' exceeds the four API-v1 texture slots")
            : (IReadOnlyList<RasterizeBundleBitmapFeed>)outcome;
    }
}
