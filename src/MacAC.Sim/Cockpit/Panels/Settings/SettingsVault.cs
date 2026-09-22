using System.Text.Json;
using MacAC.Cockpit.Settings;

namespace MacAC.Cockpit.Panels.Settings;

public readonly record struct UiWindowOffset(float X, float Y);

public sealed partial class SettingsVault(string path)
{
    private const int LatestSchemaVer = 3;
    private readonly string _trail = path ?? throw new ArgumentNullException(nameof(path));

    public ReadoutPrefs LoadDisplay()
    {
        return Section("display", ReadoutPrefs.Default, static (trunk, disp) =>
    {
        var settings = ReadoutPrefs.Default;
        float fov = Field.Float(disp, "fieldOfView", settings.FieldOfView);
        if (SchemaVersion(trunk) < 3 && disp.TryGetProperty("fieldOfView", out _))
            fov = MigrateLegacyVerticalFovDeg(fov);
        return new ReadoutPrefs(
            Resolution: Field.Text(disp, "resolution", settings.Resolution),
            Fullscreen: Field.Flag(disp, "fullscreen", settings.Fullscreen),
            VSync: Field.Flag(disp, "vsync", settings.VSync),
            FieldOfView: fov,
            Gamma: Field.Float(disp, "gamma", settings.Gamma),
            ShowFps: Field.Flag(disp, "showFps", settings.ShowFps),
            Quality: Field.Quality(disp, "quality", settings.Quality),
            ParticleRange: Field.Motes(disp, "particleRange", settings.ParticleRange),
            ScreenBrightness: Field.Float(disp, "screenBrightness", settings.ScreenBrightness),
            AutomaticDegrades: Field.Flag(disp, "automaticDegrades", settings.AutomaticDegrades),
            GraphicsPerformance: Field.Float(disp, "graphicsPerformance", settings.GraphicsPerformance),
            DegradeDistance: Field.Float(disp, "degradeDistance", settings.DegradeDistance),
            LandscapeTextureDetail: Field.Int(disp, "landscapeTextureDetail", settings.LandscapeTextureDetail),
            EnvironmentTextureDetail: Field.Int(disp, "environmentTextureDetail", settings.EnvironmentTextureDetail),
            TextureFiltering: Field.Int(disp, "textureFiltering", settings.TextureFiltering),
            LandscapeDrawDistance: Field.Int(disp, "landscapeDrawDistance", settings.LandscapeDrawDistance),
            BuildingDetailTextures: Field.Flag(disp, "buildingDetailTextures", settings.BuildingDetailTextures),
            MultiPassAlpha: Field.Flag(disp, "multiPassAlpha", settings.MultiPassAlpha))
        {
            RenderPack = RenderPackCodec.Read(disp, settings.RenderPack),
        };
    });
    }

    public void PersistDisplay(ReadoutPrefs settings)
    {
        PersistSection("display", new(StringComparer.Ordinal)
        {
            ["automaticDegrades"] = settings.AutomaticDegrades,
            ["buildingDetailTextures"] = settings.BuildingDetailTextures,
            ["degradeDistance"] = settings.DegradeDistance,
            ["environmentTextureDetail"] = settings.EnvironmentTextureDetail,
            ["fieldOfView"] = settings.FieldOfView,
            ["fullscreen"] = settings.Fullscreen,
            ["gamma"] = settings.Gamma,
            ["graphicsPerformance"] = settings.GraphicsPerformance,
            ["landscapeDrawDistance"] = settings.LandscapeDrawDistance,
            ["landscapeTextureDetail"] = settings.LandscapeTextureDetail,
            ["multiPassAlpha"] = settings.MultiPassAlpha,
            ["particleRange"] = settings.ParticleRange.ToString(),
            ["quality"] = settings.Quality.ToString(),
            ["renderPack"] = RenderPackCodec.Write(settings.RenderPack),
            ["resolution"] = settings.Resolution,
            ["screenBrightness"] = settings.ScreenBrightness,
            ["showFps"] = settings.ShowFps,
            ["textureFiltering"] = settings.TextureFiltering,
            ["vsync"] = settings.VSync,
        });
    }

