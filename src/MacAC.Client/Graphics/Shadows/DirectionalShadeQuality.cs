using MacAC.Client.Graphics.Packs;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal enum DirectionalShadePreset : byte
{
    Low,
    Medium,
    High,
}

[Flags]
internal enum DirectionalShadeSemantics : ushort
{
    None = 0,
    Terrain = 1 << 0,
    TreesAndOutdoorStatics = 1 << 1,
    Buildings = 1 << 2,
    Players = 1 << 3,
    Monsters = 1 << 4,
    AnimatedTransforms = 1 << 5,
    AlphaCutoutCasters = 1 << 6,

    Headline = Terrain
        | TreesAndOutdoorStatics
        | Buildings
        | Players
        | Monsters
        | AnimatedTransforms
        | AlphaCutoutCasters,
}

internal readonly record struct DirectionalShadeBiasRule(
    float ConstantTexels,
    float SlopeTexels,
    float NormalTexels,
    float MinimumMeters,
    float MaximumMeters)
{
    public DirectionalShadeRealmBias Resolve(float texelWorldSize)
    {
        if (!float.IsFinite(texelWorldSize) || texelWorldSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(texelWorldSize));
        if (!float.IsFinite(MinimumMeters)
            || !float.IsFinite(MaximumMeters)
            || MinimumMeters < 0f
            || MaximumMeters < MinimumMeters)
        {
            throw new InvalidOperationException(
                "Directional-shadow bias bounds has to be finite, non-negative, and ordered");
        }

        float floorMeters = MinimumMeters;
        float ceilingMeters = MaximumMeters;
        return new DirectionalShadeRealmBias(
            ConstantDepthMeters: Math.Clamp(
                ConstantTexels * texelWorldSize,
                floorMeters,
                ceilingMeters),
            SlopeDepthMeters: Math.Clamp(
                SlopeTexels * texelWorldSize,
                floorMeters,
                ceilingMeters),
            NormalOffsetMeters: Math.Clamp(
                NormalTexels * texelWorldSize,
                floorMeters,
                ceilingMeters));
    }
}

internal readonly record struct DirectionalShadeRealmBias(
    float ConstantDepthMeters,
    float SlopeDepthMeters,
    float NormalOffsetMeters);

