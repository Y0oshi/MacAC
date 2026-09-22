using System.Globalization;
using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Shell.Panels;

public static partial class SettingsKnobsSheetDriver
{
    private static void ImposeMenuChrome(
        WidgetMenu menu,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        menu.SpriteResolve = locateSprite;
        menu.DatFont = datTypeface;
        menu.Font = diagTypeface;
        ImposeMenuChromeRest(menu);
    }

    private static void ImposeMenuChromeRest(WidgetMenu menu)
    {
        menu.NormSprite = MenuChromeDecals.Normal;
        menu.PressedSprite = MenuChromeDecals.Pressed;
        ImposeMenuChromeTail(menu);
    }

    private static void ImposeMenuChromeTail(WidgetMenu menu)
    {
        menu.GearNormSprite = MenuChromeDecals.GearNorm;
        menu.GearHighlightSprite = MenuChromeDecals.GearHighlight;
        menu.RowsPerColumn = MenuChromeDecals.RanksPerColumn;
        ImposeMenuChromeCoda(menu);
    }

    private static void ImposeMenuChromeCoda(WidgetMenu menu)
    {
        menu.RowHeight = MenuChromeDecals.RankHeight;
        menu.ColumnWidth = MenuChromeDecals.ColumnWidth;
        menu.Scrollable = true;
        menu.ScrollbarWidth = MenuChromeDecals.ScrollerWidth;
        ImposeMenuChromeCoda2(menu);
    }

    private static void ImposeMenuChromeCoda2(WidgetMenu menu)
    {
        menu.RollButtonExtent = MenuChromeDecals.RollBtnReach;
        menu.RollFollowSprite = MenuChromeDecals.RollFollow;
        ImposeMenuChromeCoda3(menu);
    }

    private static void ImposeMenuChromeCoda3(WidgetMenu menu)
    {
        menu.RollThumbTopSprite = MenuChromeDecals.RollThumbTop;
        menu.RollThumbSprite = MenuChromeDecals.RollThumb;
        menu.RollThumbBottomSprite = MenuChromeDecals.RollThumbBottom;
        menu.RollUpSprite = MenuChromeDecals.ScrollUp;
        ImposeMenuChromeCoda4(menu);
    }

    private static void ImposeMenuChromeCoda4(WidgetMenu menu)
    {
        menu.RollDownSprite = MenuChromeDecals.ScrollDown;
        menu.ArrowCapClosedSprite = MenuChromeDecals.ArrowCapClosed;
        menu.ArrowCapOpenSprite = MenuChromeDecals.ArrowCapOpen;
        FinishFinishImposeMenuChrome2(menu);
    }

    private static void FinishFinishImposeMenuChrome2(WidgetMenu menu)
    {
        menu.OpenUpward = false;
        menu.PhraseIndent = 0f;
        menu.BtnPhraseIndent = 0f;
        menu.WordingTint = Vector4.One;
        FinishFinishFinishImposeMenuChrome22(menu);
    }

    private static void FinishFinishFinishImposeMenuChrome22(WidgetMenu menu)
    {
        menu.BtnPhraseCentered = true;
        menu.GearPhraseCentered = true;
        menu.PopupDimsToSubstance = true;
    }

    private static void ImposeCaptionAndHint(
        WidgetBtn tickbox, string captionTag, Func<uint, uint, string?> locateString, bool vaultSole)
    {
        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (caption is not null)
            tickbox.Label = caption;
        else
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: label '{captionTag}' didn't resolve - "
                + "row renders with no caption rather than invented English");

        tickbox.CaptionColor = vaultSole
            ? WidgetRenderScope.VaultSoleLegendTint
            : Vector4.One;

