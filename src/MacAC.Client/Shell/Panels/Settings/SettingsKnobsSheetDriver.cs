using System.Globalization;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Extensibility.RenderPacks;

namespace MacAC.Client.Shell.Panels;

public static partial class SettingsKnobsSheetDriver
{
    public const uint RootElementId = 0x100001FFu;

    private const uint SheetSocketElemIdent = 0x10000213u;

    /// <summary>The row ListBox (dat Type 5) - <c>m_pOptionBox</c>.</summary>
    public const uint RosterBoxElementId = 0x10000200u;

    public const uint ScrollbarElementId = 0x10000201u;

    private const int PreambleBlueprintOrdinal = 0;

    private const int SeparatorBlueprintOrdinal = 1;

    private const int FlipBlueprintOrdinal = 2;

    private const int SimpleDialBlueprintOrdinal = 3;

    private const int MenuBlueprintOrdinal = 4;

    private const int TrioBlueprintOrdinal = 5;

    private const int RangedDialBlueprintOrdinal = 6;

    private const uint StringChartIdent = 0x23000003u;

    private const uint DialCaptionElemIdent = 0x1000021Bu;

    private const uint DialElemIdent = 0x1000021Cu;

    private const uint DialSpanLowerElemIdent = 0x1000021Eu;

    private const uint DialSpanUpperElemIdent = 0x1000021Fu;

    private const uint MenuCaptionElemIdent = 0x10000223u;

    private const uint MenuElemIdent = 0x10000224u;

    private const uint FlipTickboxElemIdent = 0x10000219u;

    private static class MenuChromeDecals
    {
        public const uint Normal = 0x060012B3u;
        public const uint Pressed = 0x060012B4u;
        public const uint GearNorm = 0x060012B3u;
        public const uint GearHighlight = 0x060012B4u;
        public const uint ArrowCapClosed = 0x060012B1u;
        public const uint ArrowCapOpen = 0x060012B2u;

        public const int RanksPerColumn = 6;
        public const float RankHeight = 18f;
        public const float ColumnWidth = 100f;

        public const float ScrollerWidth = 16f;
        public const float RollBtnReach = 16f;
        public const uint RollFollow = 0x06004C5Fu;
        public const uint RollThumbTop = 0x06004C60u;
        public const uint RollThumb = 0x06004C63u;
        public const uint RollThumbBottom = 0x06004C66u;
        public const uint ScrollUp = CanonScrollbarChrome.UpNorm;
        public const uint ScrollDown = CanonScrollbarChrome.DownNorm;
    }

    public sealed record Bindings(
        Func<ReadoutPrefs> LoadDisplay,
        Action<ReadoutPrefs> SaveDisplay,
        Func<SoundPrefs> LoadAudio,
        Action<SoundPrefs> SaveAudio,
        Func<CameraTurnSettings> LoadCameraTurning,
        Action<CameraTurnSettings> SaveCameraTurning,
        Func<CommsPrefs> LoadChat,
        Action<CommsPrefs> SaveChat)
    {
        public RenderPackWiring? RenderPacks { get; init; }
    }

    public sealed record RenderPackWiring(
        Func<IReadOnlyList<RasterizeBundlePick>> LoadChoices)
    {
        public Func<long>? PullRev { get; init; }
        public Func<string?>? PullMissNotice { get; init; }
    }

    public sealed record RasterizeBundlePick(
        string Id,
        string DisplayName,
        string? Version,
        bool Selectable,
        string? UnavailableReason,
        IReadOnlyList<RasterizeBundlePresetPick> Presets)
    {
        public string FeatureSummary { get; init; } = string.Empty;
        public IReadOnlyList<RenderSettingSpec> Prefs { get; init; } = [];
    }

