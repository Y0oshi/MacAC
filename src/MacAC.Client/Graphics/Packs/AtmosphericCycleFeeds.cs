using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MacAC.Extensibility.RenderPacks;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Packs;

internal readonly record struct AtmosphericCycleFeeds(
    Vector2 SunScreenUv,
    bool SunIsOnScreen,
    float SunElevationDegrees,
    Vector3 SunColor,
    Vector3 SunDirection,
    float SunDirectionalBrightness,
    Matrix4x4 InverseViewProjection,
    int ActiveDayGroup,
    WeatherKind Weather,
    float WeatherIntensity,
    double DeltaSeconds,
    int ViewportWidth,
    int ViewportHeight,
    bool IsOutdoor);

internal interface IAtmosphericRealmFrameSink
{
    void Publish(
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster);
}

internal sealed class AtmosphericFrameInputLedger : IAtmosphericRealmFrameSink
{
    private RasterizeCycleFeed _hub;
    private RasterizeCycleFoundation _foundation;
    private AtmosphericCycleFeeds _latest;
    private bool _published;

    public void Publish(
        in RasterizeCycleFoundation foundation,
        in RealmRenderFrame realm,
        int engagedDayCluster)
    {
        Vector3 dir = SkyStateSource.SunDirFromKeyframe(foundation.Sky);
        Vector3 sunPt = realm.Camera.Position + (dir * 10_000f);
        Vector4 clip = Vector4.Transform(
            new Vector4(sunPt, 1f),
            realm.Camera.LensMirror);
        bool finite = float.IsFinite(clip.X)
            && float.IsFinite(clip.Y)
            && float.IsFinite(clip.W)
            && clip.W > 1e-5f;
        Vector2 uv = finite
            ? new Vector2(
                (clip.X / clip.W * 0.5f) + 0.5f,
                0.5f - (clip.Y / clip.W * 0.5f))
            : new Vector2(-1f, -1f);
        bool onMonitor = finite
            && uv.X >= 0f && uv.X <= 1f
            && uv.Y >= 0f && uv.Y <= 1f;
        Matrix4x4 invLensProj = Matrix4x4.Invert(
            realm.Camera.LensMirror,
            out Matrix4x4 inv)
                ? inv
                : Matrix4x4.Identity;

        _latest = new AtmosphericCycleFeeds(
            uv,
            onMonitor,
            foundation.Sky.SunPitchDeg,
            foundation.Sky.SunColor,
            dir,
            foundation.Sky.DirBright,
            invLensProj,
            engagedDayCluster,
            foundation.Atmosphere.Kind,
            Math.Clamp(foundation.Atmosphere.Intensity, 0f, 1f),
            _hub.DeltaSeconds,
            _hub.ViewportWidth,
            _hub.ViewportHeight,
            IsOutdoor: realm.Roots.IsAtmosphericallyExterior);
        _published = true;
    }

    internal void BeginFrame(
        in RasterizeCycleFeed hub,
        in RasterizeCycleFoundation foundation)
    {
        _hub = hub;
        _foundation = foundation;
        _latest = default;
        _published = false;
    }

    internal AtmosphericCycleFeeds Freeze()
    {
        return _published
            ? _latest
            : new AtmosphericCycleFeeds(
            new Vector2(-1f, -1f),
            SunIsOnScreen: false,
            _foundation.Sky.SunPitchDeg,
            _foundation.Sky.SunColor,
            SkyStateSource.SunDirFromKeyframe(_foundation.Sky),
            _foundation.Sky.DirBright,
            Matrix4x4.Identity,
            -1,
            _foundation.Atmosphere.Kind,
            Math.Clamp(_foundation.Atmosphere.Intensity, 0f, 1f),
            _hub.DeltaSeconds,
            _hub.ViewportWidth,
            _hub.ViewportHeight,
            IsOutdoor: false);
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct AtmosphericCycleUniforms
{
    internal const int SizeInBytes = 192;

    internal AtmosphericCycleUniforms(
        Vector4 sunMonitor,
        Vector4 sunTint,
        Vector4 viewRect,
        Vector4 weather,
        Vector4 sunDir,
        Vector4 rule,
        Matrix4x4 invLensProj,
        Vector4 timerWind,
        Vector4 windAmplitude)
    {
        SunMonitor = sunMonitor;
        SunColor = sunTint;
        Viewport = viewRect;
        Weather = weather;
        SunDir = sunDir;
        Policy = rule;
        InvLensProj = invLensProj;
        TimerWind = timerWind;
        WindAmplitude = windAmplitude;
    }

    internal readonly Vector4 SunMonitor;
    internal readonly Vector4 SunColor;
    internal readonly Vector4 Viewport;
    internal readonly Vector4 Weather;
    internal readonly Vector4 SunDir;
    internal readonly Vector4 Policy;
    internal readonly Matrix4x4 InvLensProj;
    internal readonly Vector4 TimerWind;
    internal readonly Vector4 WindAmplitude;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct AtmosphericBundleSweepUniforms
{
    internal const int SizeInBytes = 64;

    internal AtmosphericBundleSweepUniforms(
        Vector4 params0,
        Vector4 params1,
        Vector4 params2,
        Vector4 params3)
    {
        Params0 = params0;
        Params1 = params1;
        Params2 = params2;
        Params3 = params3;
    }

    internal readonly Vector4 Params0;
    internal readonly Vector4 Params1;
    internal readonly Vector4 Params2;
    internal readonly Vector4 Params3;

    internal static AtmosphericBundleSweepUniforms From(Vector4 params0) =>
        new(params0, Vector4.Zero, Vector4.Zero, Vector4.Zero);
}

// Shader ABI SSOT for opt-in set 3 binding 8
[InlineArray(ShaderAbi.BundleSettingScalarCap)]
internal struct PackPreferencesUniforms
{
    internal const int SizeInBytes = ShaderAbi.BundlePrefsByteSize;
    private float _element0;

    internal static PackPreferencesUniforms Create(
        RenderPackCard descriptor,
        QualityLadderStep preset,
        IReadOnlyDictionary<string, string>? userSettingSubstitutions = null)
    {
        PackPreferencesUniforms outcome = new PackPreferencesUniforms();
        int tally = Math.Min(
            descriptor.Settings.Count,
            ShaderAbi.BundleSettingScalarCap);
        for (int idx = 0; idx < tally; ++idx)
        {
            var setting = descriptor.Settings[idx];
            string val = RenderPackPreferenceResolution.Resolve(
                setting,
                preset,
                userSettingSubstitutions);
            outcome[idx] = SettingValueCodec.TryPack(setting, val, out float encoded)
                ? encoded
                : 0f;
        }
        return outcome;
    }
}