        string? hint = LocateHint(captionTag, locateString);
        if (hint is not null)
            tickbox.TooltipText = hint;
    }

    private static List<RasterizeBundlePick> PullBundleChoices(
        RenderPackWiring rasterizeBundles)
    {
        var discovered = rasterizeBundles.LoadChoices();
        var choices = new List<RasterizeBundlePick>(discovered.Count + 1)
        {
            new(
                RenderPackPick.CanonBundleIdent,
                "macac default (retail-faithful)",
                Version: null,
                Selectable: true,
                UnavailableReason: null,
                [new RasterizeBundlePresetPick(
                    RenderPackPick.CanonPresetIdent,
                    "Off",
                    Selectable: true,
                    UnavailableReason: null)]),
        };
        choices.AddRange(discovered.Where(static choice =>
            !string.Equals(
                choice.Id,
                RenderPackPick.CanonBundleIdent,
                StringComparison.OrdinalIgnoreCase)));
        return choices;
    }

    private static string StandardizeBundleIdent(
        RenderPackPick pick,
        IReadOnlyList<RasterizeBundlePick> choices)
    {
        return choices.Any(val => string.Equals(
            val.Id,
            pick.PackId,
            StringComparison.OrdinalIgnoreCase) && val.Selectable)
            ? pick.PackId
            : RenderPackPick.CanonBundleIdent;
    }

    private static string StandardizePresetIdent(
        string presetIdent,
        RasterizeBundlePick bundle)
    {
        return bundle.Presets.Any(val => val.Selectable && string.Equals(
            val.Id,
            presetIdent,
            StringComparison.OrdinalIgnoreCase))
            ? presetIdent
            : bundle.Presets.FirstOrDefault(static val => val.Selectable)?.Id
                ?? RenderPackPick.CanonPresetIdent;
    }

    private static string BundleHint(RasterizeBundlePick? bundle)
    {
        if (bundle is null)
            return "macac's default retail-faithful renderer remains authoritative unless an enhancement pack is explicitly selected.";
        string summary = string.IsNullOrWhiteSpace(bundle.FeatureSummary)
            ? "macac's default retail-faithful renderer remains authoritative unless an enhancement pack is explicitly selected."
            : bundle.FeatureSummary.Trim();
        return FuseHint(bundle.UnavailableReason, summary) ?? summary;
    }

    private static string PresetHint(RasterizeBundlePresetPick preset)
    {
        double housedMiB = preset.UpperHousedGpuOctets / (1024d * 1024d);
        string estimate = string.Format(
            CultureInfo.InvariantCulture,
            "Estimated ceiling — GPU p50/p99 ≤ {0:0.###}/{1:0.###} ms; "
                + "render CPU p50/p99 ≤ {2:0.###}/{3:0.###} ms; pack VRAM ≤ {4:0.##} MiB.",
            preset.UpperIncrementalGpuMillisP50,
            preset.UpperIncrementalGpuMillisP99,
            preset.UpperIncrementalCpuMillisP50,
            preset.UpperIncrementalCpuMillisP99,
            housedMiB);
        return FuseHint(preset.UnavailableReason, estimate) ?? estimate;
    }

    private static string? FuseHint(string? lead, string? second)
    {
        bool hasLead = !string.IsNullOrWhiteSpace(lead);
        bool hasSecond = !string.IsNullOrWhiteSpace(second);
        if (!hasLead)
            return hasSecond ? second!.Trim() : null;
        return !hasSecond ? lead!.Trim() : lead!.Trim() + Environment.NewLine + second!.Trim();
    }

    private static void AssignSpanCaption(
        WidgetElem rank, uint elemIdent, string captionTag, Func<uint, uint, string?> locateString)
    {
        if (WidgetElem.SeekDescendant(rank, elemIdent) is not WidgetPhrase phrase)
            return;
        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (caption is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: range label '{captionTag}' didn't "
                + "resolve - rendered with no text rather than invented English");
            return;
        }
        phrase.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, phrase.DefaultTint) };
    }

    private static void AssignCaptionPhrase(
        WidgetPhrase caption, string captionTag, Func<uint, uint, string?> locateString, bool vaultSole)
    {
        string? phrase = locateString(StringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (phrase is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: label '{captionTag}' didn't resolve - "
                + "row renders with no caption rather than invented English");
            return;
        }
        Vector4 tint = vaultSole ? WidgetRenderScope.VaultSoleLegendTint : caption.DefaultTint;
        caption.StrokesSupplier = () => new[] { new WidgetPhrase.Line(phrase, tint) };
    }

    private static double SnapExplicitNumber(
        double val,
        double lower,
        double upper,
        double hop,
        bool integer)
    {
        double clamped = Math.Clamp(val, lower, upper);
        if (hop > 0)
            clamped = lower + Math.Round((clamped - lower) / hop) * hop;
        if (integer)
            clamped = Math.Round(clamped);
        return Math.Clamp(clamped, lower, upper);
    }

    private static string ComposeExplicitNumber(double val, bool integer)
    {
        return integer
            ? checked((long)Math.Round(val)).ToString(CultureInfo.InvariantCulture)
            : val.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string? LocateHint(string captionTag, Func<uint, uint, string?> locateString)
    {
        return locateString(StringChartIdent, DatStringPicker.CalculateDigest(captionTag + "_Help"));
    }

    private static WidgetBtn? SeekTickbox(WidgetElem trunk)
    {
        if (trunk is WidgetBtn straight) return straight;
        foreach (WidgetElem descendant in trunk.Children)
            if (descendant is WidgetBtn btn)
                return btn;
        return WidgetElem.SeekDescendant(trunk, FlipTickboxElemIdent) as WidgetBtn;
    }

    private static float ToNormalized(float real, float lower, float upper)
        => upper > lower ? (real - lower) / (upper - lower) : 0f;

    private static float FromNormalized(float normalized, float lower, float upper)
        => lower + normalized * (upper - lower);
}