    public sealed record RasterizeBundlePresetPick(
        string Id,
        string DisplayName,
        bool Selectable,
        string? UnavailableReason)
    {
        public IReadOnlyList<QualitySettingTweak> SettingSubstitutions { get; init; } = [];
        public long UpperHousedGpuOctets { get; init; }
        public double UpperIncrementalGpuMillisP50 { get; init; }
        public double UpperIncrementalGpuMillisP99 { get; init; }
        public double UpperIncrementalCpuMillisP50 { get; init; }
        public double UpperIncrementalCpuMillisP99 { get; init; }
    }

    private readonly record struct ExplicitMenuPick(
        string Id,
        string Label,
        bool Enabled,
        string? Tooltip);

    private sealed class ExplicitMenuPickOrigin(
        IReadOnlyList<ExplicitMenuPick> choices)
    {
        internal IReadOnlyList<ExplicitMenuPick> Choices { get; set; } = choices;
    }

    private sealed class RasterizeBundleRearHolder(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Bindings mappings,
        int keptGearTally,
        int keptKnobTally,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        private readonly int _keptGearTally = keptGearTally;
        private readonly int _keptKnobTally = keptKnobTally;
        private int _bundleGen;
        private int _prefsGen;
        private int _prefsGearTally;
        private int _prefsKnobTally;

        internal void ReassembleBundleRear(RasterizeBundlePick bundle)
        {
            int gen = ++_bundleGen;
            ++_prefsGen;
            rosterBbox.DeleteRear(_keptGearTally);
            sheet.DropRear(_keptKnobTally);

            var readout = mappings.LoadDisplay();
            string presetIdent = StandardizePresetIdent(readout.RenderPack.PresetId, bundle);
            var preset = bundle.Presets.First(val =>
                string.Equals(val.Id, presetIdent, StringComparison.OrdinalIgnoreCase));
            AssembleExplicitStringMenuRank(
                rosterBbox,
                "Quality preset",
                bundle.Presets.Select(static val =>
                    new ExplicitMenuPick(
                        val.Id,
                        val.DisplayName,
                        val.Selectable,
                        PresetHint(val))).ToArray(),
                sheet,
                scan: () => StandardizePresetIdent(
                    mappings.LoadDisplay().RenderPack.PresetId,
                    bundle),
                enact: chosenPresetIdent =>
                {
                    if (gen != _bundleGen)
                        return;
                    var chosen = bundle.Presets.FirstOrDefault(val =>
                        val.Selectable && string.Equals(
                            val.Id,
                            chosenPresetIdent,
                            StringComparison.OrdinalIgnoreCase));
                    var latest = mappings.LoadDisplay();
                    if (chosen is null || !SameBundle(latest.RenderPack, bundle))
                        return;
                    mappings.SaveDisplay(latest with
                    {
                        RenderPack = latest.RenderPack with
                        {
                            PresetId = chosen.Id,
                            SettingOverrides = SanitizeSubstitutions(
                                bundle,
                                latest.RenderPack.SettingOverrides),
                        },
                    });
                    ReassemblePrefsRear(bundle, chosen);
                },
                defaultVal: preset.Id,
                locateSprite,
                datTypeface,
                diagTypeface);

            _prefsGearTally = rosterBbox.GearCount;
            _prefsKnobTally = sheet.Rows.Count;
            ReassemblePrefsRear(bundle, preset);
        }

        private void ReassemblePrefsRear(
            RasterizeBundlePick bundle,
            RasterizeBundlePresetPick preset)
        {
            int gen = ++_prefsGen;
            rosterBbox.DeleteRear(_prefsGearTally);
            sheet.DropRear(_prefsKnobTally);

            HashSet<string> idents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RenderSettingSpec setting in bundle.Prefs)
            {
                if (!idents.Add(setting.Id) || !CanAssembleSetting(setting))
                    continue;
                AssembleSetting(bundle, preset, setting, gen);
            }
            AssembleSeparatorRank(rosterBbox);
        }

