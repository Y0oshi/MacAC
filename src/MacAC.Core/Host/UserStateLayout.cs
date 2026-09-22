namespace MacAC.Host;

public sealed record UserStateOverrides(
    string? Config = null,
    string? Data = null,
    string? Cache = null)
{
    public static UserStateOverrides None { get; } = new();
}

/// <summary>Where this process keeps every piece of mutable user state.</summary>
public sealed record UserStateLayout(
    string Config,
    string Data,
    string Cache,
    string? PriorConfig)
{
    private const string SettingsVariable = "MACAC_CONFIG_DIR";
    private const string BlobVariable = "MACAC_DATA_DIR";
    private const string StashVariable = "MACAC_CACHE_DIR";

    public string PrefsTrail => Path.Combine(Config, "settings.json");

    public string KeybindsTrail => Path.Combine(Config, "keybinds.json");

    public string Logs => Path.Combine(Data, "logs");

    public string Screenshots => Path.Combine(Data, "screenshots");

    public string Plugins => Path.Combine(Data, "plugins");

    public string Diagnostics => Path.Combine(Cache, "diagnostics");

    public static UserStateLayout Locate(
        UserStateOverrides? substitutions = null,
        IHostProbe? sensor = null)
    {
        sensor ??= LiveHostProbe.Shared;
        substitutions ??= UserStateOverrides.None;

        string? settings = substitutions.Config ?? Variable(sensor, SettingsVariable);
        string? blob = substitutions.Data ?? Variable(sensor, BlobVariable);
        string? stash = substitutions.Cache ?? Variable(sensor, StashVariable);

        StateRoots trunks = StateRootConventions
            .For(sensor.Flavor)
            .Trunks(sensor);

        string? preceding = settings is null ? trunks.PriorConfig : null;

        return new UserStateLayout(
            Mooring(settings ?? trunks.Config, sensor),
            Mooring(blob ?? trunks.Data, sensor),
            Mooring(stash ?? trunks.Cache, sensor),
            preceding is null ? null : Mooring(preceding, sensor));
    }

    private static string? Variable(IHostProbe sensor, string label)
    {
        string? val = sensor.ScanVariable(label);
        return string.IsNullOrWhiteSpace(val) ? null : val;
    }

    private static string Mooring(string path, IHostProbe sensor)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Application directories can't be empty",
                nameof(path));
        }

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(path, sensor.WorkingFolder));
    }
}
