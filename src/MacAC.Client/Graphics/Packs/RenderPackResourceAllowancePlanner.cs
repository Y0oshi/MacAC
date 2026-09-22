using MacAC.Client.Graphics.Batching;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct RenderPackResourceAllowance(
    long RetainedGpuBytes,
    long MultisampleGpuBytes,
    int LargestImageWidth,
    int LargestImageHeight,
    int LargestImageLayerCount)
{
    internal long SumGpuOctets => checked(RetainedGpuBytes + MultisampleGpuBytes);
}

internal static class RenderPackResourceAllowancePlanner
{
    private const int HdrTintOctetsPerPixel = 8;
    private const int LdrTintOctetsPerPixel = 4;
    private const int DirectedZDepthOctetsPerPixel = 4;
    private const int PrimaryRealmZDepthOctetsPerPixel = 4;
    private const int DirectedShadeXformFlightSockets = 2;

    internal static RenderPackResourceAllowance Resolve(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        int primaryRealmWidth,
        int primaryRealmHeight,
        int specimenTally)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primaryRealmWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primaryRealmHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(specimenTally);

        long primaryPx = checked((long)primaryRealmWidth * primaryRealmHeight);
        long kept = checked(primaryPx
            * (HdrTintOctetsPerPixel + PrimaryRealmZDepthOctetsPerPixel));
        long multisample = specimenTally > 1
            ? checked(primaryPx
                * (HdrTintOctetsPerPixel + PrimaryRealmZDepthOctetsPerPixel)
                * specimenTally)
            : 0L;
        int largestWidth = primaryRealmWidth;
        int largestHeight = primaryRealmHeight;
        int largestStrata = 1;

        HashSet<string> writtenAssetList = descriptor.Passes
            .SelectMany(static pass => pass.ResourceWrites)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (RenderResourceSpec asset in descriptor.Resources)
        {
            if (asset.Semantic == RenderResourceRole.MainWorldHdr
                || !writtenAssetList.Contains(asset.Id))

                continue;
            if (UsesFusedAtmosphericPostProc(preset)
                && asset.Semantic is RenderResourceRole.BloomPing
                    or RenderResourceRole.BloomPong)

                continue;

            if (asset.Kind is not GpuResourceKind.Image2D
                and not GpuResourceKind.Image2DArray)
            {
                throw new NotSupportedException(
                    $"Resource '{asset.Id}' isn't an API-v1 image resource");
            }

            var assetOverride = preset.ResourceOverrides
                .FirstOrDefault(val => string.Equals(
                    val.ResourceId,
                    asset.Id,
                    StringComparison.OrdinalIgnoreCase));
            RenderExtentSpec reach = assetOverride?.Extent
                ?? asset.Extent
                ?? throw new NotSupportedException(
                    $"Image resource '{asset.Id}' has no extent");
            (int width, int height) = LocateReach(
                asset.Id,
                reach,
                primaryRealmWidth,
                primaryRealmHeight);
            int strata = reach.Layers;
            if (strata <= 0)
            {
                throw new NotSupportedException(
                    $"Image resource '{asset.Id}' has no image layers");
            }

            int octetsPerPixel = asset.Format switch
            {
                PixelFormatKind.HdrColor => HdrTintOctetsPerPixel,
                PixelFormatKind.LdrColor or PixelFormatKind.SingleChannel =>
                    LdrTintOctetsPerPixel,
                PixelFormatKind.DirectionalDepth => DirectedZDepthOctetsPerPixel,
                _ => throw new NotSupportedException(
                    $"Image resource '{asset.Id}' has not supported format "
                    + $"'{asset.Format}'."),
            };
            kept = checked(kept
                + ((long)width * height * strata * octetsPerPixel));
            largestWidth = Math.Max(largestWidth, width);
            largestHeight = Math.Max(largestHeight, height);
            largestStrata = Math.Max(largestStrata, strata);
        }

        if (descriptor.Passes.Any(pass =>
                pass.Semantic == RenderPassRole.DirectionalShadowDepth))
        {
            kept = checked(
                kept
                + DirectedShadeXformFlightSockets
                    * RealmTransformCapRule.StartingMappingByteSize);
        }

