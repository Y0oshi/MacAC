using MacAC.Client.Graphics;
using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Shell.Panels;

public static partial class SettingsKnobsSheetDriver
{
    public static bool Bind(
        ImportedArrangement arrangement,
        KnobPage sheet,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        Func<uint, (uint tex, int w, int h)>? locateSprite = null,
        WidgetDatFont? datTypeface = null,
        BitmapFont? diagTypeface = null,
        IReadOnlyList<string>? onHandResolutions = null,
        string? resolutionDefault = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(locateString);
        ArgumentNullException.ThrowIfNull(mappings);

        if (arrangement.SeekElem(RosterBoxElementId) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: ListBox 0x{RosterBoxElementId:X8} "
                + "not found (or not a WidgetBlueprintRosterBbox) in the built Options panel tree - "
                + "the Config tab will have no rows");
            return false;
        }

        rosterBbox.TemplateResolver = blueprintLocator;

        uint scrollerElemIdent = rosterBbox.ScrollbarElementId;
        WidgetElem? settingsSheetSocket = arrangement.SeekElem(SheetSocketElemIdent);
        WidgetElem? scrollerElem = settingsSheetSocket is null || scrollerElemIdent is 0
            ? null
            : WidgetElem.SeekDescendant(settingsSheetSocket, scrollerElemIdent);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: scrollbar 0x{scrollerElemIdent:X8} "
                + $"not found under Config page slot 0x{SheetSocketElemIdent:X8} - the Config "
                + "tab's row list will not scroll");

        ReadoutPrefs readout = mappings.LoadDisplay();
        SoundPrefs sound = mappings.LoadAudio();
        CameraTurnSettings camTurning = mappings.LoadCameraTurning();
        CommsPrefs comms = mappings.LoadChat();

        AttachSfxSection(rosterBbox, sheet, locateString, mappings, ref sound, locateSprite, datTypeface, diagTypeface);
        AssembleSeparatorRank(rosterBbox);
        AttachCamSection(rosterBbox, sheet, locateString, mappings, ref camTurning);
        AssembleSeparatorRank(rosterBbox);
        AttachVisualsSection(
            rosterBbox, sheet, locateString, mappings, ref readout,
            locateSprite, datTypeface, diagTypeface,
            onHandResolutions, resolutionDefault);
        AssembleSeparatorRank(rosterBbox);
        AttachRenderingFidelitySection(rosterBbox, sheet, locateString, mappings, ref readout, locateSprite, datTypeface, diagTypeface);
        AssembleSeparatorRank(rosterBbox);
        AttachFeedSection(rosterBbox, sheet, locateString, mappings, ref camTurning);
        AssembleSeparatorRank(rosterBbox);
        AttachWidgetSection(rosterBbox, sheet, locateString, mappings, ref comms, locateSprite, datTypeface, diagTypeface);
        AssembleSeparatorRank(rosterBbox);

        if (mappings.RenderPacks is not null)
            AttachRasterizeBundleSection(
                rosterBbox,
                sheet,
                mappings,
                mappings.RenderPacks,
                locateSprite,
                datTypeface,
                diagTypeface);

        return true;
    }

    private static void AttachRasterizeBundleSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Bindings mappings,
        RenderPackWiring rasterizeBundles,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        List<RasterizeBundlePick> choices = PullBundleChoices(rasterizeBundles);
        long observedRev = rasterizeBundles.PullRev?.Invoke() ?? 0;
        ExplicitMenuPickOrigin menuChoices = new ExplicitMenuPickOrigin(
            choices.Select(static val =>
                new ExplicitMenuPick(
                    val.Id,
                    val.DisplayName,
                    val.Selectable,
                    BundleHint(val))).ToArray());

        AssembleExplicitPreambleRank(rosterBbox, "Graphics Enhancements");
        RasterizeBundleRearHolder? rear = null;
        StringKnobRow? bundleKnob = null;
        WidgetMenu? bundleMenu = AssembleExplicitStringMenuRank(
            rosterBbox,
            "Shader pack",
            menuChoices.Choices,
            sheet,
            scan: () => StandardizeBundleIdent(mappings.LoadDisplay().RenderPack, choices),
            enact: chosenIdent =>
            {
                var chosen = choices.FirstOrDefault(val =>
                    val.Selectable && string.Equals(
                        val.Id,
                        chosenIdent,
                        StringComparison.OrdinalIgnoreCase));
                var preset = chosen?.Presets.FirstOrDefault(
                    static val => val.Selectable);
                if (chosen is null || preset is null)
                    return;
                mappings.SaveDisplay(mappings.LoadDisplay() with
                {
                    RenderPack = new RenderPackPick(
                        chosen.Id,
                        chosen.Version,
                        preset.Id),
                });
                rear?.ReassembleBundleRear(chosen);
            },
            defaultVal: RenderPackPick.CanonBundleIdent,
            locateSprite,
            datTypeface,
            diagTypeface,
            menuChoices,
            knob => bundleKnob = knob);

        string startingBundleIdent = StandardizeBundleIdent(mappings.LoadDisplay().RenderPack, choices);
        var startingBundle = choices.First(val =>
            string.Equals(val.Id, startingBundleIdent, StringComparison.OrdinalIgnoreCase));
        rear = new RasterizeBundleRearHolder(
            rosterBbox,
            sheet,
            mappings,
            keptGearTally: rosterBbox.GearCount,
            keptKnobTally: sheet.Rows.Count,
            locateSprite,
            datTypeface,
            diagTypeface);
        rear.ReassembleBundleRear(startingBundle);
        if (bundleMenu is not null)
        {
            bundleMenu.PriorOpen = () =>
            {
                if (rasterizeBundles.PullRev is not { } pullRev)
                    return;
                long rev = pullRev();
                if (rev == observedRev)
                    return;
                observedRev = rev;
                choices = PullBundleChoices(rasterizeBundles);
                menuChoices.Choices = choices.Select(static val =>
                    new ExplicitMenuPick(
                        val.Id,
                        val.DisplayName,
                        val.Selectable,
                        BundleHint(val))).ToArray();
                bundleMenu.Items = menuChoices.Choices
                    .Select(static choice => new WidgetMenu.MenuGear(
                        choice.Label,
                        choice.Id))
                    .ToArray();
                string chosenTag = StandardizeBundleIdent(
                    mappings.LoadDisplay().RenderPack,
                    choices);
                bundleMenu.Selected = chosenTag;
                bundleMenu.TooltipText = menuChoices.Choices.FirstOrDefault(choice =>
                        string.Equals(
                            choice.Id,
                            chosenTag,
                            StringComparison.OrdinalIgnoreCase))
                    .Tooltip;
                var chosen = choices.First(val => string.Equals(
                    val.Id,
                    chosenTag,
                    StringComparison.OrdinalIgnoreCase));
                rear.ReassembleBundleRear(chosen);
                bundleKnob?.PersistLatestVal();
            };
            bundleMenu.HintPhraseSupplier = () => FuseHint(
                rasterizeBundles.PullMissNotice?.Invoke(),
                BundleHint(choices.FirstOrDefault(val => string.Equals(
                    val.Id,
                    bundleMenu.Selected as string,
                    StringComparison.OrdinalIgnoreCase))));
        }
    }

    private static void AttachSfxSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref SoundPrefs sound,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        AssemblePreambleRank(rosterBbox, "ID_Sound_SoundSection", locateString);

        AssembleMenuRank(
            rosterBbox, "ID_Sound_SoundFeatures",
            ["ID_Sound_Stereo", "ID_Sound_Mono"],
            sheet, locateString,
            scan: () => mappings.LoadAudio().SoundFeatures,
            enact: val =>
            {
                var updated = mappings.LoadAudio() with { SoundFeatures = val };
                mappings.SaveAudio(updated);
            },
            defaultVal: 0,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        AssembleTrioRank(
            rosterBbox, "ID_Sound_DisableSound", dialHintTag: "ID_Sound_EffectVolume",
            flipDefault: true,
            dialLower: 0f, dialUpper: 1f, dialDefault: 1.0f,
            sheet, locateString,
            flipScan: () => mappings.LoadAudio().SfxEnabled,
            flipEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { SfxEnabled = val }),
            dialScan: () => mappings.LoadAudio().Sfx,
            dialEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { Sfx = val }),
            vaultSole: false); // LIVE

        AssembleTrioRank(
            rosterBbox, "ID_Sound_DisableAmbientSound", dialHintTag: "ID_Sound_AmbientVolume",
            flipDefault: true,
            dialLower: 0f, dialUpper: 1f, dialDefault: 1.0f,
            sheet, locateString,
            flipScan: () => mappings.LoadAudio().AmbientEnabled,
            flipEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { AmbientEnabled = val }),
            dialScan: () => mappings.LoadAudio().Ambient,
            dialEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { Ambient = val }),
            vaultSole: false); // LIVE

        AssembleTrioRank(
            rosterBbox, "ID_Sound_DisableInterfaceSound", dialHintTag: "ID_Sound_InterfaceVolume",
            flipDefault: true,
            dialLower: 0f, dialUpper: 1f, dialDefault: 1.0f,
            sheet, locateString,
            flipScan: () => mappings.LoadAudio().InterfaceEnabled,
            flipEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { InterfaceEnabled = val }),
            dialScan: () => mappings.LoadAudio().InterfaceVolume,
            dialEnact: val => mappings.SaveAudio(mappings.LoadAudio() with { InterfaceVolume = val }),
            vaultSole: true);

        AssembleFlipRank(
            rosterBbox, "ID_Sound_NoFocusNoSound", defaultVal: true, sheet, locateString,
            scan: () => mappings.LoadAudio().PlaySoundOnlyWhenActive,
            enact: val => mappings.SaveAudio(mappings.LoadAudio() with { PlaySoundOnlyWhenActive = val }),
            vaultSole: true);

        sound = mappings.LoadAudio();
    }

    private static void AttachCamSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref CameraTurnSettings camTurning)
    {
        AssemblePreambleRank(rosterBbox, "ID_Camera_CameraSection", locateString);

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Camera_Stiffness",
            lower: 0.285714298f, upper: 1f, defaultVal: 0.45f, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().Stiffness,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { Stiffness = val }),
            vaultSole: true,
            spanLoTag: "ID_Graphics_Value_Soft", spanHiTag: "ID_Graphics_Value_Hard");

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Camera_AdjustmentSpeed",
            lower: 5f, upper: 80f, defaultVal: 40.0f, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().AdjustmentSpeed,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { AdjustmentSpeed = val }),
            vaultSole: true,
            spanLoTag: "ID_Graphics_Value_Slow", spanHiTag: "ID_Graphics_Value_Fast");

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Graphics_FieldOfView",
            lower: 10f, upper: 160f, defaultVal: 90.0f, sheet, locateString,
            scan: () => mappings.LoadDisplay().FieldOfView,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { FieldOfView = val }),
            vaultSole: false, // NEXT-LAUNCH, not store-only
            spanLoTag: "ID_Graphics_Value_Narrow", spanHiTag: "ID_Graphics_Value_Wide");

        AssembleFlipRank(
            rosterBbox, "ID_Camera_AlignToSlope", defaultVal: true, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().AlignToSlope,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { AlignToSlope = val }),
            vaultSole: true);

        camTurning = mappings.LoadCameraTurning();
    }

    private static void AttachVisualsSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref ReadoutPrefs readout,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        IReadOnlyList<string>? onHandResolutions,
        string? resolutionDefault)
    {
        AssemblePreambleRank(rosterBbox, "ID_Graphics_GraphicsSection", locateString);

        AssembleStringMenuRank(
            rosterBbox, "ID_Rendering_DisplayResolution",
            onHandResolutions ?? ReadoutPrefs.OnHandResolutions, sheet, locateString,
            scan: () => mappings.LoadDisplay().Resolution,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { Resolution = val }),
            defaultVal: resolutionDefault ?? ReadoutPrefs.Default.Resolution,
            vaultSole: false, // LIVE
            locateSprite, datTypeface, diagTypeface);

        AssembleFlipRank(
            rosterBbox, "ID_Rendering_FullScreen", defaultVal: true, sheet, locateString,
            scan: () => mappings.LoadDisplay().Fullscreen,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { Fullscreen = val }),
            vaultSole: false); // LIVE

        AssembleFlipRank(
            rosterBbox, "ID_Rendering_SyncToDisplayRefresh", defaultVal: false, sheet, locateString,
            scan: () => mappings.LoadDisplay().VSync,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { VSync = val }),
            vaultSole: false); // NEXT-LAUNCH, not store-only

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Graphics_ScreenBrightness",
            lower: -1f, upper: 1f, defaultVal: 0f, sheet, locateString,
            scan: () => mappings.LoadDisplay().ScreenBrightness,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { ScreenBrightness = val }),
            vaultSole: true, //
            spanLoTag: "ID_Graphics_Value_Dark", spanHiTag: "ID_Graphics_Value_Bright");

        AssembleFlipRank(
            rosterBbox, "ID_Graphics_AdaptiveDegrade", defaultVal: false, sheet, locateString,
            scan: () => mappings.LoadDisplay().AutomaticDegrades,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { AutomaticDegrades = val }),
            vaultSole: false);

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Graphics_AdaptiveDegradeBias",
            lower: -1f, upper: 1f, defaultVal: 0f, sheet, locateString,
            scan: () => mappings.LoadDisplay().GraphicsPerformance,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { GraphicsPerformance = val }),
            vaultSole: false,
            spanLoTag: "ID_Graphics_Value_Speed", spanHiTag: "ID_Graphics_Value_Detail");

        AssembleDialRank(
            rosterBbox, RangedDialBlueprintOrdinal, "ID_Graphics_DegradeDistance",
            lower: 0f, upper: 100f, defaultVal: 50.0f, sheet, locateString,
            scan: () => mappings.LoadDisplay().DegradeDistance,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { DegradeDistance = val }),
            vaultSole: false,
            spanLoTag: "ID_Graphics_Value_Close", spanHiTag: "ID_Graphics_Value_Far");

        readout = mappings.LoadDisplay();
    }

    private static void AttachRenderingFidelitySection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref ReadoutPrefs readout,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        AssemblePreambleRank(rosterBbox, "ID_Graphics_TextureSection", locateString);

        AssembleMenuRank(
            rosterBbox, "ID_Graphics_LandscapeTextureDetail", TextureSpecificsChoices, sheet, locateString,
            scan: () => mappings.LoadDisplay().LandscapeTextureDetail,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { LandscapeTextureDetail = val }),
            defaultVal: 2,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        AssembleMenuRank(
            rosterBbox, "ID_Graphics_EnvironmentTextureDetail", TextureSpecificsChoices, sheet, locateString,
            scan: () => mappings.LoadDisplay().EnvironmentTextureDetail,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { EnvironmentTextureDetail = val }),
            defaultVal: 1,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        AssembleMenuRank(
            rosterBbox, "ID_Graphics_TextureFiltering", TextureFilteringChoices, sheet, locateString,
            scan: () => mappings.LoadDisplay().TextureFiltering,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { TextureFiltering = val }),
            defaultVal: 1,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        AssembleMenuRank(
            rosterBbox, "ID_Graphics_LandscapeDrawDistance", SceneryPaintGapChoices, sheet, locateString,
            scan: () => mappings.LoadDisplay().LandscapeDrawDistance,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { LandscapeDrawDistance = val }),
            defaultVal: 8,
            vaultSole: false,
            locateSprite, datTypeface, diagTypeface,
            payloadValues: SceneryPaintGapVals);

        AssembleFlipRank(
            rosterBbox, "ID_Graphics_BuildingDetailTextures", defaultVal: true, sheet, locateString,
            scan: () => mappings.LoadDisplay().BuildingDetailTextures,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { BuildingDetailTextures = val }),
            vaultSole: false);

        AssembleFlipRank(
            rosterBbox, "ID_Graphics_MultiPassAlpha", defaultVal: false, sheet, locateString,
            scan: () => mappings.LoadDisplay().MultiPassAlpha,
            enact: val => mappings.SaveDisplay(mappings.LoadDisplay() with { MultiPassAlpha = val }),
            vaultSole: true);

        readout = mappings.LoadDisplay();
    }

    private static void AttachFeedSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref CameraTurnSettings camTurning)
    {
        AssemblePreambleRank(rosterBbox, "ID_Input_InputSection", locateString);

        AssembleDialRank(
            rosterBbox, SimpleDialBlueprintOrdinal, "ID_Input_MouseLookSensitivity",
            lower: 0.00999999978f, upper: 1f, defaultVal: 0.55f, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().MouseLookSensitivity,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { MouseLookSensitivity = val }),
            vaultSole: true);

        AssembleFlipRank(
            rosterBbox, "ID_Input_InvertMouseLookYAxis", defaultVal: false, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().InvertMouseLookYAxis,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { InvertMouseLookYAxis = val }),
            vaultSole: true);

        AssembleFlipRank(
            rosterBbox, "ID_Input_UseMouseTurning", defaultVal: false, sheet, locateString,
            scan: () => mappings.LoadCameraTurning().UseMouseTurning,
            enact: val => mappings.SaveCameraTurning(mappings.LoadCameraTurning() with { UseMouseTurning = val }),
            vaultSole: true);

        camTurning = mappings.LoadCameraTurning();
    }

    private static void AttachWidgetSection(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        ref CommsPrefs comms,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        AssemblePreambleRank(rosterBbox, "ID_UI_UISection", locateString);

        AssembleMenuRank(
            rosterBbox, "ID_UI_ChatFontFace", CommsTypefaceFaceChoices, sheet, locateString,
            scan: () => mappings.LoadChat().ChatFontFace,
            enact: val => mappings.SaveChat(mappings.LoadChat() with { ChatFontFace = val }),
            defaultVal: 2,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        AssembleMenuRank(
            rosterBbox, "ID_UI_ChatFontSize", CommsTypefaceDimsChoices, sheet, locateString,
            scan: () => mappings.LoadChat().ChatFontSizeIndex,
            enact: val => mappings.SaveChat(mappings.LoadChat() with { ChatFontSizeIndex = val }),
            defaultVal: 1,
            vaultSole: true,
            locateSprite, datTypeface, diagTypeface);

        comms = mappings.LoadChat();
    }
}
