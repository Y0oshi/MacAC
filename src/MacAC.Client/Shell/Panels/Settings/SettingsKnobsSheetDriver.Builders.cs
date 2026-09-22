using System.Numerics;
using MacAC.Client.Graphics;

namespace MacAC.Client.Shell.Panels;

public static partial class SettingsKnobsSheetDriver
{

    private static void AssemblePreambleRank(
        WidgetBlueprintRosterBbox rosterBbox, string preambleTag, Func<uint, uint, string?> locateString)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(PreambleBlueprintOrdinal) is not WidgetPhrase preamble)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: header template didn't build as "
                + $"WidgetPhrase for '{preambleTag}'.");
            return;
        }

        string? caption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(preambleTag));
        if (caption is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: header string '{preambleTag}' didn't "
                + "resolve from the DAT string table - the row renders with no text rather "
                + "than an invented label");
            return;
        }

        preamble.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, preamble.DefaultTint) };
    }

    private static void AssembleExplicitPreambleRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string caption)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(PreambleBlueprintOrdinal) is not WidgetPhrase preamble)
        {
            Console.WriteLine(
                "[render-pack] Config header template didn't build as UiText");
            return;
        }

        preamble.StrokesSupplier = () => new[]
        {
            new WidgetPhrase.Line(caption, preamble.DefaultTint),
        };
    }

    private static void AssembleSeparatorRank(WidgetBlueprintRosterBbox rosterBbox)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(SeparatorBlueprintOrdinal) is null)
            Console.WriteLine("[UI] SettingsKnobsSheetDriver: separator template didn't build");
    }

    private static void AssembleFlipRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionTag,
        bool defaultVal,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Func<bool> scan,
        Action<bool> enact,
        bool vaultSole)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(FlipBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: toggle template didn't build for "
                + $"'{captionTag}'.");
            return;
        }

        WidgetBtn? tickbox = SeekTickbox(rank);
        if (tickbox is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: no checkbox child found in the "
                + $"toggle row for '{captionTag}'.");
            return;
        }

        ImposeCaptionAndHint(tickbox, captionTag, locateString, vaultSole);

        bool starting = scan();
        tickbox.Selected = starting;

        BoolKnobRow row_ = new BoolKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                tickbox.Selected = val;
                enact(val);
            },
            scan: scan,
            renew: val => tickbox.Selected = val);
        sheet.Register(row_);

        tickbox.OnClick = () => row_.AssignLatestVal(tickbox.Selected);
    }

    private static void AssembleDialRank(
        WidgetBlueprintRosterBbox rosterBbox,
        int blueprintOrdinal,
        string captionTag,
        float lower,
        float upper,
        float defaultVal,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Func<float> scan,
        Action<float> enact,
        bool vaultSole,
        string? spanLoTag = null,
        string? spanHiTag = null)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(blueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: slider template didn't build for "
                + $"'{captionTag}'.");
            return;
        }

        if (WidgetElem.SeekDescendant(rank, DialCaptionElemIdent) is WidgetPhrase caption)
            AssignCaptionPhrase(caption, captionTag, locateString, vaultSole);

        if (spanLoTag is not null)
            AssignSpanCaption(rank, DialSpanLowerElemIdent, spanLoTag, locateString);
        if (spanHiTag is not null)
            AssignSpanCaption(rank, DialSpanUpperElemIdent, spanHiTag, locateString);

        if (WidgetElem.SeekDescendant(rank, DialElemIdent) is not WidgetScroller dial)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: no slider leaf found in the row for "
                + $"'{captionTag}'.");
            return;
        }

        // the slider IS the interactive/hoverable
        // widget for this row - the label text has no hit-test surface.
        string? hint = LocateHint(captionTag, locateString);
        if (hint is not null)
            dial.TooltipText = hint;

        float starting = scan();
        dial.AssignScalarLocus(ToNormalized(starting, lower, upper));

        FloatKnobRow row_ = new FloatKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                dial.AssignScalarLocus(ToNormalized(val, lower, upper));
                enact(val);
            },
            scan: scan,
            renew: val => dial.AssignScalarLocus(ToNormalized(val, lower, upper)));
        sheet.Register(row_);

        dial.ScalarAltered = normalized => row_.ApplyLatestVal(FromNormalized(normalized, lower, upper));
    }

    private static void AssembleTrioRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string flipCaptionTag,
        string dialHintTag,
        bool flipDefault,
        float dialLower,
        float dialUpper,
        float dialDefault,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Func<bool> flipScan,
        Action<bool> flipEnact,
        Func<float> dialScan,
        Action<float> dialEnact,
        bool vaultSole)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(TrioBlueprintOrdinal) is not WidgetKnobFlipDial trio)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: trio template didn't build as "
                + $"WidgetKnobFlipDial for '{flipCaptionTag}'.");
            return;
        }

        WidgetBtn? tickbox = trio.Toggle;
        var dial = trio.Slider;
        if (tickbox is null || dial is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: trio row for '{flipCaptionTag}' is "
                + $"absent its toggle or slider child (toggle={tickbox is not null}, "
                + $"slider={dial is not null}).");
            return;
        }

        ImposeCaptionAndHint(tickbox, flipCaptionTag, locateString, vaultSole);

        bool flipStarting = flipScan();
        tickbox.Selected = flipStarting;
        BoolKnobRow flipRank = new BoolKnobRow(
            flipStarting,
            flipDefault,
            enact: val =>
            {
                tickbox.Selected = val;
                flipEnact(val);
            },
            scan: flipScan,
            renew: val => tickbox.Selected = val);
        sheet.Register(flipRank);
        tickbox.OnClick = () => flipRank.AssignLatestVal(tickbox.Selected);

        string? dialHint = LocateHint(dialHintTag, locateString);
        if (dialHint is not null)
            dial.TooltipText = dialHint;

        float dialStarting = dialScan();
        dial.AssignScalarLocus(ToNormalized(dialStarting, dialLower, dialUpper));
        FloatKnobRow dialRank = new FloatKnobRow(
            dialStarting,
            dialDefault,
            enact: val =>
            {
                dial.AssignScalarLocus(ToNormalized(val, dialLower, dialUpper));
                dialEnact(val);
            },
            scan: dialScan,
            renew: val => dial.AssignScalarLocus(ToNormalized(val, dialLower, dialUpper)));
        sheet.Register(dialRank);
        dial.ScalarAltered = normalized =>
            dialRank.ApplyLatestVal(FromNormalized(normalized, dialLower, dialUpper));
    }

    private static void AssembleMenuRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionTag,
        string[] choiceTags,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Func<int> scan,
        Action<int> enact,
        int defaultVal,
        bool vaultSole,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        IReadOnlyList<int>? payloadValues = null)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(MenuBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: menu template didn't build for "
                + $"'{captionTag}'.");
            return;
        }

        if (WidgetElem.SeekDescendant(rank, MenuCaptionElemIdent) is WidgetPhrase caption)
            AssignCaptionPhrase(caption, captionTag, locateString, vaultSole);

        if (WidgetElem.SeekDescendant(rank, MenuElemIdent) is not WidgetMenu menu)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: no WidgetMenu leaf found in the row for "
                + $"'{captionTag}'.");
            return;
        }

        ImposeMenuChrome(menu, locateSprite, datTypeface, diagTypeface);

        string? hint = LocateHint(captionTag, locateString);
        if (hint is not null)
            menu.TooltipText = hint;

        if (payloadValues is not null && payloadValues.Count != choiceTags.Length)
            throw new ArgumentException(
                "Menu payload count must match the choice count",
                nameof(payloadValues));

        string[] choiceCaptions = new string[choiceTags.Length];
        int[] choiceVals = new int[choiceTags.Length];
        WidgetMenu.MenuGear[] gearList = new WidgetMenu.MenuGear[choiceTags.Length];
        for (int idx = 0; idx < choiceTags.Length; ++idx)
        {
            string? choiceCaption = locateString(StringChartIdent, DatStringPicker.CalculateDigest(choiceTags[idx]));
            choiceCaptions[idx] = choiceCaption ?? string.Empty;
            if (choiceCaption is null)
                Console.WriteLine(
                    $"[UI] SettingsKnobsSheetDriver: menu choice '{choiceTags[idx]}' "
                    + $"(for '{captionTag}') didn't resolve - item renders with no caption "
                    + "rather than invented English");
            choiceVals[idx] = payloadValues?[idx] ?? idx;
            gearList[idx] = new WidgetMenu.MenuGear(choiceCaptions[idx], choiceVals[idx]);
        }
        menu.Items = gearList;

        int starting = scan();
        menu.Selected = starting;
        menu.BtnCaptionSupplier = () =>
        {
            int latest = menu.Selected is int chosen ? chosen : starting;
            int choiceOrdinal = Array.IndexOf(choiceVals, latest);
            return choiceOrdinal >= 0 ? choiceCaptions[choiceOrdinal] : string.Empty;
        };

        IntKnobRow row_ = new IntKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                menu.Selected = val;
                enact(val);
            },
            scan: scan,
            renew: val => menu.Selected = val);
        sheet.Register(row_);

        menu.OnSelect = cargo =>
        {
            if (cargo is int value)
                row_.AssignCurrentValue(value);
        };
    }

    private static void AssembleStringMenuRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionTag,
        IReadOnlyList<string> choices,
        KnobPage sheet,
        Func<uint, uint, string?> locateString,
        Func<string> scan,
        Action<string> enact,
        string defaultVal,
        bool vaultSole,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(MenuBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: menu template didn't build for "
                + $"'{captionTag}'.");
            return;
        }

        if (WidgetElem.SeekDescendant(rank, MenuCaptionElemIdent) is WidgetPhrase caption)
            AssignCaptionPhrase(caption, captionTag, locateString, vaultSole);

        if (WidgetElem.SeekDescendant(rank, MenuElemIdent) is not WidgetMenu menu)
        {
            Console.WriteLine(
                $"[UI] SettingsKnobsSheetDriver: no WidgetMenu leaf found in the row for "
                + $"'{captionTag}'.");
            return;
        }

        ImposeMenuChrome(menu, locateSprite, datTypeface, diagTypeface);

        string? hint = LocateHint(captionTag, locateString);
        if (hint is not null)
            menu.TooltipText = hint;

        WidgetMenu.MenuGear[] gearList = new WidgetMenu.MenuGear[choices.Count];
        for (int idx = 0; idx < choices.Count; ++idx)
            gearList[idx] = new WidgetMenu.MenuGear(choices[idx], choices[idx]);
        menu.Items = gearList;

        string starting = scan();
        menu.Selected = starting;
        menu.BtnCaptionSupplier = () => menu.Selected as string ?? starting;

        StringKnobRow stringRank = new StringKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                menu.Selected = val;
                enact(val);
            },
            scan: scan,
            renew: val => menu.Selected = val);
        sheet.Register(stringRank);

        menu.OnSelect = cargo =>
        {
            if (cargo is string value)
                stringRank.SetLatestValue(value);
        };
    }

    private static WidgetMenu? AssembleExplicitStringMenuRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionPhrase,
        IReadOnlyList<ExplicitMenuPick> choices,
        KnobPage sheet,
        Func<string> scan,
        Action<string> enact,
        string defaultVal,
        Func<uint, (uint tex, int w, int h)>? locateSprite,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface,
        ExplicitMenuPickOrigin? dynamicChoices = null,
        Action<StringKnobRow>? grabKnob = null)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(MenuBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[render-pack] Config menu template didn't build for '{captionPhrase}'.");
            return null;
        }

        if (WidgetElem.SeekDescendant(rank, MenuCaptionElemIdent) is WidgetPhrase caption)
        {
            caption.StrokesSupplier = () => new[]
            {
                new WidgetPhrase.Line(captionPhrase, caption.DefaultTint),
            };
        }

        if (WidgetElem.SeekDescendant(rank, MenuElemIdent) is not WidgetMenu menu)
        {
            Console.WriteLine(
                $"[render-pack] No WidgetMenu leaf found for '{captionPhrase}'.");
            return null;
        }

        ExplicitMenuPickOrigin choiceSrc = dynamicChoices ?? new ExplicitMenuPickOrigin(choices);
        ImposeMenuChrome(menu, locateSprite, datTypeface, diagTypeface);
        menu.Items = choiceSrc.Choices
            .Select(static choice => new WidgetMenu.MenuGear(choice.Label, choice.Id))
            .ToArray();
        menu.TurnedOnSupplier = cargo => choiceSrc.Choices.Any(choice =>
            choice.Enabled && Equals(choice.Id, cargo));

        string starting = scan();
        menu.Selected = starting;
        menu.BtnCaptionSupplier = () =>
        {
            string latest = menu.Selected as string ?? starting;
            return choiceSrc.Choices.FirstOrDefault(choice => string.Equals(
                    choice.Id,
                    latest,
                    StringComparison.OrdinalIgnoreCase))
                .Label ?? latest;
        };
        menu.TooltipText = choiceSrc.Choices.FirstOrDefault(choice => string.Equals(
                choice.Id,
                starting,
                StringComparison.OrdinalIgnoreCase))
            .Tooltip;

        StringKnobRow knob = new StringKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                menu.Selected = val;
                menu.TooltipText = choiceSrc.Choices.FirstOrDefault(choice => string.Equals(
                        choice.Id,
                        val,
                        StringComparison.OrdinalIgnoreCase))
                    .Tooltip;
                enact(val);
            },
            scan,
            renew: val => menu.Selected = val);
        sheet.Register(knob);
        grabKnob?.Invoke(knob);
        menu.OnSelect = cargo =>
        {
            if (cargo is string value && choiceSrc.Choices.Any(choice =>
                choice.Enabled && string.Equals(
                    choice.Id,
                    value,
                    StringComparison.OrdinalIgnoreCase)))
                knob.SetLatestValue(value);
        };
        return menu;
    }

    private static WidgetBtn? AssembleExplicitFlipRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionPhrase,
        bool defaultVal,
        KnobPage sheet,
        Func<bool> scan,
        Action<bool> enact,
        Func<bool> isLatest)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(FlipBlueprintOrdinal);
        WidgetBtn? tickbox = rank is null ? null : SeekTickbox(rank);
        if (tickbox is null)
        {
            Console.WriteLine(
                $"[render-pack] Toggle template didn't build for '{captionPhrase}'.");
            return null;
        }

        tickbox.Label = captionPhrase;
        tickbox.CaptionColor = Vector4.One;
        bool starting = scan();
        tickbox.Selected = starting;
        BoolKnobRow knob = new BoolKnobRow(
            starting,
            defaultVal,
            enact: val =>
            {
                tickbox.Selected = val;
                if (isLatest()) enact(val);
            },
            scan: () => isLatest() ? scan() : starting,
            renew: val => tickbox.Selected = val);
        sheet.Register(knob);
        tickbox.OnClick = () =>
        {
            if (isLatest()) knob.AssignLatestVal(tickbox.Selected);
        };
        return tickbox;
    }

    private static WidgetScroller? AssembleExplicitNumericDialRank(
        WidgetBlueprintRosterBbox rosterBbox,
        string captionPhrase,
        double lower,
        double upper,
        double hop,
        bool integer,
        double defaultVal,
        KnobPage sheet,
        Func<double> scan,
        Action<double> enact,
        Func<bool> isLatest)
    {
        WidgetElem? rank = rosterBbox.AppendGearFromBlueprintRoster(RangedDialBlueprintOrdinal);
        if (rank is null)
        {
            Console.WriteLine(
                $"[render-pack] Slider template didn't build for '{captionPhrase}'.");
            return null;
        }
        if (WidgetElem.SeekDescendant(rank, DialCaptionElemIdent) is WidgetPhrase caption)
        {
            caption.StrokesSupplier = () =>
            [
                new WidgetPhrase.Line(captionPhrase, caption.DefaultTint),
            ];
        }
        if (WidgetElem.SeekDescendant(rank, DialSpanLowerElemIdent) is WidgetPhrase lo)
        {
            string phrase = ComposeExplicitNumber(lower, integer);
            lo.StrokesSupplier = () => [new WidgetPhrase.Line(phrase, lo.DefaultTint)];
        }
        if (WidgetElem.SeekDescendant(rank, DialSpanUpperElemIdent) is WidgetPhrase hi)
        {
            string phrase = ComposeExplicitNumber(upper, integer);
            hi.StrokesSupplier = () => [new WidgetPhrase.Line(phrase, hi.DefaultTint)];
        }
        if (WidgetElem.SeekDescendant(rank, DialElemIdent) is not WidgetScroller dial)
        {
            Console.WriteLine(
                $"[render-pack] No slider leaf found for '{captionPhrase}'.");
            return null;
        }

        double startingVal = SnapExplicitNumber(scan(), lower, upper, hop, integer);
        float starting = (float)startingVal;
        dial.AssignScalarLocus((float)((startingVal - lower) / (upper - lower)));
        FloatKnobRow knob = new FloatKnobRow(
            starting,
            (float)SnapExplicitNumber(defaultVal, lower, upper, hop, integer),
            enact: val =>
            {
                double snapped = SnapExplicitNumber(val, lower, upper, hop, integer);
                dial.AssignScalarLocus((float)((snapped - lower) / (upper - lower)));
                if (isLatest()) enact(snapped);
            },
            scan: () => isLatest()
                ? (float)SnapExplicitNumber(scan(), lower, upper, hop, integer)
                : starting,
            renew: val =>
            {
                double snapped = SnapExplicitNumber(val, lower, upper, hop, integer);
                dial.AssignScalarLocus((float)((snapped - lower) / (upper - lower)));
            });
        sheet.Register(knob);
        dial.ScalarAltered = normalized =>
        {
            if (!isLatest()) return;
            double raw = lower + normalized * (upper - lower);
            knob.ApplyLatestVal((float)SnapExplicitNumber(raw, lower, upper, hop, integer));
        };
        return dial;
    }
}