        private void AssembleSetting(
            RasterizeBundlePick bundle,
            RasterizeBundlePresetPick preset,
            RenderSettingSpec setting,
            int gen)
        {
            bool IsLatest() => gen == _prefsGen
                && SameBundle(mappings.LoadDisplay().RenderPack, bundle)
                && string.Equals(
                    mappings.LoadDisplay().RenderPack.PresetId,
                    preset.Id,
                    StringComparison.OrdinalIgnoreCase);
            string Scan() => LocateSettingVal(
                bundle,
                preset,
                setting,
                mappings.LoadDisplay().RenderPack.SettingOverrides);
            string defaultVal = LocateSettingVal(
                bundle,
                preset,
                setting,
                RenderPackOverrides.Empty);
            void Impose(string val)
            {
                if (!IsLatest()
                    || !SettingValueCodec.TryPack(setting, val, out _))
                    return;
                var latest = mappings.LoadDisplay();
                var clean = SanitizeSubstitutions(
                    bundle,
                    latest.RenderPack.SettingOverrides);
                mappings.SaveDisplay(latest with
                {
                    RenderPack = latest.RenderPack with
                    {
                        SettingOverrides = clean.Set(setting.Id, val),
                    },
                });
            }

            switch (setting.Kind)
            {
                case SettingValueKind.Boolean:
                    AssembleExplicitFlipRank(
                        rosterBbox,
                        setting.DisplayName,
                        bool.Parse(defaultVal),
                        sheet,
                        scan: () => bool.Parse(Scan()),
                        enact: val => Impose(val ? "true" : "false"),
                        IsLatest);
                    break;

                case SettingValueKind.Float:
                case SettingValueKind.Integer:
                    double lower = setting.Minimum!.Value;
                    double upper = setting.Maximum!.Value;
                    double hop = setting.Step
                        ?? (setting.Kind == SettingValueKind.Integer ? 1d : 0d);
                    AssembleExplicitNumericDialRank(
                        rosterBbox,
                        setting.DisplayName,
                        lower,
                        upper,
                        hop,
                        setting.Kind == SettingValueKind.Integer,
                        double.Parse(defaultVal, CultureInfo.InvariantCulture),
                        sheet,
                        scan: () => double.Parse(Scan(), CultureInfo.InvariantCulture),
                        enact: val => Impose(ComposeNumeric(val, setting.Kind)),
                        IsLatest);
                    break;

                case SettingValueKind.Choice:
                    AssembleExplicitStringMenuRank(
                        rosterBbox,
                        setting.DisplayName,
                        setting.Choices.Select(static val =>
                            new ExplicitMenuPick(val, val, true, null)).ToArray(),
                        sheet,
                        scan: Scan,
                        enact: val =>
                        {
                            if (IsLatest()) Impose(val);
                        },
                        defaultVal,
                        locateSprite,
                        datTypeface,
                        diagTypeface);
                    break;
            }
        }

        private static bool CanAssembleSetting(RenderSettingSpec setting)
        {
            if (string.IsNullOrWhiteSpace(setting.Id)
                || string.IsNullOrWhiteSpace(setting.DisplayName)
                || !SettingValueCodec.TryPack(
                    setting,
                    setting.DefaultValue,
                    out _))
                return false;
            return setting.Kind is SettingValueKind.Float or SettingValueKind.Integer
                ? setting.Minimum is { } lower
                    && setting.Maximum is { } upper
                    && double.IsFinite(lower)
                    && double.IsFinite(upper)
                    && lower >= -float.MaxValue
                    && upper <= float.MaxValue
                    && upper > lower
                    && (setting.Step is null
                        || double.IsFinite(setting.Step.Value) && setting.Step.Value > 0)
                : setting.Kind is SettingValueKind.Boolean
                || setting.Kind == SettingValueKind.Choice && setting.Choices.Count > 0;
        }

