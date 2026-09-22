using MacAC.Cockpit.Settings;

namespace MacAC.Cockpit.Panels.Settings;

public enum MoteSpan
{
    Retail = 0,
    Extended = 1,
}

public sealed class RenderPackOverrides : IReadOnlyDictionary<string, string>, IEquatable<RenderPackOverrides>
{
    private readonly SortedDictionary<string, string> _vals = new(StringComparer.OrdinalIgnoreCase);

    public static RenderPackOverrides Empty { get; } = new([]);

    public RenderPackOverrides(IEnumerable<KeyValuePair<string, string>> vals)
    {
        ArgumentNullException.ThrowIfNull(vals);
        foreach ((string tag, string val) in vals)
        {
            ArgumentNullException.ThrowIfNull(tag);
            ArgumentNullException.ThrowIfNull(val);
            _vals[tag] = val;
        }
    }

    public int Count => _vals.Count;

    public IEnumerable<string> Keys => _vals.Keys;

    public IEnumerable<string> Values => _vals.Values;

    public string this[string key] => _vals[key];

    public bool ContainsKey(string tag) => _vals.ContainsKey(tag);

    public bool TryGetValue(string tag, out string val) => _vals.TryGetValue(tag, out val!);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _vals.GetEnumerator();

    /// <summary>A copy with one setting replaced.</summary>
    public RenderPackOverrides Set(string settingIdent, string val)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingIdent);
        ArgumentNullException.ThrowIfNull(val);
        return new RenderPackOverrides(_vals.Append(new KeyValuePair<string, string>(settingIdent, val)));
    }

    public bool Equals(RenderPackOverrides? another)
    {
        return another is not null
        && _vals.Count == another._vals.Count
        && _vals.All(duo => another._vals.TryGetValue(duo.Key, out string? theirs) && string.Equals(duo.Value, theirs, StringComparison.Ordinal));
    }

    public override bool Equals(object? objRef) => objRef is RenderPackOverrides another && Equals(another);

    public override int GetHashCode()
    {
        HashCode digest = new HashCode();
        foreach ((string tag, string val) in _vals)
        {
            digest.Add(tag, StringComparer.OrdinalIgnoreCase);
            digest.Add(val, StringComparer.Ordinal);
        }
        return digest.ToHashCode();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Which render pack and preset is active; retail with preset "off" is the stock look.</summary>
public sealed record RenderPackPick(string PackId, string? PackVersion, string PresetId)
{
    public const string CanonBundleIdent = "retail";
    public const string CanonPresetIdent = "off";

    public static RenderPackPick Retail { get; } = new(CanonBundleIdent, PackVersion: null, CanonPresetIdent);

    public RenderPackOverrides SettingOverrides { get; init; } = RenderPackOverrides.Empty;

    public bool IsCanon
    {
        get
        {
            return string.Equals(PackId, CanonBundleIdent, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public sealed record ReadoutPrefs(
    string Resolution,
    bool Fullscreen,
    bool VSync,
    float FieldOfView,
    float Gamma,
    bool ShowFps,
    QualityTier Quality,
    MoteSpan ParticleRange,
    float ScreenBrightness = 0f,
    bool AutomaticDegrades = false,
    float GraphicsPerformance = 0f,
    float DegradeDistance = 50f,
    int LandscapeTextureDetail = 2,
    int EnvironmentTextureDetail = 1,
    int TextureFiltering = 1,
    int LandscapeDrawDistance = 8,
    bool BuildingDetailTextures = true,
    bool MultiPassAlpha = false)
{
    public RenderPackPick RenderPack { get; init; } = RenderPackPick.Retail;

    public static ReadoutPrefs Default { get; } = new(
        Resolution: "1280x720",
        Fullscreen: false,
        VSync: true,
        FieldOfView: 90f,
        Gamma: 1.0f,
        ShowFps: false,
        Quality: QualityTier.High,
        ParticleRange: MoteSpan.Extended);

    public static IReadOnlyList<string> OnHandResolutions { get; } = ["1280x720", "1366x768", "1600x900", "1920x1080", "2560x1440", "3840x2160"];
}
