using System.Numerics;

namespace MacAC.Mechanics.Realm;

public enum FogManner
{
    Off = 0,
    Linear = 1,
    Exp = 2,
    Exp2 = 3,
}

public readonly record struct SkyKeyframe(
    float Begin,
    float SunHeadingDeg,
    float SunPitchDeg,
    Vector3 DirColor,
    float DirBright,
    Vector3 AmbColor,
    float AmbBright,
    Vector3 FogColor,
    float FogDensity,
    float FogStart = 80f,
    float FogEnd = 350f,
    FogManner FogMode = FogManner.Linear)
{
    public Vector3 SunColor => DirColor * SkyStateSource.CanonSunVector(this).Length();

    public Vector3 AmbientColor
    {
        get
        {
            return AmbColor * (AmbBright + 0.2f * SkyStateSource.CanonSunVector(this).Length());
        }
    }
}

/// <summary>A day's worth of sky keyframes and the interpolation between them.</summary>
public sealed class SkyStateSource
{
    private const float DegToRadians = MathF.PI / 180f;

    private readonly List<SkyKeyframe> _tags;

    public SkyStateSource(IReadOnlyList<SkyKeyframe> keyframes)
    {
        if (keyframes is null || keyframes.Count is 0)
            throw new ArgumentException("At least one keyframe needed", nameof(keyframes));
        _tags = new List<SkyKeyframe>(keyframes);
        _tags.Sort(static (keyframe, b) => keyframe.Begin.CompareTo(b.Begin));
    }

    public int KeyframeTally => _tags.Count;

    public IReadOnlyList<SkyKeyframe> Keyframes => _tags;

    /// <summary>A plausible four-point day used when the region has no SkyDesc.</summary>
    public static SkyStateSource Default()
    {
        return new(new[]
    {
        new SkyKeyframe(0.0f, 0f, -30f, new(0.02f, 0.02f, 0.08f), 1.0f, new(0.05f, 0.05f, 0.12f), 1.0f, new(0.02f, 0.02f, 0.05f), 0.004f, 30f, 180f),
        new SkyKeyframe(0.25f, 90f, 0f, new(1.0f, 0.7f, 0.4f), 1.0f, new(0.4f, 0.35f, 0.3f), 1.0f, new(0.8f, 0.55f, 0.4f), 0.002f, 60f, 260f),
        new SkyKeyframe(0.5f, 180f, 70f, new(1.0f, 0.98f, 0.95f), 1.0f, new(0.5f, 0.5f, 0.55f), 1.0f, new(0.7f, 0.75f, 0.85f), 0.0008f, 120f, 500f),
        new SkyKeyframe(0.75f, 270f, 0f, new(0.95f, 0.4f, 0.25f), 1.0f, new(0.35f, 0.25f, 0.25f), 1.0f, new(0.85f, 0.45f, 0.35f), 0.002f, 60f, 260f),
    });
    }

    public SkyKeyframe Blend(float t)
    {
        t = (float)(t - Math.Floor(t));

        // The last keyframe whose Begin is at or before t; wraps to the last one before midnight.
        int from = _tags.Count - 1;
        for (int idx = 0; idx < _tags.Count && _tags[idx].Begin <= t; ++idx)
            from = idx;
        int to = (from + 1) % _tags.Count;

        SkyKeyframe keyframe = _tags[from];
        SkyKeyframe b = _tags[to];

        float aCommence = keyframe.Begin;
        float bCommence = b.Begin <= aCommence ? b.Begin + 1.0f : b.Begin;
        float instant = t < aCommence ? t + 1.0f : t;
        float u = Math.Clamp((instant - aCommence) / Math.Max(1e-6f, bCommence - aCommence), 0f, 1f);

        return new SkyKeyframe(
            Begin: t,
            SunHeadingDeg: ShortestAngleLerp(keyframe.SunHeadingDeg, b.SunHeadingDeg, u),
            SunPitchDeg: Lerp(keyframe.SunPitchDeg, b.SunPitchDeg, u),
            DirColor: Vector3.Lerp(keyframe.DirColor, b.DirColor, u),
            DirBright: Lerp(keyframe.DirBright, b.DirBright, u),
            AmbColor: Vector3.Lerp(keyframe.AmbColor, b.AmbColor, u),
            AmbBright: Lerp(keyframe.AmbBright, b.AmbBright, u),
            FogColor: Vector3.Lerp(keyframe.FogColor, b.FogColor, u),
            FogDensity: Lerp(keyframe.FogDensity, b.FogDensity, u),
            FogStart: Lerp(keyframe.FogStart, b.FogStart, u),
            FogEnd: Lerp(keyframe.FogEnd, b.FogEnd, u),
            FogMode: keyframe.FogMode);
    }

