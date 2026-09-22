using System.Globalization;
using System.Reflection;
using System.Text;
using MacAC.Client.Bootstrap;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Paging;
using MacAC.Sim.Presence;

namespace MacAC.Client;

public sealed record EngineKnobs(
    string DatDir,
    string PreparedAssetPath,
    bool LiveMode,
    string LiveHost,
    int LivePort,
    string? LiveUser,
    string? LivePass,
    bool DevTools,
    bool UncappedRendering,
    bool DumpMoveTruth,
    bool DumpSky,
    bool DumpWalkTranscript,
    bool NoAudio,
    int HidePartIndex,
    bool RetailCloseDegrades,
    bool DumpSceneryZ,
    int? LegacyStreamRadius,
    bool RetailUi,
    bool OpenCharacterCreationOnStart,
    string? AcDir,
    bool UiProbeDump,
    string? UiProbeScript,
    string? AutomationArtifactDirectory,
    bool ExactAutomationFramebuffer,
    int? ForcedDayGroupIndex,
    float? PinnedWorldDayFraction,
    float? SkyAnimationPhaseSeconds,
    float? InitialOrbitDistanceMeters,
    float? InitialOrbitYawDegrees,
    float? InitialOrbitPitchDegrees,
    TenancyAllowanceKnobs ResidencyBudgets,
    PagingWorkAllowanceKnobs StreamingWorkBudgets,
    string? VulkanDeviceOverride,
    string? VulkanForcedUnsupportedFeature,
    bool VulkanCapabilityProbe,
    int VulkanCapabilityProbeFrames,
    string? SessionConfigPath,
    string? SessionId,
    OnlineSessionToonSelector? LiveCharacterSelector,
    string? StatusFilePath,
    IReadOnlyList<string>? Plugins,
    IReadOnlyList<string> LoginCommands,
    int LoginCommandDelayMs)
{
    public string? ReadiedAssetTopLayerTrail { get; init; }

    public uint? ReadiedAssetBaseRecipeVer { get; init; }

    public uint? ReadiedAssetNetRecipeVer { get; init; }

    public IReadOnlyList<string> ExtensionTags { get; init; } = [];

    public string? VtankProfileFolderOverride { get; init; }

    /// <summary>Reads the knobs from the process environment; what <c>Program</c> does at startup.</summary>
    public static EngineKnobs FromSurroundings(string datDirection) => Parse(datDirection, Environment.GetEnvironmentVariable);

    public static EngineKnobs Parse(string datDirection, Func<string, string?> environ)
    {
        ArgumentNullException.ThrowIfNull(datDirection);
        ArgumentNullException.ThrowIfNull(environ);
        Env e = new Env(environ);

        return new EngineKnobs(
            DatDir: datDirection,
            // Content is built from the dats as the world asks for it; a prebuilt package is an
            // explicit opt-in, never picked up off the disk by accident.
            PreparedAssetPath: e.Text("MACAC_PAK_PATH") ?? string.Empty,
            LiveMode: e.On("MACAC_LIVE"),
            LiveHost: environ("MACAC_TEST_HOST") ?? "127.0.0.1",
            LivePort: e.Int("MACAC_TEST_PORT") ?? 9000,
            LiveUser: e.Text("MACAC_TEST_USER"),
            LivePass: e.Text("MACAC_TEST_PASS"),
            DevTools: e.On("MACAC_DEVTOOLS"),
            // Normal presentation is always bounded by VSync or a refresh-rate
            // software pacer; this diagnostic is the only way to measure truly
            // uncapped renderer throughput.
            UncappedRendering: e.On("MACAC_UNCAPPED_RENDER"),
            DumpMoveTruth: e.On("MACAC_DUMP_MOVE_TRUTH"),
            DumpSky: e.On("MACAC_DUMP_SKY"),
            DumpWalkTranscript: e.On("MACAC_DUMP_WALK_TRANSCRIPT"),
            NoAudio: e.On("MACAC_NO_AUDIO"),
            HidePartIndex: e.Int("MACAC_HIDE_PART") ?? -1,
            RetailCloseDegrades: e.NotOff("MACAC_RETAIL_CLOSE_DEGRADES"),
            DumpSceneryZ: e.On("MACAC_DUMP_SCENERY_Z"),
            LegacyStreamRadius: e.Count("MACAC_STREAM_RADIUS"),
            RetailUi: e.NotOff("MACAC_RETAIL_UI"),
            OpenCharacterCreationOnStart: e.On("MACAC_OPEN_CHARGEN"),
            AcDir: e.Text("MACAC_AC_DIR"),
            UiProbeDump: e.On("MACAC_UI_PROBE_DUMP"),
            UiProbeScript: e.Text("MACAC_UI_PROBE_SCRIPT"),
            AutomationArtifactDirectory: e.Text("MACAC_AUTOMATION_ARTIFACT_DIR"),
            ExactAutomationFramebuffer: e.On("MACAC_AUTOMATION_EXACT_FRAMEBUFFER"),
            ForcedDayGroupIndex: e.Count("MACAC_DAY_GROUP"),
            PinnedWorldDayFraction: e.Float("MACAC_WORLD_TIME", static v => v is >= 0f and < 1f),
            SkyAnimationPhaseSeconds: e.Float("MACAC_SKY_PHASE_SECONDS"),
            InitialOrbitDistanceMeters: e.Float("MACAC_ORBIT_DISTANCE_METERS", static v => float.IsFinite(v) && v > 0f),
            InitialOrbitYawDegrees: e.Float("MACAC_ORBIT_YAW_DEGREES", float.IsFinite),
            InitialOrbitPitchDegrees: e.Float("MACAC_ORBIT_PITCH_DEGREES", static v => float.IsFinite(v) && v is >= -89f and <= 89f),
            ResidencyBudgets: TenancyAllowanceKnobs.Decode(environ),
            StreamingWorkBudgets: PagingWorkAllowanceKnobs.Decode(environ),
            VulkanDeviceOverride: e.Text("MACAC_VULKAN_DEVICE"),
            VulkanForcedUnsupportedFeature: e.Text("MACAC_VULKAN_FORCE_UNSUPPORTED"),
            VulkanCapabilityProbe: e.On("MACAC_VULKAN_PROBE"),
            VulkanCapabilityProbeFrames: e.Count("MACAC_VULKAN_PROBE_FRAMES") ?? 0,
            SessionConfigPath: null,
            SessionId: null,
            LiveCharacterSelector: null,
            StatusFilePath: null,
            Plugins: null,
            LoginCommands: [],
            LoginCommandDelayMs: 500)
        {
            ExtensionTags = ExtensionTagRoster(environ("MACAC_PLUGIN_TAGS")),
            VtankProfileFolderOverride = e.Text("MACAC_VTANK_PROFILE_DIR"),
        };
    }

    internal static EngineKnobs FromSessSettings(
        string datDirection,
        Func<string, string?> environ,
        string sessSettingsTrail,
        SessionSetup config,
        SessionSpec session,
        string? settledPassword)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (session is null) throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessSettingsTrail);

        EngineKnobs baseKnobs = Parse(datDirection, environ);
        var substance = config.Process?.Content;
        return baseKnobs with
        {
            PreparedAssetPath = Env.Blank(substance?.PreparedAssetPath)
                ?? baseKnobs.PreparedAssetPath,
            ReadiedAssetTopLayerTrail =
                Env.Blank(substance?.PreparedAssetOverlayPath),
            ReadiedAssetBaseRecipeVer =
                substance?.PreparedAssetBaseRecipeVersion,
            ReadiedAssetNetRecipeVer =
                substance?.PreparedAssetEffectiveRecipeVersion,
            LiveMode = true,
            LiveHost = session.Endpoint.Host,
            LivePort = session.Endpoint.Port,
            LiveUser = session.Account,
            LivePass = settledPassword,
            SessionConfigPath = sessSettingsTrail,
            SessionId = session.Id,
            LiveCharacterSelector = ToonChoose(session.Character),
            StatusFilePath = Env.Blank(session.StatusFile),
            Plugins = session.Plugins,
            LoginCommands = (IReadOnlyList<string>?)session.LoginCommands ?? [],
            LoginCommandDelayMs = session.LoginCommandDelayMs,
        };
    }

    private static OnlineSessionToonSelector? ToonChoose(SessionToonSelectorSpec? selector)
    {
        return selector is null ? null : new OnlineSessionToonSelector(selector.Index, selector.Id, selector.Name);
    }

    private static readonly PropertyInfo[] PrintableProps =
        [.. typeof(EngineKnobs)
            .GetProperties(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.DeclaredOnly)
            .Where(static prop =>
                prop.GetMethod is not null
                && prop.GetIndexParameters().Length is 0)
            .OrderBy(static prop => prop.MetadataToken)];

    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        for (int ordinal = 0; ordinal < PrintableProps.Length; ++ordinal)
        {
            var prop = PrintableProps[ordinal];
            if (ordinal is not 0)
                builder.Append(", ");
            builder.Append(prop.Name);
            builder.Append(" = ");
            builder.Append(
                prop.Name == nameof(LivePass) && LivePass is not null
                    ? "<redacted>"
                    : prop.GetValue(this));
        }
        return PrintableProps.Length is not 0;
    }

    public bool HasLiveCredentials
    {
        get
        {
            return LiveMode && !string.IsNullOrEmpty(LiveUser) && !string.IsNullOrEmpty(LivePass);
        }
    }

    public bool WidgetSensorTurnedOn => UiProbeDump || !string.IsNullOrEmpty(UiProbeScript);

    private static IReadOnlyList<string> ExtensionTagRoster(string? val)
    {
        return (val ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static tag => tag.Length <= 128)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToArray();
    }

    // Typed readers over the environment: "1" is on, "0" is off, blanks are absent, numbers are
    // invariant
    private readonly struct Env(Func<string, string?> scan)
    {
        public bool On(string label) => string.Equals(scan(label), "1", StringComparison.Ordinal);

        public bool NotOff(string label) => !string.Equals(scan(label), "0", StringComparison.Ordinal);

        public string? Text(string label) => Blank(scan(label));

        public int? Int(string label)
        {
            return int.TryParse(scan(label), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
        }

        // A non-negative integer
        public int? Count(string label) => Int(label) is { } v && v >= 0 ? v : null;

        public float? Float(string label, Func<float, bool>? admit = null)
        {
            return float.TryParse(scan(label), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            && (admit is null || admit(v))
                ? v
                : null;
        }

        public static string? Blank(string? s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