        private static string LocateSettingVal(
            RasterizeBundlePick bundle,
            RasterizeBundlePresetPick preset,
            RenderSettingSpec setting,
            IReadOnlyDictionary<string, string> userSubstitutions)
        {
            if (TryGet(userSubstitutions, setting.Id, out string? user)
                && SettingValueCodec.TryPack(setting, user, out _))
                return user;
            var presetVal = preset.SettingSubstitutions
                .FirstOrDefault(val => string.Equals(
                    val.SettingId,
                    setting.Id,
                    StringComparison.OrdinalIgnoreCase));
            return presetVal is not null
                && SettingValueCodec.TryPack(setting, presetVal.Value, out _)
                ? presetVal.Value
                : setting.DefaultValue;
        }

        private static RenderPackOverrides SanitizeSubstitutions(
            RasterizeBundlePick bundle,
            IReadOnlyDictionary<string, string> substitutions)
        {
            var valid = new List<KeyValuePair<string, string>>();
            foreach ((string ident, string val) in substitutions)
            {
                var setting = bundle.Prefs.FirstOrDefault(contender =>
                    string.Equals(contender.Id, ident, StringComparison.OrdinalIgnoreCase));
                if (setting is not null
                    && SettingValueCodec.TryPack(setting, val, out _))
                    valid.Add(new KeyValuePair<string, string>(setting.Id, val));
            }
            return new RenderPackOverrides(valid);
        }

        private static bool TryGet(
            IReadOnlyDictionary<string, string> vals,
            string ident,
            out string val)
        {
            if (vals.TryGetValue(ident, out val!))
                return true;
            foreach ((string tag, string contender) in vals)
            {
                if (string.Equals(tag, ident, StringComparison.OrdinalIgnoreCase))
                {
                    val = contender;
                    return true;
                }
            }
            val = string.Empty;
            return false;
        }

        private static bool SameBundle(
            RenderPackPick pick,
            RasterizeBundlePick bundle)
        {
            return string.Equals(pick.PackId, bundle.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pick.PackVersion, bundle.Version, StringComparison.Ordinal);
        }

        private static string ComposeNumeric(double val, SettingValueKind sort)
        {
            return sort == SettingValueKind.Integer
                ? checked((long)Math.Round(val)).ToString(CultureInfo.InvariantCulture)
                : val.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    private static readonly string[] TextureSpecificsChoices =
    [
        "ID_Graphics_Value_VeryLow", "ID_Graphics_Value_Low", "ID_Graphics_Value_Medium",
        "ID_Graphics_Value_High", "ID_Graphics_Value_VeryHigh",
    ];

    private static readonly string[] TextureFilteringChoices =
    [
        "ID_Graphics_TextureFiltering_Bilinear", "ID_Graphics_TextureFiltering_Trilinear",
        "ID_Graphics_TextureFiltering_Sharp", "ID_Graphics_TextureFiltering_Anisotropic",
    ];

    private static readonly string[] SceneryPaintGapChoices =
    [
        "ID_Graphics_Value_VeryLow", "ID_Graphics_Value_Low", "ID_Graphics_Value_Medium",
        "ID_Graphics_Value_High", "ID_Graphics_Value_VeryHigh", "ID_Graphics_Value_Extreme",
    ];

    private static readonly int[] SceneryPaintGapVals =
    [
        3, 5, 8, 11, 15, 25,
    ];

    private static readonly string[] CommsTypefaceFaceChoices =
    [
        "ID_UI_Value_Arial", "ID_UI_Value_CourierNew", "ID_UI_Value_PalatinoLinotype",
        "ID_UI_Value_Tahoma", "ID_UI_Value_TimesNewRoman",
    ];

    private static readonly string[] CommsTypefaceDimsChoices =
    [
        "ID_UI_Value_Tiny", "ID_UI_Value_Small", "ID_UI_Value_Medium",
        "ID_UI_Value_Large", "ID_UI_Value_XLarge",
    ];
}