internal readonly record struct DirectionalShadeQuality(
    DirectionalShadePreset Preset,
    int CascadeCount,
    int MapResolution,
    float MaximumReachMeters,
    int PcfRadiusTexels,
    long ApproximateDepthMapBytes,
    double IncrementalGpuP50BudgetMilliseconds,
    double IncrementalGpuP99BudgetMilliseconds,
    double IncrementalCpuP50BudgetMilliseconds,
    double IncrementalCpuP99BudgetMilliseconds,
    long PackResidentGpuByteBudget,
    DirectionalShadeSemantics Semantics,
    DirectionalShadeBiasRule BiasPolicy)
{
    private const long MiB = 1024L * 1024L;

    public static DirectionalShadeQuality For(DirectionalShadePreset preset)
    {
        return preset switch
        {
            DirectionalShadePreset.Low => Create(
                preset,
                cascades: 2,
                resolution: 768,
                reachMeters: 72f,
                pcfRadius: 0,
                gpuP50: 2.0,
                gpuP99: 3.0,
                cpuP50: 0.15,
                cpuP99: 0.50,
                housedAllowance: 64L * MiB,
                bias: new DirectionalShadeBiasRule(
                    0.45f, 1.25f, 1.0f, 0.001f, 0.35f)),
            DirectionalShadePreset.Medium => Create(
                preset,
                cascades: 3,
                resolution: 1536,
                reachMeters: 144f,
                pcfRadius: 1,
                gpuP50: 3.25,
                gpuP99: 4.50,
                cpuP50: 0.25,
                cpuP99: 0.75,
                housedAllowance: 128L * MiB,
                bias: new DirectionalShadeBiasRule(
                    0.40f, 1.15f, 0.9f, 0.001f, 0.30f)),
            DirectionalShadePreset.High => Create(
                preset,
                cascades: 4,
                resolution: 2048,
                reachMeters: 240f,
                pcfRadius: 2,
                gpuP50: 4.50,
                gpuP99: 6.00,
                cpuP50: 0.35,
                cpuP99: 1.00,
                housedAllowance: 256L * MiB,
                bias: new DirectionalShadeBiasRule(
                    0.35f, 1.0f, 0.8f, 0.001f, 0.25f)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }

    private static DirectionalShadeQuality Create(
        DirectionalShadePreset preset,
        int cascades,
        int resolution,
        float reachMeters,
        int pcfRadius,
        double gpuP50,
        double gpuP99,
        double cpuP50,
        double cpuP99,
        long housedAllowance,
        DirectionalShadeBiasRule bias)
    {
        return new(
            preset,
            cascades,
            resolution,
            reachMeters,
            pcfRadius,
            checked((long)cascades * resolution * resolution * sizeof(float)),
            gpuP50,
            gpuP99,
            cpuP50,
            cpuP99,
            housedAllowance,
            DirectionalShadeSemantics.Headline,
            bias);
    }
}

internal enum DirectionalShadeTurnstileReason : byte
{
    Enabled,
    PackDisabled,
    PortalOrLoginCover,
    Indoor,
    NoVisibleCelestial,
    SelectedLightBelowHorizon,
    SelectedLightHasNoEnergy,
    AtmosphereSuppressed,
    ResidentWindowUnavailable,
}

internal readonly record struct DirectionalShadeAtmosphereRule(
    float MinimumLightElevationSin,
    float FullStrengthLightElevationSin,
    float ClearStrength,
    float OvercastStrength,
    float RainStrength,
    float SnowStrength,
    float StormStrength,
    float ClearSoftness,
    float OvercastSoftness,
    float RainSoftness,
    float SnowSoftness,
    float StormSoftness)
{
    public static DirectionalShadeAtmosphereRule BuiltIn { get; } = new(
        MinimumLightElevationSin: MathF.Sin(MathF.PI / 180f),
        FullStrengthLightElevationSin: MathF.Sin(12f * MathF.PI / 180f),
        ClearStrength: 1.0f,
        OvercastStrength: 0.65f,
        RainStrength: 0.45f,
        SnowStrength: 0.60f,
        StormStrength: 0.25f,
        ClearSoftness: 1.0f,
        OvercastSoftness: 1.5f,
        RainSoftness: 1.8f,
        SnowSoftness: 1.6f,
        StormSoftness: 2.0f);

    public float StrengthFor(WeatherKind weather)
    {
        return weather switch
        {
            WeatherKind.Clear => ClearStrength,
            WeatherKind.Overcast => OvercastStrength,
            WeatherKind.Rain => RainStrength,
            WeatherKind.Snow => SnowStrength,
            WeatherKind.Storm => StormStrength,
            _ => throw new ArgumentOutOfRangeException(nameof(weather), weather, null),
        };
    }

    public float SoftnessFor(WeatherKind weather)
    {
        return weather switch
        {
            WeatherKind.Clear => ClearSoftness,
            WeatherKind.Overcast => OvercastSoftness,
            WeatherKind.Rain => RainSoftness,
            WeatherKind.Snow => SnowSoftness,
            WeatherKind.Storm => StormSoftness,
            _ => throw new ArgumentOutOfRangeException(nameof(weather), weather, null),
        };
    }
}

internal readonly record struct DirectionalShadeEnvironmentInput(
    bool PackEnabled,
    bool PortalOrLoginCoverVisible,
    bool PlayerInsideCell,
    AuthoredCelestialShadeSource Source,
    AtmosphereFrame Atmosphere,
    float DayGroupWeight = 1f);

internal readonly record struct DirectionalShadeEnvironmentLedger(
    DirectionalShadeTurnstileReason Reason,
    System.Numerics.Vector3 SurfaceToLightDirection,
    float LightElevationSin,
    float Strength,
    float SoftnessMultiplier,
    AuthoredCelestialShadeSourceKind SourceKind =
        AuthoredCelestialShadeSourceKind.None,
    int SourceObjectIndex = -1,
    uint SourceGfxObjId = 0u)
{
    public bool ShouldRasterize => Reason is DirectionalShadeTurnstileReason.Enabled;
}

internal static class DirectionalShadeEnvironmentTurnstile
{
    private const float FloorDirectedEnergy = 1e-5f;

    public static DirectionalShadeEnvironmentLedger Evaluate(
        in DirectionalShadeEnvironmentInput feed,
        in DirectionalShadeAtmosphereRule rule)
    {
        if (!feed.PackEnabled)
            return Disabled(DirectionalShadeTurnstileReason.PackDisabled);
        if (feed.PortalOrLoginCoverVisible)
            return Disabled(DirectionalShadeTurnstileReason.PortalOrLoginCover);
        if (feed.PlayerInsideCell)
            return Disabled(DirectionalShadeTurnstileReason.Indoor);

        if (!feed.Source.IsOnHand)
            return Disabled(DirectionalShadeTurnstileReason.NoVisibleCelestial);

        var canvasToLamp =
            feed.Source.SurfaceToLightDirection;
        float elevation = feed.Source.ElevationSin;
        if (!float.IsFinite(elevation)
            || elevation <= rule.MinimumLightElevationSin)
        {
            return new DirectionalShadeEnvironmentLedger(
                DirectionalShadeTurnstileReason.SelectedLightBelowHorizon,
                canvasToLamp,
                elevation,
                0f,
                1f,
                feed.Source.Kind,
                feed.Source.ObjectIndex,
                feed.Source.GfxObjId);
        }

        float energy = feed.Source.AuthoredEnergy;
        if (!float.IsFinite(energy)
            || energy <= FloorDirectedEnergy)
        {
            return new DirectionalShadeEnvironmentLedger(
                DirectionalShadeTurnstileReason.SelectedLightHasNoEnergy,
                canvasToLamp,
                elevation,
                0f,
                1f,
                feed.Source.Kind,
                feed.Source.ObjectIndex,
                feed.Source.GfxObjId);
        }

        float elevationSpan = MathF.Max(
            1e-5f,
            rule.FullStrengthLightElevationSin - rule.MinimumLightElevationSin);
        float elevationStrength = Math.Clamp(
            (elevation - rule.MinimumLightElevationSin) / elevationSpan,
            0f,
            1f);
        float weatherStrength = rule.StrengthFor(feed.Atmosphere.Kind);
        float atmosphereHeadway = Math.Clamp(feed.Atmosphere.Intensity, 0f, 1f);
        float dayClusterStrength = Math.Clamp(feed.DayGroupWeight, 0f, 1f);
        float strength = elevationStrength
            * Math.Clamp(energy, 0f, 1f)
            * weatherStrength
            * atmosphereHeadway
            * dayClusterStrength;
        return !float.IsFinite(strength) || strength <= 0f
            ? new DirectionalShadeEnvironmentLedger(
                DirectionalShadeTurnstileReason.AtmosphereSuppressed,
                canvasToLamp,
                elevation,
                0f,
                rule.SoftnessFor(feed.Atmosphere.Kind),
                feed.Source.Kind,
                feed.Source.ObjectIndex,
                feed.Source.GfxObjId)
            : new DirectionalShadeEnvironmentLedger(
            DirectionalShadeTurnstileReason.Enabled,
            canvasToLamp,
            elevation,
            Math.Clamp(strength, 0f, 1f),
            MathF.Max(1f, rule.SoftnessFor(feed.Atmosphere.Kind)),
            feed.Source.Kind,
            feed.Source.ObjectIndex,
            feed.Source.GfxObjId);
    }

    private static DirectionalShadeEnvironmentLedger Disabled(
        DirectionalShadeTurnstileReason cause) =>
        new(cause, System.Numerics.Vector3.UnitZ, 0f, 0f, 1f);
}