    public SoundPrefs LoadAudio()
    {
        return Section("audio", SoundPrefs.Default, static (_, sound) =>
    {
        var settings = SoundPrefs.Default;
        return new SoundPrefs(
            Master: Field.Float(sound, "master", settings.Master),
            Sfx: Field.Float(sound, "sfx", settings.Sfx),
            Ambient: Field.Float(sound, "ambient", settings.Ambient),
            SoundFeatures: Field.Int(sound, "soundFeatures", settings.SoundFeatures),
            SfxEnabled: Field.Flag(sound, "sfxEnabled", settings.SfxEnabled),
            AmbientEnabled: Field.Flag(sound, "ambientEnabled", settings.AmbientEnabled),
            InterfaceEnabled: Field.Flag(sound, "interfaceEnabled", settings.InterfaceEnabled),
            InterfaceVolume: Field.Float(sound, "interfaceVolume", settings.InterfaceVolume),
            PlaySoundOnlyWhenActive: Field.Flag(sound, "playSoundOnlyWhenActive", settings.PlaySoundOnlyWhenActive));
    });
    }

    public void PersistAudio(SoundPrefs settings)
    {
        PersistSection("audio", new(StringComparer.Ordinal)
        {
            ["ambient"] = settings.Ambient,
            ["ambientEnabled"] = settings.AmbientEnabled,
            ["interfaceEnabled"] = settings.InterfaceEnabled,
            ["interfaceVolume"] = settings.InterfaceVolume,
            ["master"] = settings.Master,
            ["playSoundOnlyWhenActive"] = settings.PlaySoundOnlyWhenActive,
            ["sfx"] = settings.Sfx,
            ["sfxEnabled"] = settings.SfxEnabled,
            ["soundFeatures"] = settings.SoundFeatures,
        });
    }

    public CommsPrefs LoadChat()
    {
        return Section("chat", CommsPrefs.Default, static (_, comms) =>
    {
        var settings = CommsPrefs.Default;
        return new CommsPrefs(
            HearGeneralChat: Field.Flag(comms, "hearGeneralChat", settings.HearGeneralChat),
            HearTradeChat: Field.Flag(comms, "hearTradeChat", settings.HearTradeChat),
            HearLFGChat: Field.Flag(comms, "hearLFGChat", settings.HearLFGChat),
            HearRoleplayChat: Field.Flag(comms, "hearRoleplayChat", settings.HearRoleplayChat),
            HearSocietyChat: Field.Flag(comms, "hearSocietyChat", settings.HearSocietyChat),
            AppearOffline: Field.Flag(comms, "appearOffline", settings.AppearOffline),
            ShowTimestamps: Field.Flag(comms, "showTimestamps", settings.ShowTimestamps),
            FilterProfanity: Field.Flag(comms, "filterProfanity", settings.FilterProfanity),
            FontSize: Field.Float(comms, "fontSize", settings.FontSize),
            ChatWindow1Filter: Field.ULong(comms, "chatWindow1Filter", settings.ChatWindow1Filter),
            ChatWindow2Filter: Field.ULong(comms, "chatWindow2Filter", settings.ChatWindow2Filter),
            ChatWindow3Filter: Field.ULong(comms, "chatWindow3Filter", settings.ChatWindow3Filter),
            ChatWindow4Filter: Field.ULong(comms, "chatWindow4Filter", settings.ChatWindow4Filter),
            ChatWindowMainFilter: Field.ULong(comms, "chatWindowMainFilter", settings.ChatWindowMainFilter),
            DefaultOpacity: Field.Float(comms, "defaultOpacity", settings.DefaultOpacity),
            ActiveOpacity: Field.Float(comms, "activeOpacity", settings.ActiveOpacity),
            ChatFontFace: Field.Int(comms, "chatFontFace", settings.ChatFontFace),
            ChatFontSizeIndex: Field.Int(comms, "chatFontSizeIndex", settings.ChatFontSizeIndex));
    });
    }