        return new RenderPackResourceAllowance(
            kept,
            multisample,
            largestWidth,
            largestHeight,
            largestStrata);
    }

    internal static RenderPackResourceAllowance DemandWithinPreset(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        int primaryRealmWidth,
        int primaryRealmHeight,
        int specimenTally)
    {
        var allowance = Resolve(
            descriptor,
            preset,
            primaryRealmWidth,
            primaryRealmHeight,
            specimenTally);
        return allowance.RetainedGpuBytes > preset.MaxResidentGpuBytes
            ? throw new NotSupportedException(
                $"Render pack preset '{preset.Id}' needs "
                + $"{allowance.RetainedGpuBytes} resident GPU bytes at "
                + $"{primaryRealmWidth}x{primaryRealmHeight}; its declared ceiling is "
                + $"{preset.MaxResidentGpuBytes}. Select a compatible preset or "
                + "reduce the main-world resolution")
            : allowance;
    }

    internal static RenderPackResourceAllowance DemandWithinHub(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        int primaryRealmWidth,
        int primaryRealmHeight,
        int specimenTally,
        RasterizeBundleHubCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var allowance = DemandWithinPreset(
            descriptor,
            preset,
            primaryRealmWidth,
            primaryRealmHeight,
            specimenTally);
        if (allowance.LargestImageWidth > capabilities.MaxImageDimension2D
            || allowance.LargestImageHeight > capabilities.MaxImageDimension2D)
        {
            throw new NotSupportedException(
                $"Render pack preset '{preset.Id}' resolves an image to "
                + $"{allowance.LargestImageWidth}x{allowance.LargestImageHeight} at "
                + $"{primaryRealmWidth}x{primaryRealmHeight}; this device's maximum "
                + $"2-D image edge is {capabilities.MaxImageDimension2D}.");
        }
        if (allowance.LargestImageLayerCount > capabilities.MaxImageArrayLayers)
        {
            throw new NotSupportedException(
                $"Render pack preset '{preset.Id}' needs "
                + $"{allowance.LargestImageLayerCount} image-array layers; this "
                + $"device provides {capabilities.MaxImageArrayLayers}.");
        }
        if (allowance.RetainedGpuBytes > capabilities.MaxPackResidentBytes)
        {
            throw new NotSupportedException(
                $"Render pack preset '{preset.Id}' needs "
                + $"{allowance.RetainedGpuBytes} resident GPU bytes at "
                + $"{primaryRealmWidth}x{primaryRealmHeight}; this host permits "
                + $"{capabilities.MaxPackResidentBytes} under its "
                + $"{capabilities.MemoryPolicyDescription} policy");
        }
        return allowance.MultisampleGpuBytes > capabilities.MaxPackTransientBytes
            ? throw new NotSupportedException(
                $"Render pack preset '{preset.Id}' needs "
                + $"{allowance.MultisampleGpuBytes} transient multisample GPU bytes "
                + $"at {primaryRealmWidth}x{primaryRealmHeight} x{specimenTally}; this "
                + $"host permits {capabilities.MaxPackTransientBytes} under its "
                + $"{capabilities.MemoryPolicyDescription} policy")
            : allowance;
    }

    private static bool UsesFusedAtmosphericPostProc(
        QualityLadderStep preset)
    {
        return (preset.ExecutionHints
            & QualityExecutionHints.FusedAtmosphericPostProcess) != 0;
    }

    private static (int Width, int Height) LocateReach(
        string assetIdent,
        RenderExtentSpec reach,
        int primaryRealmWidth,
        int primaryRealmHeight)
    {
        if (!double.IsFinite(reach.Width)
            || !double.IsFinite(reach.Height)
            || reach.Width <= 0d
            || reach.Height <= 0d)
        {
            throw new NotSupportedException(
                $"Image resource '{assetIdent}' has an not valid extent");
        }

        try
        {
            return reach.Mode switch
            {
                ExtentRule.AbsolutePixels =>
                    (checked((int)reach.Width), checked((int)reach.Height)),
                ExtentRule.RelativeToMainWorld or ExtentRule.RelativeToOutput =>
                    (Math.Max(1, checked((int)Math.Ceiling(primaryRealmWidth * reach.Width))),
                     Math.Max(1, checked((int)Math.Ceiling(primaryRealmHeight * reach.Height)))),
                _ => throw new NotSupportedException(
                    $"Image resource '{assetIdent}' has not supported extent mode "
                    + $"'{reach.Mode}'."),
            };
        }
        catch (OverflowException problem)
        {
            throw new NotSupportedException(
                $"Image resource '{assetIdent}' extent overflows the host image range",
                problem);
        }
    }
}
