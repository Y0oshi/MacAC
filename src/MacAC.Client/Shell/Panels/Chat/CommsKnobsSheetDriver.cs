using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public static class CommsKnobsSheetDriver
{
    public const uint RootElementId = 0x1000050Au;

    private const uint SheetSocketElemIdent = 0x1000050Cu;

    /// <summary>The row ListBox (dat Type 5) - <c>m_pOptionBox</c>.</summary>
    public const uint RosterBboxElemTag = 0x1000050Du;

    public const uint ScrollbarElementId = 0x10000201u;

    private const int PreambleBlueprintOrdinal = 0;
    private const int SeparatorBlueprintOrdinal = 1;
    private const int UnlabelledDialBlueprintOrdinal = 3;
    private const int LabelledDialBlueprintOrdinal = 6;
    private const int BitfieldBlueprintOrdinal = 8;

    private const uint StringChartIdent = 0x23000003u;

    private const uint SiftStringChartIdent = 0x2300000Du;

    // The slider leaf inside either slider row template's subtree
    private const uint DialElemIdent = 0x1000021Cu;

    private const uint DialCaptionElemIdent = 0x1000021Bu;

    private const uint DialSpanLowerElemIdent = 0x1000021Eu;
    private const uint DialSpanUpperElemIdent = 0x1000021Fu;

    public readonly record struct SiftRankSpec(ulong Mask, string RetailLabelKey);

    public static readonly SiftRankSpec[] SiftRanks =
    [
        new(0x0000000083912021ul, "ID_ChatOption_TextFilter_Gameplay"),
        new(0x0000000000600040ul, "ID_ChatOption_TextFilter_Combat"),
        new(0x0000000000020080ul, "ID_ChatOption_TextFilter_Magic"),
        new(0x0000000000001004ul, "ID_ChatOption_TextFilter_AreaSpeech"),
        new(0x0000000000000018ul, "ID_ChatOption_TextFilter_Tells"),
        new(0x0000000000040C00ul, "ID_ChatOption_TextFilter_Allegience"),
        new(0x0000000000080000ul, "ID_ChatOption_TextFilter_Fellowship"),
        new(0x0000000008000000ul, "ID_ChatOption_TextFilter_General"),
        new(0x0000000010000000ul, "ID_ChatOption_TextFilter_Trade"),
        new(0x0000000020000000ul, "ID_ChatOption_TextFilter_LFG"),
        new(0x0000000040000000ul, "ID_ChatOption_TextFilter_Roleplay"),
        new(0x0000000100000000ul, "ID_ChatOption_TextFilter_Society"),
        new(0x0000000004000000ul, "ID_ChatOption_TextFilter_Error"),
    ];

    public readonly record struct SiftChunkSpec(
        int RetailWindowId,
        int CompactWindowId,
        string HeaderKey,
        ulong DefaultFilter,
        bool IncludesGameplayRow);

    public static readonly SiftChunkSpec[] SiftChunks =
    [
        new(8, ChatPaneState.PrimaryPaneIdent, "ID_ChatOption_MainChatWindow_Section",
            ChatPaneState.PrimaryPaneDefaultSift, IncludesGameplayRow: false),
        new(2, 1, "ID_ChatOption_FloatyChatWindow1_Section",
            ChatPaneState.Floaty1DefaultSift, IncludesGameplayRow: true),
        new(3, 2, "ID_ChatOption_FloatyChatWindow2_Section",
            ChatPaneState.Floaty2DefaultSift, IncludesGameplayRow: true),
        new(4, 3, "ID_ChatOption_FloatyChatWindow3_Section",
            ChatPaneState.Floaty3DefaultSift, IncludesGameplayRow: true),
        new(5, 4, "ID_ChatOption_FloatyChatWindow4_Section",
            ChatPaneState.Floaty4DefaultSift, IncludesGameplayRow: true),
    ];

    public sealed record BindingsDef(
        Func<float> CurrentDefaultOpacity,
        Func<float> CurrentActiveOpacity,
        Action<float> SetDefaultOpacity,
        Action<float> SetActiveOpacity,
        Action FlushOpacity,
        float DefaultOpacityDatDefault,
        float ActiveOpacityDatDefault,
        Func<int, ulong> CurrentFilter,
        Action<int, ulong> SetFilter,
        CommsKnobsDatCaptions.Legend DefaultOpacityCaption,
        CommsKnobsDatCaptions.Legend ActiveOpacityCaption);

    public static bool Bind(
        ImportedArrangement arrangement,
        KnobPage sheet,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, uint, string?> locateString,
        BindingsDef mappings)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(locateString);
        ArgumentNullException.ThrowIfNull(mappings);

        if (arrangement.SeekElem(RosterBboxElemTag) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: ListBox 0x{RosterBboxElemTag:X8} "
                + "not found (or not a WidgetBlueprintRosterBbox) in the built Options panel tree - "
                + "the Chat tab will have no rows");
            return false;
        }

        rosterBbox.TemplateResolver = blueprintLocator;

        WidgetElem? commsSheetSocket = arrangement.SeekElem(SheetSocketElemIdent);
        WidgetElem? scrollerElem = commsSheetSocket is null
            ? null
            : WidgetElem.SeekDescendant(commsSheetSocket, ScrollbarElementId);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: scrollbar 0x{ScrollbarElementId:X8} "
                + $"not found under Chat page slot 0x{SheetSocketElemIdent:X8} - the Chat "
                + "tab's row list will not scroll");

        AssemblePreambleRank(rosterBbox, "ID_ChatOption_GeneralOptions_Section", locateString);
        AssembleDensityDials(rosterBbox, sheet, locateString, mappings);
        AssembleSeparatorRank(rosterBbox);

        foreach (SiftChunkSpec spec in SiftChunks)
        {
            BindLoop(rosterBbox, spec, locateString, sheet, blueprintLocator, mappings);
        }

        return true;
    }

    private static void BindLoop(WidgetBlueprintRosterBbox rosterBbox, SiftChunkSpec spec, Func<uint, uint, string?> locateString, KnobPage sheet, Func<uint, uint, WidgetElem?> blueprintLocator, BindingsDef mappings)
    {
        AssemblePreambleRank(rosterBbox, spec.HeaderKey, locateString);
        AssembleSiftChunk(rosterBbox, spec, sheet, blueprintLocator, locateString, mappings);
        AssembleSeparatorRank(rosterBbox);
    }

    private static void AssemblePreambleRank(
        WidgetBlueprintRosterBbox rosterBbox, string preambleTag, Func<uint, uint, string?> locateString)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(PreambleBlueprintOrdinal) is not WidgetPhrase preamble)
        {
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: header template didn't build as "
                + $"WidgetPhrase for '{preambleTag}'.");
            return;
        }

        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(preambleTag));
        if (caption is null)
        {
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: header string '{preambleTag}' didn't "
                + "resolve from the DAT string table - the row renders with no text rather "
                + "than an invented label");
            return;
        }

        preamble.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, preamble.DefaultTint) };
    }

    private static void AssembleSeparatorRank(WidgetBlueprintRosterBbox rosterBbox)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(SeparatorBlueprintOrdinal) is null)
            Console.WriteLine("[UI] CommsKnobsSheetDriver: separator template didn't build");
    }

    private static void AssembleDensityDials(
        WidgetBlueprintRosterBbox rosterBbox,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        BindingsDef mappings)
    {
        WidgetElem? row1 = rosterBbox.AppendGearFromBlueprintRoster(UnlabelledDialBlueprintOrdinal);
        WidgetScroller? slider1 = row1 is null ? null : WidgetElem.SeekDescendant(row1, DialElemIdent) as WidgetScroller;
        if (slider1 is null)
            Console.WriteLine(
                "[UI] CommsKnobsSheetDriver: default-opacity slider template didn't "
                + "build a WidgetScroller leaf");

        WidgetElem? row2 = rosterBbox.AppendGearFromBlueprintRoster(LabelledDialBlueprintOrdinal);
        WidgetScroller? slider2 = row2 is null ? null : WidgetElem.SeekDescendant(row2, DialElemIdent) as WidgetScroller;
        if (slider2 is null)
            Console.WriteLine(
                "[UI] CommsKnobsSheetDriver: active-opacity slider template didn't "
                + "build a WidgetScroller leaf");

        if (row2 is not null)
        {
            AssignSpanCaption(row2, DialSpanLowerElemIdent, "ID_UI_Value_Transparent", locateString);
            AssignSpanCaption(row2, DialSpanUpperElemIdent, "ID_UI_Value_Opaque", locateString);
        }

        if (row1 is not null)
            AssignDensityLegend(row1, mappings.DefaultOpacityCaption, slider1);
        if (row2 is not null)
            AssignDensityLegend(row2, mappings.ActiveOpacityCaption, slider2);

        if (slider1 is null || slider2 is null)
            return;

        float startingDefault = mappings.CurrentDefaultOpacity();
        float startingEngaged = mappings.CurrentActiveOpacity();
        slider1.AssignScalarLocus(startingDefault);
        slider2.AssignScalarLocus(startingEngaged);

        FloatKnobRow? defaultRank = null;
        FloatKnobRow? engagedRank = null;

        defaultRank = new FloatKnobRow(
            startingDefault,
            mappings.DefaultOpacityDatDefault,
            enact: val =>
            {
                mappings.SetDefaultOpacity(val);
                slider1.AssignScalarLocus(mappings.CurrentDefaultOpacity());
                engagedRank!.RenewFromConnect(mappings.CurrentActiveOpacity());
                if (!slider1.IsDragging) mappings.FlushOpacity(); // S1: settle now unless mid-drag
            },
            scan: mappings.CurrentDefaultOpacity,
            renew: val => slider1.AssignScalarLocus(val));

        engagedRank = new FloatKnobRow(
            startingEngaged,
            mappings.ActiveOpacityDatDefault,
            enact: val =>
            {
                mappings.SetActiveOpacity(val);
                slider2.AssignScalarLocus(mappings.CurrentActiveOpacity());
                defaultRank!.RenewFromConnect(mappings.CurrentDefaultOpacity());
                if (!slider2.IsDragging) mappings.FlushOpacity(); // S1: settle now unless mid-drag
            },
            scan: mappings.CurrentActiveOpacity,
            renew: val => slider2.AssignScalarLocus(val));

        slider1.ScalarAltered = val => defaultRank.ApplyLatestVal(val);
        slider2.ScalarAltered = val => engagedRank.ApplyLatestVal(val);

        // S1: the drag-end seam - flushes whatever the tick loop above deferred.
        slider1.PullFinished = mappings.FlushOpacity;
        slider2.PullFinished = mappings.FlushOpacity;

        sheet.Register(defaultRank);
        sheet.Register(engagedRank);
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
                $"[UI] CommsKnobsSheetDriver: range label '{captionTag}' didn't "
                + "resolve - rendered with no text rather than invented English");
            return;
        }
        phrase.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, phrase.DefaultTint) };
    }

    private static void AssignDensityLegend(
        WidgetElem rank, CommsKnobsDatCaptions.Legend legend, WidgetScroller? dial)
    {
        if (WidgetElem.SeekDescendant(rank, DialCaptionElemIdent) is WidgetPhrase phrase)
        {
            if (legend.Name is { Length: > 0 } label)
                phrase.StrokesSupplier = () => new[] { new WidgetPhrase.Line(label, phrase.DefaultTint) };
            else
                Console.WriteLine(
                    "[UI] CommsKnobsSheetDriver: opacity slider caption didn't "
                    + "resolve from the DAT name/tooltip catalog - the row renders with no "
                    + "text rather than invented English");
        }

        if (dial is not null && legend.Tooltip is { Length: > 0 } hint)
            dial.TooltipText = hint;
    }

    private static void AssembleSiftChunk(
        WidgetBlueprintRosterBbox rosterBbox,
        SiftChunkSpec spec,
        KnobPage sheet,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, uint, string?> locateString,
        BindingsDef mappings)
    {
        if (BitfieldBlueprintOrdinal >= rosterBbox.Templates.Count)
        {
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: bitfield template index "
                + $"{BitfieldBlueprintOrdinal} absent from the Chat ListBox's authored "
                + $"template list - window {spec.RetailWindowId}'s filter block wasn't built");
            return;
        }

        var listing = rosterBbox.Templates[BitfieldBlueprintOrdinal];
        WidgetElem? built = blueprintLocator(listing.TemplateLayoutId, listing.TemplateElementId);
        if (built is not WidgetTickboxBitfield64 chunk)
        {
            Console.WriteLine(
                $"[UI] CommsKnobsSheetDriver: filter block template didn't build as "
                + $"WidgetTickboxBitfield64 for window {spec.RetailWindowId}.");
            return;
        }

        chunk.TemplateResolver = blueprintLocator;

        (ulong defaultLo, ulong defaultHi) = DivideBitmask(spec.DefaultFilter);
        chunk.SetDefaultValue(defaultLo, defaultHi);

        for (int idx = 0; idx < SiftRanks.Length; ++idx)
        {
            if (idx is 0 && !spec.IncludesGameplayRow)
                continue; // research doc §5.2: the main window has no Gameplay row.

            var rankSpec = SiftRanks[idx];
            string? caption = locateString(SiftStringChartIdent, DatStringPicker.CalculateDigest(rankSpec.RetailLabelKey));
            string? hint = locateString(
                SiftStringChartIdent, DatStringPicker.CalculateDigest(rankSpec.RetailLabelKey + "_Desc"));
            if (caption is null)
                Console.WriteLine(
                    $"[UI] CommsKnobsSheetDriver: filter row label "
                    + $"'{rankSpec.RetailLabelKey}' didn't resolve - row renders with no "
                    + "caption rather than invented English");

            (ulong lo, ulong hi) = DivideBitmask(rankSpec.Mask);
            if (chunk.AddChild(lo, hi, caption ?? string.Empty, hint) is null)
                Console.WriteLine(
                    $"[UI] CommsKnobsSheetDriver: filter row '{rankSpec.RetailLabelKey}' "
                    + $"didn't build for window {spec.RetailWindowId}.");
        }

        rosterBbox.AppendPrebuiltRank(chunk);

        ulong starting = mappings.CurrentFilter(spec.CompactWindowId);
        (ulong startingLo, ulong startingHi) = DivideBitmask(starting);
        chunk.SetCurrentValue(startingLo, startingHi); // live seed, not the window default

        BitfieldKnobRow rank = new BitfieldKnobRow(
            starting,
            spec.DefaultFilter,
            enact: val =>
            {
                (ulong lo, ulong hi) = DivideBitmask(val);
                chunk.SetCurrentValue(lo, hi);
                mappings.SetFilter(spec.CompactWindowId, val);
            },
            scan: () => mappings.CurrentFilter(spec.CompactWindowId),
            renew: val =>
            {
                (ulong lo, ulong hi) = DivideBitmask(val);
                chunk.SetCurrentValue(lo, hi);
            });

        chunk.ValAltered = (low, high) => rank.SetCurrentVal(FuseBitmask(low, high));

        sheet.Register(rank);
    }

    private static (ulong Low, ulong High) DivideBitmask(ulong combined)
        => (combined & 0xFFFFFFFFul, (combined >> 32) & 0xFFFFFFFFul);

    private static ulong FuseBitmask(ulong lo, ulong hi)
        => ((hi & 0xFFFFFFFFul) << 32) | (lo & 0xFFFFFFFFul);
}
