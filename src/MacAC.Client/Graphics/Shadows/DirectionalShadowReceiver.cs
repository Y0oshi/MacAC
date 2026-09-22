using System.Numerics;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal readonly record struct DirectionalShadeFrameWiring(
    long FrameSerial,
    bool Enabled,
    IClientGpuBuffer? Buffer,
    uint OffsetBytes,
    uint SizeBytes,
    GpuTextureSlot TextureSlot,
    int CascadeCount,
    AtmosphericFrameBufferWiring AtmosphericFrame = default)
{
    internal static DirectionalShadeFrameWiring Disabled => default;

    internal bool IsBindableFor(IGpuCycle cycle)
    {
        return Buffer is not null
        && FrameSerial == cycle.SerialNo
        && SizeBytes == DirectionalShadeUniforms.SizeInBytes;
    }

    internal bool IsValidFor(IGpuCycle cycle)
    {
        return IsBindableFor(cycle)
        && Enabled
        && TextureSlot.IsAssigned
        && CascadeCount is >= 2 and <= 4;
    }
}

// Receiver-side seam
internal interface IDirectionalShadeReceiverSource
{
    DirectionalShadePipelineShaders PipeShaders { get; }

    bool TryFetchLatestCycleMapping(
        IGpuCycle cycle,
        out DirectionalShadeFrameWiring mapping);
}

internal readonly record struct DirectionalShadePipelineShaders(
    GpuShaderGroup TerrainCaster,
    GpuShaderGroup WorldOpaqueCaster,
    GpuShaderGroup WorldAlphaCutoutCaster,
    GpuShaderGroup TerrainReceiver,
    GpuShaderGroup WorldReceiver)
{
    internal DirectionalShadeMultiviewPipelineShaders? MultiviewCasters { get; init; }

    internal static DirectionalShadePipelineShaders Local { get; } = new(
        new GpuShaderGroup("sunshade_land"),
        new GpuShaderGroup("sunshade_props_solid"),
        new GpuShaderGroup("sunshade_props_cutout"),
        new GpuShaderGroup("land_haze"),
        new GpuShaderGroup("props_haze"))
    {
        MultiviewCasters = new DirectionalShadeMultiviewPipelineShaders(
            new GpuShaderGroup("sunshade_land_mv"),
            new GpuShaderGroup("sunshade_props_solid_mv"),
            new GpuShaderGroup("sunshade_props_cutout_mv")),
    };
}

internal readonly record struct DirectionalShadeMultiviewPipelineShaders(
    GpuShaderGroup TerrainCaster,
    GpuShaderGroup WorldOpaqueCaster,
    GpuShaderGroup WorldAlphaCutoutCaster);

internal readonly record struct DirectionalShadeCascadeBlend(
    int PrimaryCascade,
    int SecondaryCascade,
    float SecondaryWeight,
    bool WithinShadowReach);

internal static class DirectionalShadeReceiverRule
{
    internal const string AtmosphericRealmPassLabel = "atmospheric-world-hdr";

    internal static bool ShouldPickRecipientPipe(
        string passLabel,
        bool srcPresent,
        bool mappingValid)
    {
        return srcPresent
        && mappingValid
        && string.Equals(
            passLabel,
            AtmosphericRealmPassLabel,
            StringComparison.Ordinal);
    }

    internal static DirectionalShadeCascadeBlend PickCascade(
        float cameraDistanceMeters,
        Vector4 splitFarMeters,
        int cascadeCount,
        float blendWidthMeters)
    {
        if (!float.IsFinite(cameraDistanceMeters) || cameraDistanceMeters < 0f)
            throw new ArgumentOutOfRangeException(nameof(cameraDistanceMeters));
        if (cascadeCount is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(cascadeCount));
        if (!float.IsFinite(blendWidthMeters) || blendWidthMeters < 0f)
            throw new ArgumentOutOfRangeException(nameof(blendWidthMeters));

        Span<float> splits =
        [
            splitFarMeters.X,
            splitFarMeters.Y,
            splitFarMeters.Z,
            splitFarMeters.W,
        ];
        for (int idx = 0; idx < cascadeCount; ++idx)
        {
            if (!float.IsFinite(splits[idx])
                || splits[idx] <= 0f
                || (idx > 0 && splits[idx] < splits[idx - 1]))
            {
                throw new ArgumentException(
                    "Directional-shadow split distances has to be finite, positive, and monotonic",
                    nameof(splitFarMeters));
            }
        }

        int primary = 0;
        while (primary < cascadeCount && cameraDistanceMeters > splits[primary])
            ++primary;
        if (primary == cascadeCount)
            return new DirectionalShadeCascadeBlend(cascadeCount - 1, cascadeCount - 1, 0f, false);

        if (primary == cascadeCount - 1 || blendWidthMeters <= 0f)
            return new DirectionalShadeCascadeBlend(primary, primary, 0f, true);

        float blendBegin = MathF.Max(0f, splits[primary] - blendWidthMeters);
        float t = Math.Clamp(
            (cameraDistanceMeters - blendBegin) / MathF.Max(blendWidthMeters, 1e-6f),
            0f,
            1f);
        float smooth = t * t * (3f - 2f * t);
        return new DirectionalShadeCascadeBlend(primary, primary + 1, smooth, true);
    }

    internal static float RecipientBiasMeters(
        in DirectionalShadeRealmBias bias,
        float normDotCanvasToLamp)
    {
        return bias.ConstantDepthMeters
        + bias.SlopeDepthMeters * (1f - Math.Clamp(normDotCanvasToLamp, 0f, 1f));
    }

    internal static bool ShouldSpecimen(
        bool mappingTurnedOn,
        bool inside,
        bool hasChosenCelestialDirectedLamp) =>
        mappingTurnedOn && !inside && hasChosenCelestialDirectedLamp;
}
