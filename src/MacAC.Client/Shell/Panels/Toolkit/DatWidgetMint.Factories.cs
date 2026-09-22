namespace MacAC.Client.Shell.Panels;

public static partial class DatWidgetMint
{
    public static WidgetElem? Create(ElemDetails details,
        Func<uint, (uint, int, int)> locate, WidgetDatFont? datTypeface,
        Func<uint, WidgetDatFont?>? typefaceLocate = null,
        Func<WidgetStringInfoValue, string?>? stringLocate = null)
    {
        var elemTypeface = datTypeface;
        if (typefaceLocate is not null && details.FontDid is not 0)
            elemTypeface = typefaceLocate(details.FontDid) ?? datTypeface;

        WidgetElem element = details.Type switch
        {
            UiRadar.CanonClassIdent => new UiRadar(),
            WidgetVitalsRoot.GmVitalsClassIdent
                or WidgetVitalsRoot.GmFloatyVitalsClassIdent
                or WidgetVitalsRoot.GmFloatyFlankVitalsClassIdent
                => new WidgetVitalsRoot(details, locate),
            1 => AssembleBtn(details, locate, elemTypeface, typefaceLocate, stringLocate), // UIElement_Button
            2 => new WidgetDatElement(details, locate)
            {
                PaneRelocateHnd = true,
                ClickThrough = false,
            },
            IndicatorBarDriver.BurdenClassIdent
                or IndicatorBarDriver.FxListClassIdent
                or IndicatorBarDriver.ConnectClassIdent
                or IndicatorBarDriver.MiniPlayClassIdent
                or IndicatorBarDriver.VitaeClassIdent => AssembleBtn(
                    details, locate, elemTypeface, typefaceLocate, stringLocate),
            5 => new WidgetBlueprintRosterBbox(details, locate, details.TemplateList, details.ScrollbarElementId),
            6 => AssembleMenu(details, locate, elemTypeface, typefaceLocate), // UIElement_Menu (reg :120163)
            7 => AssembleGauge(details, locate, elemTypeface, stringLocate),    // UIElement_Meter
            8 => new WidgetTabBoard(details, locate, details.TabTable),
            9 => AssembleRescaleGrip(details, locate),
            0xD => new WidgetViewport(),                          // UIElement_Viewport - 3-D mini-scene blit leaf
            11 => AssembleScroller(details, locate),             // UIElement_Scrollbar (reg :124137)
            12 => AssemblePhrase(details, locate, elemTypeface, stringLocate), // UIElement_Text
            0x13 => new WidgetPopupTrunk(),
            0x14 => new WidgetPopupTrunk(),
            0x15 => new WidgetPopupTrunk(),
            0x17 => new WidgetPopupTrunk(),                         // MessageDialog
            0x19 => new WidgetPopupTrunk(),
            0x10000031u => new WidgetGearRoster(locate),            // UIElement_ItemList - toolbar/inventory/paperdoll slots
            0x10000035u => AssembleTickbox(
                details, locate, elemTypeface, typefaceLocate, stringLocate), // UIOption_Checkbox
            0x10000036u => new WidgetKnobFlipDial(),
            0x10000037u => AssembleScroller(details, locate),
            0x10000038u => AssembleMenu(details, locate, elemTypeface, typefaceLocate),
            0x10000044u => new WidgetTickboxBitfield64(
                details.TemplateList, details.LedCheckedSprite, details.LedUncheckedSprite),
            _ => new WidgetDatElement(details, locate),
        };

        element.DatElemIdent = details.Id;
        element.AssignPhaseCursors(details.StateCursors);

        // Propagate position + size (pixel-exact from the dat)
        element.Left = details.X;
        element.Top = details.Y;
        element.Width = details.Width;
        element.Height = details.Height;

        element.ZOrder = (int)details.ReadOrder - (int)details.ZLevel * 10000;

        element.Moorings = ElemScanner.ToMoorings(details.Left, details.Top, details.Right, details.Bottom);

        if (details.HasOriginalParentSize)
            element.ArrangementRule = BuildArrangementRule(details);

        return element;
    }

    internal static string? LocateHintPhrase(
        ElemDetails details,
        Func<WidgetStringInfoValue, string?>? stringLocate)
    {
        return stringLocate is null || details.TooltipText is not { } hintPhrase ? null : stringLocate(hintPhrase);
    }

    private static WidgetArrangementRule? BuildArrangementRule(ElemDetails details)
    {
        return !details.HasOriginalParentSize
            ? null
            : new WidgetArrangementRule(
            details.Left,
            details.Top,
            details.Right,
            details.Bottom,
            WidgetPixelRect.FromLocusAndDims(
                (int)details.X,
                (int)details.Y,
                (int)details.Width,
                (int)details.Height),
            WidgetPixelRect.FromLocusAndDims(
                0,
                0,
                (int)details.OriginalParentWidth,
                (int)details.OriginalParentHeight));
    }

    private static uint ReferencedElemIdent(ElemDetails details, uint propIdent)
    {
        return !details.TryFetchNetProp(propIdent, out var prop)
            ? 0u
            : prop.Kind switch
            {
                WidgetPropertyKind.Enum or WidgetPropertyKind.DataId => (uint)prop.UnsignedValue,
                WidgetPropertyKind.Integer when prop.IntegerValue >= 0 => (uint)prop.IntegerValue,
                _ => 0u,
            };
    }

