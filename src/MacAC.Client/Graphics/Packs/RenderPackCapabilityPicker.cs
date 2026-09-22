using MacAC.Client.Graphics.Gpu;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal static class RenderPackCapabilityPicker
{
    internal const long AbsoluteHousedByteCeiling = 256L * 1024 * 1024;
    internal const long AbsoluteTransientByteCeiling = 512L * 1024 * 1024;
    internal const int DevOwnPortionDenominator = 8;

    internal static RasterizeBundleHubCapabilities Resolve(GpuCapabilityCapture gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var onHand = new HashSet<RenderFeature>
        {
            RenderFeature.FullscreenPasses,
            RenderFeature.AuthoredSunDirection,
            RenderFeature.AuthoredCelestialDirectionalLight,
            RenderFeature.AuthoredSunScreenPosition,
            RenderFeature.AuthoredWeather,
            RenderFeature.OutdoorDirectionalShadowCasterReplay,
            RenderFeature.AnimatedCasterTransforms,
            RenderFeature.AlphaCutoutShadowCasters,
        };

        if (gpu.SupportsRgba16FloatRasterizeMarks)
            onHand.Add(RenderFeature.MainWorldColorIntermediate);
        if (gpu.SupportsSampledZDepth)
        {
            onHand.Add(RenderFeature.SceneDepthSampling);
            onHand.Add(RenderFeature.DirectionalShadowMaps);
        }
        if (gpu.SupportsStampAsks)
            onHand.Add(RenderFeature.GpuTimestampQueries);
        if (gpu.SupportsMultiview)
            onHand.Add(RenderFeature.MultiviewDirectionalShadowCascades);

        long housedOctets = DevOwnPortion(
            gpu.DevOwnMemoryOctets,
            AbsoluteHousedByteCeiling);
        long transientOctets = DevOwnPortion(
            gpu.DevOwnMemoryOctets,
            AbsoluteTransientByteCeiling);
        return new RasterizeBundleHubCapabilities(
            onHand,
            MaxImageDimension2D: checked((int)Math.Min(
                gpu.UpperImageDimension2D,
                (uint)int.MaxValue)),
            MaxImageArrayLayers: checked((int)Math.Min(
                gpu.UpperImageArrStrata,
                (uint)int.MaxValue)),
            MaxPackResidentBytes: housedOctets,
            MaxPackTransientBytes: transientOctets,
            MemoryPolicyDescription:
                $"one eighth of {gpu.DevOwnMemoryOctets} device-local bytes, "
                + $"capped at {AbsoluteHousedByteCeiling} resident and "
                + $"{AbsoluteTransientByteCeiling} transient bytes");
    }

    private static long DevOwnPortion(ulong devOwnOctets, long ceiling)
    {
        ulong portion = devOwnOctets / DevOwnPortionDenominator;
        return (long)Math.Min(portion, checked((ulong)ceiling));
    }
}