    public void PersistChat(CommsPrefs settings)
    {
        PersistSection("chat", new(StringComparer.Ordinal)
        {
            ["activeOpacity"] = settings.ActiveOpacity,
            ["appearOffline"] = settings.AppearOffline,
            ["chatFontFace"] = settings.ChatFontFace,
            ["chatFontSizeIndex"] = settings.ChatFontSizeIndex,
            ["chatWindow1Filter"] = settings.ChatWindow1Filter,
            ["chatWindow2Filter"] = settings.ChatWindow2Filter,
            ["chatWindow3Filter"] = settings.ChatWindow3Filter,
            ["chatWindow4Filter"] = settings.ChatWindow4Filter,
            ["chatWindowMainFilter"] = settings.ChatWindowMainFilter,
            ["defaultOpacity"] = settings.DefaultOpacity,
            ["filterProfanity"] = settings.FilterProfanity,
            ["fontSize"] = settings.FontSize,
            ["hearGeneralChat"] = settings.HearGeneralChat,
            ["hearLFGChat"] = settings.HearLFGChat,
            ["hearRoleplayChat"] = settings.HearRoleplayChat,
            ["hearSocietyChat"] = settings.HearSocietyChat,
            ["hearTradeChat"] = settings.HearTradeChat,
            ["showTimestamps"] = settings.ShowTimestamps,
        });
    }

    public CameraTurnSettings PullCameraTurning()
    {
        return Section("cameraTurning", CameraTurnSettings.Default, static (_, element) =>
    {
        var settings = CameraTurnSettings.Default;
        return new CameraTurnSettings(
            Stiffness: Field.Float(element, "stiffness", settings.Stiffness),
            AdjustmentSpeed: Field.Float(element, "adjustmentSpeed", settings.AdjustmentSpeed),
            MouseLookSensitivity: Field.Float(element, "mouseLookSensitivity", settings.MouseLookSensitivity),
            AlignToSlope: Field.Flag(element, "alignToSlope", settings.AlignToSlope),
            InvertMouseLookYAxis: Field.Flag(element, "invertMouseLookYAxis", settings.InvertMouseLookYAxis),
            UseMouseTurning: Field.Flag(element, "useMouseTurning", settings.UseMouseTurning));
    });
    }

    public void PersistCameraTurning(CameraTurnSettings settings)
    {
        PersistSection("cameraTurning", new(StringComparer.Ordinal)
        {
            ["adjustmentSpeed"] = settings.AdjustmentSpeed,
            ["alignToSlope"] = settings.AlignToSlope,
            ["invertMouseLookYAxis"] = settings.InvertMouseLookYAxis,
            ["mouseLookSensitivity"] = settings.MouseLookSensitivity,
            ["stiffness"] = settings.Stiffness,
            ["useMouseTurning"] = settings.UseMouseTurning,
        });
    }

    public MiscPrefs PullMisc()
    {
        return Section("misc", MiscPrefs.Default, static (_, misc) =>
    {
        var settings = MiscPrefs.Default;
        return new MiscPrefs(
            TooltipEnable: Field.Flag(misc, "tooltipEnable", settings.TooltipEnable),
            TooltipDelaySeconds: Field.Float(misc, "tooltipDelaySeconds", settings.TooltipDelaySeconds));
    });
    }

    /// <summary>Saves the tooltip preferences, leaving every other top-level key alone.</summary>
    public void PersistMisc(MiscPrefs settings)
    {
        PersistSection("misc", new(StringComparer.Ordinal)
        {
            ["tooltipDelaySeconds"] = settings.TooltipDelaySeconds,
            ["tooltipEnable"] = settings.TooltipEnable,
        });
    }

    internal static float MigrateLegacyVerticalFovDeg(float legacyVerticalFovDeg)
    {
        return legacyVerticalFovDeg == 60f
            ? 90f
            : Math.Clamp(legacyVerticalFovDeg * (16f / 9f - 0.1f), 10f, 160f);
    }

    // Reads one top-level object section, handing the reader both the root (for the schema version)
    // and the section
    private T Section<T>(string label, T backup, Func<JsonElement, JsonElement, T> scan)
    {
        if (!File.Exists(_trail))
            return backup;
        try
        {
            using FileStream flow = File.OpenRead(_trail);
            using var doc = JsonDocument.Parse(flow);
            JsonElement trunk = doc.RootElement;
            return trunk.TryGetProperty(label, out JsonElement section) && section.ValueKind == JsonValueKind.Object
                ? scan(trunk, section)
                : backup;
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: could not load {_trail}: {exc.Message} - using defaults");
            return backup;
        }
    }

    private static int SchemaVersion(JsonElement trunk)
    {
        return trunk.TryGetProperty("version", out JsonElement element) && element.ValueKind == JsonValueKind.Number ? element.GetInt32() : 1;
    }
}
