using System.Numerics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal readonly record struct VolumetricShaftFidelity(
    DirectionalShadePreset Preset,
    bool EnabledByDefault,
    float ResolutionScale,
    int RayMarchSteps,
    double IncrementalGpuP50BudgetMilliseconds,
    double IncrementalGpuP99BudgetMilliseconds)
{
    internal static VolumetricShaftFidelity For(DirectionalShadePreset preset)
    {
        return preset switch
        {
            DirectionalShadePreset.Low => new(
                preset,
                EnabledByDefault: false,
                ResolutionScale: 0.25f,
                RayMarchSteps: 24,
                IncrementalGpuP50BudgetMilliseconds: 0.15,
                IncrementalGpuP99BudgetMilliseconds: 0.30),
            DirectionalShadePreset.Medium => new(
                preset,
                EnabledByDefault: true,
                ResolutionScale: 0.25f,
                RayMarchSteps: 40,
                IncrementalGpuP50BudgetMilliseconds: 0.25,
                IncrementalGpuP99BudgetMilliseconds: 0.40),
            DirectionalShadePreset.High => new(
                preset,
                EnabledByDefault: true,
                ResolutionScale: 0.50f,
                RayMarchSteps: 56,
                IncrementalGpuP50BudgetMilliseconds: 0.40,
                IncrementalGpuP99BudgetMilliseconds: 0.65),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
    }
}

internal readonly record struct VolumetricShaftCycleParams(
    bool Enabled,
    float ResolutionScale,
    int RayMarchSteps,
    float Density,
    float Strength,
    Vector3 AuthoredSunColor)
{
    internal static VolumetricShaftCycleParams Disabled =>
        new(false, 0f, 0, 0f, 0f, Vector3.Zero);
}

internal static class VolumetricShaftRule
{
    internal static VolumetricShaftCycleParams Evaluate(
        in VolumetricShaftFidelity fidelity,
        bool userTurnedOn,
        in DirectionalShadeEnvironmentLedger shade,
        WeatherKind weather,
        Vector3 authoredSunTint,
        float authoredSunBrightness)
    {
        if (!userTurnedOn
            || !shade.ShouldRasterize
            || shade.SourceKind is not Packs.AuthoredCelestialShadeSourceKind.Sun
            || !float.IsFinite(authoredSunBrightness)
            || authoredSunBrightness <= 0f)
            return VolumetricShaftCycleParams.Disabled;

        float weatherMultiplier = weather switch
        {
            WeatherKind.Clear => 1f,
            WeatherKind.Overcast => 0.18f,
            WeatherKind.Rain => 0.10f,
            WeatherKind.Snow => 0.16f,
            WeatherKind.Storm => 0.06f,
            _ => 0f,
        };
        if (weatherMultiplier <= 0f)
            return VolumetricShaftCycleParams.Disabled;

        float elevation = Math.Clamp(shade.LightElevationSin, 0f, 1f);
        float noonRollOff = 1f - SmoothHop(0.18f, 0.82f, elevation);
        float strength = Math.Clamp(
            shade.Strength * weatherMultiplier * noonRollOff,
            0f,
            1f);
        if (strength <= 1e-4f)
            return VolumetricShaftCycleParams.Disabled;

        Vector3 tint = Vector3.Max(Vector3.Zero, authoredSunTint)
            * Math.Clamp(authoredSunBrightness, 0f, 8f);
        return new VolumetricShaftCycleParams(
            Enabled: true,
            fidelity.ResolutionScale,
            fidelity.RayMarchSteps,
            Density: 0.035f * strength,
            Strength: strength,
            AuthoredSunColor: tint);
    }

    private static float SmoothHop(float floor, float ceiling, float val)
    {
        float t = Math.Clamp((val - floor) / (ceiling - floor), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}
