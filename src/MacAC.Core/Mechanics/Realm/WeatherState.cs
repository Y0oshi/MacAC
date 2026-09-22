using System.Numerics;

namespace MacAC.Mechanics.Realm;

public enum WeatherKind
{
    Clear = 0,
    Overcast = 1,
    Rain = 2,
    Snow = 3,
    Storm = 4,
}

public enum MechEnvironOverride
{
    None = 0x00,
    RedFog = 0x01,
    BlueFog = 0x02,
    WhiteFog = 0x03,
    GreenFog = 0x04,
    BlackFog = 0x05,
    BlackFog2 = 0x06,
}

/// <summary>Fog and weather for one frame, after server overrides and settings.</summary>
public readonly record struct AtmosphereFrame(
    WeatherKind Kind,
    float Intensity,
    Vector3 FogColor,
    float FogStart,
    float FogEnd,
    FogManner FogMode,
    float LightningFlash,
    MechEnvironOverride Override);

public sealed class WeatherEngine
{
    public const float ChangeoverSecs = 10f;

    private const float FlashDecayPerSecond = 1f / 0.200f;
    private const float FlashPeakSecs = 0.05f;
    private const float FlashFloor = 1e-3f;
    private const int Forced = int.MaxValue;
    private const int NeverRolled = int.MinValue;

    private static readonly string[] OvercastHints = ["storm", "snow", "rain", "cloud", "overcast", "dark", "fog"];

    private WeatherKind _sort = WeatherKind.Clear;
    private WeatherKind _wasSort = WeatherKind.Clear;
    private float _flash;
    private float _flashAge;
    private int _rolledDay = NeverRolled;
    private bool _drivenByHeavens;

    public WeatherEngine(Random? rng = null)
    {
        _ = rng;
    }

    public Func<bool>? DeactivateGapFogSrc { get; set; }

    public WeatherKind Kind => _sort;

    /// <summary>The last server fog override; it persists until the next sync says otherwise.</summary>
    public MechEnvironOverride Override { get; set; }

    public void ForceWeather(WeatherKind sort)
    {
        Shift(sort);
        _rolledDay = Forced;
    }

    public void AssignSortFromDayClusterLabel(string? dayClusterLabel)
    {
        _drivenByHeavens = true;
        WeatherKind mapped = SortFromLabel(dayClusterLabel);
        if (mapped != _sort)
            Shift(mapped);
    }

    public void Tick(double instantSecs, int dayOrdinal, float dtSecs)
    {
        if (!_drivenByHeavens && dayOrdinal != _rolledDay && _rolledDay != Forced)
        {
            _rolledDay = dayOrdinal;
            WeatherKind rolled = RollForDay(dayOrdinal);
            if (rolled != _sort)
                Shift(rolled);
        }

        if (_flash > 0f)
        {
            _flashAge += dtSecs;
            _flash = _flashAge < FlashPeakSecs ? 1f : MathF.Exp(-(_flashAge - FlashPeakSecs) * FlashDecayPerSecond);
            if (_flash < FlashFloor)
                _flash = 0f;
        }
    }

    /// <summary>A lightning strike now (server-forced, or a test hook).</summary>
    public void FireFlash()
    {
        _flash = 1f;
        _flashAge = 0f;
    }

    public AtmosphereFrame Snapshot(in SkyKeyframe keyframe)
    {
        Vector3 fogTint = Override != MechEnvironOverride.None ? OverrideTint(Override) : keyframe.FogColor;
        FogManner fogManner = DeactivateGapFogSrc?.Invoke() == true ? FogManner.Off : keyframe.FogMode;
        return new AtmosphereFrame(_sort, 1f, fogTint, keyframe.FogStart, keyframe.FogEnd, fogManner, _flash, Override);
    }

    private void Shift(WeatherKind upcoming)
    {
        _wasSort = _sort;
        _sort = upcoming;
    }

    private static WeatherKind SortFromLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return WeatherKind.Clear;
        string lower = label.ToLowerInvariant();
        foreach (string hint in OvercastHints)
        {
            if (lower.Contains(hint))
                return WeatherKind.Overcast;
        }
        return WeatherKind.Clear;
    }

    private static WeatherKind RollForDay(int dayOrdinal)
    {
        double roll = new Random(unchecked((int)((uint)dayOrdinal * 0x9E3779B1u))).NextDouble();
        return roll switch
        {
            < 0.60 => WeatherKind.Clear,
            < 0.80 => WeatherKind.Overcast,
            < 0.92 => WeatherKind.Rain,
            < 0.97 => WeatherKind.Snow,
            _ => WeatherKind.Storm,
        };
    }

    private static Vector3 OverrideTint(MechEnvironOverride o)
    {
        return o switch
        {
            MechEnvironOverride.RedFog => new(0.60f, 0.05f, 0.05f),
            MechEnvironOverride.BlueFog => new(0.08f, 0.15f, 0.60f),
            MechEnvironOverride.WhiteFog => new(0.90f, 0.90f, 0.92f),
            MechEnvironOverride.GreenFog => new(0.08f, 0.55f, 0.12f),
            MechEnvironOverride.BlackFog => new(0.02f, 0.02f, 0.02f),
            MechEnvironOverride.BlackFog2 => new(0.04f, 0.01f, 0.01f),
            _ => Vector3.One,
        };
    }
}