    /// <summary>Lerps headings along the shorter arc: 350° to 10° goes forward through 0°.</summary>
    public static float ShortestAngleLerp(float aDeg, float bDeg, float u)
    {
        float diff = bDeg - aDeg;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return aDeg + diff * u;
    }

    /// <summary>The retail sun vector: brightness-scaled, heading around Z, pitch above the horizon.</summary>
    public static Vector3 CanonSunVector(SkyKeyframe keyframe)
    {
        float h = keyframe.SunHeadingDeg * DegToRadians;
        float p = keyframe.SunPitchDeg * DegToRadians;
        float cosP = MathF.Cos(p);
        float bright = keyframe.DirBright;
        return new Vector3(bright * cosP * MathF.Sin(h), bright * cosP * MathF.Cos(h), bright * MathF.Sin(p));
    }

    public static Vector3 SunDirFromKeyframe(SkyKeyframe keyframe)
    {
        Vector3 v = CanonSunVector(keyframe);
        float len = v.Length();
        return len > 1e-6f ? v / len : Vector3.UnitZ;
    }

    private static float Lerp(float a, float b, float u) => a + (b - a) * u;
}

public sealed class WorldClock
{
    private readonly TimeProvider _clock;
    private readonly Action<string>? _synchronizeProbe;
    private SkyStateSource _heavens;
    private double _synchronizeBeats;
    private DateTimeOffset _synchronizeAtUtc;
    private float? _diagDayRatio;

    public WorldClock(SkyStateSource heavens) : this(heavens, new DerethAlmanac(), TimeProvider.System)
    {
    }

    public WorldClock(SkyStateSource sky, DerethAlmanac calendar, TimeProvider? momentSupplier = null, Action<string>? synchronizationProbe = null)
    {
        _heavens = sky ?? throw new ArgumentNullException(nameof(sky));
        Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _clock = momentSupplier ?? TimeProvider.System;
        _synchronizeProbe = synchronizationProbe;
        _synchronizeAtUtc = _clock.GetUtcNow();
    }

    public DerethAlmanac Calendar { get; }

    public float? PinnedDayRatio { get; set; }

    public double TickSize { get; set; } = 1.0;

    public double InstantBeats
    {
        get
        {
            return _synchronizeBeats + (_clock.GetUtcNow() - _synchronizeAtUtc).TotalSeconds * TickSize;
        }
    }

    public double DayFraction => PinnedDayRatio ?? _diagDayRatio ?? Calendar.DayFraction(InstantBeats);

    public SkyKeyframe LatestHeavens => _heavens.Blend((float)DayFraction);

    public Vector3 LatestSunDir => SkyStateSource.SunDirFromKeyframe(LatestHeavens);

    public DerethDateMoment.Almanac LatestCalendar => Calendar.ToCalendar(InstantBeats);

    public bool IsDaytime => Calendar.IsDaytime(InstantBeats);

    public void AssignSupplier(SkyStateSource sky) => _heavens = sky ?? throw new ArgumentNullException(nameof(sky));

    public void SyncFromServer(double srvBeats)
    {
        _synchronizeBeats = srvBeats;
        _synchronizeAtUtc = _clock.GetUtcNow();
        _diagDayRatio = null;

        if (_synchronizeProbe is null)
            return;
        double ratio = Calendar.DayFraction(srvBeats);
        var cal = Calendar.ToCalendar(srvBeats);
        _synchronizeProbe(
            $"[sky-dump] SyncFromServer: ticks={srvBeats:F1} dayFraction={ratio:F4} " +
            $"calendar=PY{cal.Year} {cal.Month} {cal.Day} {cal.Hour}");
    }

    public void AssignDiagMoment(float dayRatio) => _diagDayRatio = dayRatio;

    public void WipeDiagMoment() => _diagDayRatio = null;

    public SkyKeyframe HeavensAtDayRatio(float dayRatio) => _heavens.Blend(dayRatio);
}
