using MacAC.Extensibility.RenderPacks;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Packs;

internal static class RenderPackAtmosphereRuleEvaluation
{
    internal static DirectionalShadeAtmosphereRule NeutralDirectedShadeElevation { get; } =
        DirectionalShadeAtmosphereRule.BuiltIn with
        {
            MinimumLightElevationSin = -1.001f,
            FullStrengthLightElevationSin = -1f,
        };

    internal static float Ray(
        IReadOnlyList<SunElevationKnot>? pts,
        float elevationDeg,
        float backup = 1f)
    {
        return Evaluate(
            pts,
            elevationDeg,
            static val => (float)val,
            static val => (float)val,
            backup);
    }

    internal static float DirectionalShadow(
        IReadOnlyList<SunElevationKnot>? pts,
        float elevationDeg,
        float backup = 0f)
    {
        return DirectedShadeFromSin(
            pts,
            MathF.Sin(elevationDeg * (MathF.PI / 180f)),
            backup);
    }

    internal static float DirectedShadeFromSin(
        IReadOnlyList<SunElevationKnot>? pts,
        float lampElevationSin,
        float backup = 0f)
    {
        return Evaluate(
            pts,
            Math.Clamp(lampElevationSin, -1f, 1f),
            static deg => MathF.Sin((float)deg * (MathF.PI / 180f)),
            static val => (float)val,
            backup);
    }

    internal static (float Mean, float Gust) FoliageWind(
        IReadOnlyList<WindWeatherKnot>? pts,
        WeatherKind weather)
    {
        if (pts is null)
            return (0f, 0f);
        string sort = weather.ToString();
        WindWeatherKnot? wipe = null;
        foreach (WindWeatherKnot pt in pts)
        {
            if (string.Equals(pt.WeatherKind, sort, StringComparison.Ordinal))
                return ((float)pt.Mean, (float)pt.Gust);
            if (wipe is null
                && string.Equals(pt.WeatherKind, nameof(WeatherKind.Clear), StringComparison.Ordinal))

                wipe = pt;
        }
        return wipe is { } backup ? ((float)backup.Mean, (float)backup.Gust) : (0f, 0f);
    }

    internal static float EaseTowardMark(
        float latest,
        float mark,
        float diffSecs,
        float changeoverSecs)
    {
        float rate = changeoverSecs <= 0f
            ? 1f
            : Math.Clamp(diffSecs / changeoverSecs, 0f, 1f);
        return latest + ((mark - latest) * rate);
    }

    internal static float VolumetricShaft(
        IReadOnlyList<SunElevationKnot>? pts,
        float elevationDeg,
        float backup = 0f)
    {
        return Evaluate(
            pts,
            elevationDeg,
            static val => (float)val,
            static val => val * val * (3f - (2f * val)),
            backup);
    }

    private static float Evaluate(
        IReadOnlyList<SunElevationKnot>? pts,
        float feed,
        Func<double, float> xformPt,
        Func<float, float> xformInterpolation,
        float backup)
    {
        if (pts is null || pts.Count is 0)
            return backup;
        float lead = xformPt(pts[0].ElevationDegrees);
        if (feed <= lead)
            return (float)pts[0].Multiplier;
        for (int idx = 1; idx < pts.Count; ++idx)
        {
            var upper = pts[idx];
            float upperFeed = xformPt(upper.ElevationDegrees);
            if (feed > upperFeed)
                continue;
            var lower = pts[idx - 1];
            float lowerFeed = xformPt(lower.ElevationDegrees);
            float span = upperFeed - lowerFeed;
            float t = span <= 0f
                ? 0f
                : Math.Clamp((feed - lowerFeed) / span, 0f, 1f);
            t = xformInterpolation(t);
            return (float)(lower.Multiplier
                + ((upper.Multiplier - lower.Multiplier) * t));
        }
        return (float)pts[^1].Multiplier;
    }
}