    private static uint DefaultImage(ElemDetails details)
    {
        uint phaseIdent = details.NetDefaultPhaseIdent();
        if (details.States.TryGetValue(phaseIdent, out var phase) && phase.Image is { } image)
            return image.File;
        return details.States.TryGetValue(WidgetStateInfo.StraightPhaseIdent, out var straight)
            && straight.Image is { } straightImage
            ? straightImage.File
            : 0u;
    }

    private static uint BtnPhaseImage(ElemDetails? details, string phaseLabel)
    {
        if (details is null)
            return 0u;
        if (details.StateMedia.TryGetValue(phaseLabel, out var media))
            return media.File;
        var phase = details.States.Values.FirstOrDefault(
            contender => string.Equals(contender.Name, phaseLabel, StringComparison.Ordinal));
        return phase?.Image is { } image ? image.File : phaseLabel == "Normal" ? DefaultImage(details) : 0u;
    }

    private static bool HasThreeSliceForm(ElemDetails vessel)
    {
        return vessel.Children.Count(info =>
                info.StateMedia.TryGetValue("", out var media) && media.File is not 0) >= 3;
    }

    private static bool HasStatefulPopulate(ElemDetails vessel)
    {
        return vessel.States.Any(duo =>
                duo.Key != WidgetStateInfo.StraightPhaseIdent
                && vessel.StateMedia.TryGetValue(duo.Value.Name, out var media)
                && media.File is not 0);
    }

    private static ElemDetails? SpecificsTopLayer(ElemDetails vessel)
    {
        return vessel.Children.FirstOrDefault(static info =>
                !info.StateMedia.ContainsKey("")
                && info.StateMedia.TryGetValue("ShowDetail", out var media)
                && media.File is not 0);
    }

    private static WidgetMeterDetailOverlaySpec SpecificsTopLayerSpec(
        ElemDetails? topLayer, ElemDetails vessel)
    {
        return topLayer is null
                ? default
                : new WidgetMeterDetailOverlaySpec(
                    topLayer.StateMedia["ShowDetail"].File,
                    topLayer.X, topLayer.Y, topLayer.Width, topLayer.Height,
                    topLayer.Left, topLayer.Top, topLayer.Right, topLayer.Bottom,
                    vessel.Width, vessel.Height);
    }

    private static (uint left, uint tile, uint right) SliceIdents(ElemDetails vessel)
    {
        var slices = vessel.Children
            .Where(info => info.StateMedia.TryGetValue("", out var med) && med.File is not 0)
            .Select(info => (info.X, info.StateMedia[""].File))
            .OrderBy(t => t.X)
            .ToList();

        uint left = slices.Count > 0 ? slices[0].File : 0u;
        uint tile = slices.Count > 1 ? slices[1].File : 0u;
        uint right = slices.Count > 2 ? slices[2].File : 0u;

        return (left, tile, right);
    }

    private static (float X, float Y, float Width, float Height) ReflowValDescendantRect(
        ElemDetails descendant, ElemDetails ancestor)
    {
        float originalAncestorWidth = descendant.HasOriginalParentSize ? descendant.OriginalParentWidth : ancestor.Width;
        float originalAncestorHeight = descendant.HasOriginalParentSize ? descendant.OriginalParentHeight : ancestor.Height;

        WidgetPixelRect originalDescendant = WidgetPixelRect.FromLocusAndDims(
            (int)descendant.X, (int)descendant.Y, (int)descendant.Width, (int)descendant.Height);
        WidgetPixelRect originalAncestor = WidgetPixelRect.FromLocusAndDims(
            0, 0, (int)originalAncestorWidth, (int)originalAncestorHeight);
        WidgetPixelRect latestAncestor = WidgetPixelRect.FromLocusAndDims(
            0, 0, (int)ancestor.Width, (int)ancestor.Height);
        WidgetPixelRect noLatestDescendant = new WidgetPixelRect(0, 0, -1, -1);

        var reflowed = WidgetArrangementRule.Apply(
            descendant.Left, descendant.Top, descendant.Right, descendant.Bottom,
            originalDescendant, originalAncestor,
            noLatestDescendant, latestAncestor);

        return (reflowed.X0, reflowed.Y0, reflowed.Width, reflowed.Height);
    }

    private static ElemDetails? SeekStatefulFaceDescendant(ElemDetails details)
        => SeekStatefulFaceDescendants(details).FirstOrDefault();

    private static ElemDetails[] SeekStatefulFaceDescendants(ElemDetails details)
    {
        return [.. details.Children.Where(descendant =>
            descendant.StateMedia.Count is not 0
            && descendant.StateMedia.Keys.Any(descendantPhase =>
                details.States.Values.Any(ancestorPhase =>
                    string.Equals(ancestorPhase.Name, descendantPhase, StringComparison.Ordinal))))
            .OrderBy(descendant => descendant.ReadOrder)];
    }

    private static string? LocateAuthoredString(
        ElemDetails details,
        Func<WidgetStringInfoValue, string?>? stringLocate)
    {
        return stringLocate is null
            || !details.TryFetchNetProp(0x17u, out var prop)
            || prop.Kind != WidgetPropertyKind.StringInfo
            ? null
            : stringLocate(prop.StringInfoValue);
    }
}
