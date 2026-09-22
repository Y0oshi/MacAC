using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static partial class BuiltInAtmosphericRasterizeBundle
{
    internal const string Id = "macac.atmospheric";

    internal static RenderPackCard Descriptor { get; } = new RenderPackCard(
        Id,
        "Atmospheric Rendering",
        new Version(1, 0, 0),
        RenderPackContract.Current,
        PackTier.Tier2Plus,
        [
            RenderFeature.MainWorldColorIntermediate,
            RenderFeature.FullscreenPasses,
            RenderFeature.SceneDepthSampling,
            RenderFeature.AuthoredSunDirection,
            RenderFeature.AuthoredSunScreenPosition,
            RenderFeature.AuthoredWeather,
            RenderFeature.DirectionalShadowMaps,
            RenderFeature.OutdoorDirectionalShadowCasterReplay,
            RenderFeature.AnimatedCasterTransforms,
            RenderFeature.AlphaCutoutShadowCasters,
            RenderFeature.AuthoredCelestialDirectionalLight,
        ],
        [RenderFeature.GpuTimestampQueries],
        Resources(),
        Passs(),
        TableauReplays(),
        PipeVariants(),
        FidelityPresets(),
        Prefs(),
        AtmosphereRule())
    {
        FeatureSummary = "Filmic HDR atmosphere, moving sun-and-moon shadows from terrain, "
                + "trees, buildings, players, and monsters, plus optional volumetric shafts.",
    };
}

internal sealed class FolderRasterizeBundleHoldings : IRenderPackFiles
{
    private readonly string _trunk;

    internal FolderRasterizeBundleHoldings(string trunk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trunk);
        _trunk = Path.GetFullPath(trunk);
    }

    public Stream OpenScan(string assetTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetTag);
        string normalized = assetTag.Replace('/', Path.DirectorySeparatorChar);
        string trail = Path.GetFullPath(Path.Combine(_trunk, normalized));
        string relative = Path.GetRelativePath(_trunk, trail);
        return Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? throw new UnauthorizedAccessException("The asset key escapes the render-pack root")
            : (Stream)File.Open(trail, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
}
