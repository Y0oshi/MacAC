using System.Numerics;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Controls;

namespace MacAC.Client.Shell.Panels;

public sealed partial class KeyboardSettingsDriver
{
    public IReadOnlyList<RankLens> Rows => _ranks;

    private KeyboardSettingsDriver() { }

    public static KeyboardSettingsDriver? Bind(
        ImportedArrangement arrangement,
        CanonActionMapFrame capture,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        Func<uint, uint, string?> locateString,
        Bindings mappings,
        Func<uint, uint, WidgetDatFont?>? locateBlueprintTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(locateString);
        ArgumentNullException.ThrowIfNull(mappings);

        if (arrangement.SeekElem(PaneTrunkElemIdent) is null)
        {
            Console.WriteLine(
                $"[UI] KeyboardSettingsDriver: window root 0x{PaneTrunkElemIdent:X8} "
                + "not found in the built layout - Configure Keyboard will not open");
            return null;
        }

        KeyboardSettingsDriver driver = new KeyboardSettingsDriver
        {
            _capture = capture,
            _bindings = mappings,
            _locateBlueprintTypeface = locateBlueprintTypeface,
            _depict = new CanonKeyNames(locateString).Portray,
        };

        var byClass = capture.Rows
            .Where(r => r.ActionClass != CanonActionClass.None)
            .GroupBy(r => r.ActionClass)
            .ToDictionary(grouping => grouping.Key, grouping => grouping.ToList());

        foreach ((uint sheetVesselIdent, CanonActionClass cls) in Sheets)
        {
            WidgetElem? sheetTrunk = WidgetElem.SeekDescendant(arrangement.Root, sheetVesselIdent);
            if (sheetTrunk is null)
            {
                Console.WriteLine(
                    $"[UI] KeyboardSettingsDriver: page container 0x{sheetVesselIdent:X8} "
                    + "not found - that ActionClass tab will have no rows");
                continue;
            }
            if (WidgetElem.SeekDescendant(sheetTrunk, RosterBboxElemIdent) is not WidgetBlueprintRosterBbox rosterBbox)
            {
                Console.WriteLine(
                    $"[UI] KeyboardSettingsDriver: ListBox 0x{RosterBboxElemIdent:X8} not found "
                    + $"(or not a WidgetBlueprintRosterBbox) under page 0x{sheetVesselIdent:X8}.");
                continue;
            }
            rosterBbox.TemplateResolver = blueprintLocator;
            if (WidgetElem.SeekDescendant(sheetTrunk, ScrollbarElementId) is WidgetScroller scroller)
                scroller.Model = rosterBbox.Scroll;

            if (!byClass.TryGetValue(cls, out List<CanonActionMapRow>? classRanks))
                continue;

            var byFeedLookup = classRanks
                .GroupBy(r => r.InputMapId)
                .OrderBy(grouping => grouping.Key);

            foreach (var feedLookupCluster in byFeedLookup)
            {
                AssemblePreambleRank(rosterBbox, feedLookupCluster.Key, locateString);
                foreach (CanonActionMapRow rank in feedLookupCluster)
                    driver.AssembleActRank(rosterBbox, rank, locateString, mappings);
            }
        }

        WireMonitorBtns(arrangement, driver, mappings);

        if (arrangement.SeekElem(TabHubElemIdent) is WidgetTabBoard tabHub)
        {
            tabHub.ActivateTabBehavior();
        }
        else
        {
            Console.WriteLine(
                $"[UI] KeyboardSettingsDriver: tab host 0x{TabHubElemIdent:X8} not found "
                + "(or not a WidgetTabBoard) - all six ActionClass pages will render stacked");
        }

        return driver;
    }

    private static void AssemblePreambleRank(
        WidgetBlueprintRosterBbox rosterBbox, uint feedLookupIdent, Func<uint, uint, string?> locateString)
    {
        if (rosterBbox.AppendGearFromBlueprintRoster(PreambleBlueprintOrdinal) is not WidgetPhrase preamble)
        {
            Console.WriteLine(
                "[UI] KeyboardSettingsDriver: header template didn't build as WidgetPhrase "
                + $"for InputMap 0x{feedLookupIdent:X8}.");
            return;
        }
        if (!CanonInputMapHeaders.LabelByFeedLookupIdent.TryGetValue(feedLookupIdent, out string? preambleTag))
            return;
        // action of ours falls in one, but stay honest rather than assume

        string? caption = locateString(
            CanonInputMapHeaders.StringChartIdent, DatStringPicker.CalculateDigest(preambleTag));
        if (caption is null)
        {
            Console.WriteLine(
                $"[UI] KeyboardSettingsDriver: header string '{preambleTag}' didn't resolve - "
                + "row renders with no text rather than invented English");
            return;
        }
        preamble.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, preamble.DefaultTint) };
    }

    private void AssembleActRank(
        WidgetBlueprintRosterBbox rosterBbox,
        CanonActionMapRow rank,
        Func<uint, uint, string?> locateString,
        Bindings mappings)
    {
        WidgetElem? built = rosterBbox.AppendGearFromBlueprintRoster(RankBlueprintOrdinal);
        if (built is null)
        {
            Console.WriteLine(
                "[UI] KeyboardSettingsDriver: row template didn't build for InputMap "
                + $"0x{rank.InputMapId:X8} action 0x{rank.ActionId:X8}.");
            return;
        }

        List<WidgetBtn> tagBtns = new List<WidgetBtn>(TagBtnIdents.Length);
        foreach (uint ident in TagBtnIdents)
        {
            if (WidgetElem.SeekDescendant(built, ident) is WidgetBtn btn)
                tagBtns.Add(btn);
        }

        string? caption = locateString(CanonInputMapHeaders.StringChartIdent, rank.LabelHash);
        string? hint = locateString(CanonInputMapHeaders.StringChartIdent, rank.TooltipHash);

        bool mapped = CanonActionIdentityTable.TryResolve(rank.InputMapId, rank.ActionId, out FeedAct act);
        FeedAct? mappedAct = mapped ? act : null;

        WidgetPhrase legendPhrase = new WidgetPhrase
        {
            Left = 0f,
            Top = 0f,
            Width = 260f,
            Height = built.Height,
            ClickThrough = true,
            Centered = false,
            RightAligned = false,
            Padding = 2f,
            Moorings = MooringRims.Left | MooringRims.Top,
            DefaultTint = mapped ? Vector4.One : WidgetRenderScope.VaultSoleLegendTint,
            DatFont = RankLegendTypeface(rosterBbox),
        };
        if (caption is not null)
            legendPhrase.StrokesSupplier = () => new[] { new WidgetPhrase.Line(caption, legendPhrase.DefaultTint) };
        legendPhrase.AuthoredHintPhrase = hint;
        built.AddChild(legendPhrase);

        IReadOnlyList<CockpitBinding> onlineMappings = mapped
            ? mappings.CurrentForAction(act)
            : Array.Empty<CockpitBinding>();
        (ActivationKind Activation, InputLayer Scope) blueprint = onlineMappings.Count > 0
            ? (onlineMappings[0].Activation, onlineMappings[0].Scope)
            : (
                CanonActionIdentityTable.ActivationFor(rank.InputMapId, rank.ActionId),
                CanonActionIdentityTable.AmbitForFeedLookup(rank.InputMapId));

        var defaults = DatDefaultsToChords(rank.DefaultBindings);
        IReadOnlyList<KeyStroke> storedUnmapped = mapped
            ? Array.Empty<KeyStroke>()
            : mappings.CurrentForUnmapped((rank.InputMapId, rank.ActionId));
        IReadOnlyList<KeyStroke> starting = mapped
            ? onlineMappings.Select(binding => binding.Chord).ToArray()
            : storedUnmapped.Count > 0 ? storedUnmapped : defaults;

        ActionKeyMapKnobRow model = new ActionKeyMapKnobRow(starting, defaults, enact: val =>
        {
            IReadOnlyList<KeyStroke> real = val.Where(stroke => stroke != default).ToArray();
            if (mapped)
                mappings.SetForAction(
                    act,
                    real.Select(stroke => new CockpitBinding(stroke, act, blueprint.Activation, blueprint.Scope)).ToArray());
            else
                mappings.SetForUnmapped((rank.InputMapId, rank.ActionId), real);
        });
        Page.Register(model);

        RankLens lens = new RankLens(rank.InputMapId, rank.ActionId, mappedAct, caption, model, tagBtns);
        _ranks.Add(lens);

        RenewRankBtns(lens);

        for (int socket = 0; socket < tagBtns.Count; ++socket)
        {
            int grabbedSocket = socket;
            tagBtns[socket].OnClick = () => CommenceSocketGrab(lens, grabbedSocket, mappings);
            tagBtns[socket].OnRightPress = () => EraseSocket(lens, grabbedSocket);
        }
    }

    // The authored font of this ListBox's action-row template, resolved once per (layout, element)
    // pair
    private WidgetDatFont? RankLegendTypeface(WidgetBlueprintRosterBbox rosterBbox)
    {
        if (_locateBlueprintTypeface is null
            || RankBlueprintOrdinal >= rosterBbox.Templates.Count)
            return null;
        (uint arrangementIdent, uint elemIdent) = (
            rosterBbox.Templates[RankBlueprintOrdinal].TemplateLayoutId,
            rosterBbox.Templates[RankBlueprintOrdinal].TemplateElementId);
        if (_blueprintTypefaceStash.TryGetValue((arrangementIdent, elemIdent), out WidgetDatFont? stashed))
            return stashed;
        var typeface = _locateBlueprintTypeface(arrangementIdent, elemIdent);
        _blueprintTypefaceStash[(arrangementIdent, elemIdent)] = typeface;
        return typeface;
    }

    private static IReadOnlyList<KeyStroke> DatDefaultsToChords(IReadOnlyList<CanonKeyChord> raw)
    {
        List<KeyStroke> outcome = new List<KeyStroke>(raw.Count);
        foreach (CanonKeyChord chord in raw)
        {
            var tag = CanonScanCodeMap.ToSilkTag(chord.Scan, chord.Device);
            if (tag is null) continue;
            outcome.Add(new KeyStroke(tag.Value, CanonScanCodeMap.ToModifierBitmask(chord.Modifier), (byte)chord.Device));
        }
        return outcome;
    }

    private void RenewRankBtns(RankLens lens)
    {
        var latest = lens.Model.Current;
        for (int idx = 0; idx < lens.KeyButtons.Count; ++idx)
        {
            bool tied = idx < latest.Count && latest[idx] != default;
            if (!tied)
            {
                lens.KeyButtons[idx].Label = null;
                lens.KeyButtons[idx].TooltipText = LocateBlueprint(
                    "ID_ActionKeyMap_TT_NewBinding",
                    VacantBlueprintVariables);
                continue;
            }

            string tagLabel = _depict(latest[idx]);
            string? btnCaption = LocateBlueprint(
                "ID_ActionKeyMap_ButtonLabel",
                new Dictionary<uint, string> { [CaptionVariable] = tagLabel });
            lens.KeyButtons[idx].Label = btnCaption;
            lens.KeyButtons[idx].TooltipText = btnCaption is null
                ? null
                : LocateBlueprint(
                    "ID_ActionKeyMap_TT_ExistingBinding",
                    new Dictionary<uint, string> { [ValVariable] = btnCaption });
        }
    }

    private string? LocateBlueprint(
        string tag,
        IReadOnlyDictionary<uint, string> variables) =>
        _bindings?.ResolveTemplate(tag, variables);

    private static string DepictChord(KeyStroke chord)
    {
        string mods = chord.Modifiers == ModifierBits.None ? "" : chord.Modifiers.ToString() + "+";
        return mods + chord.Key;
    }

    private void CommenceSocketGrab(RankLens lens, int socket, Bindings mappings)
    {
        uint instructionsCtx = 0u;
        if (mappings.OpenCaptureInstructions is { } openInstructions)
        {
            instructionsCtx = openInstructions(lens.Label ?? string.Empty);
            if (instructionsCtx is 0u)
            {
                Console.WriteLine(
                    "[UI] KeyboardSettingsDriver: capture-instruction dialog "
                    + "could not open - capture not armed (retail refuses too)");
                return;
            }
        }

        void ArmGrab() => mappings.BeginCapture(grabbed =>
        {
            if (grabbed is { } unsupported && IsUnsupportedCanonGrab(unsupported))
            {
                ArmGrab();
                return;
            }

            if (instructionsCtx is not 0u)
                mappings.CloseCaptureInstructions?.Invoke(instructionsCtx);

            if (grabbed is not { } chord) return;

            if (lens.Model.Current.Contains(chord))
                return;

            (ConflictVerdict verdict, List<RankLens> conflictRanks) = SeekConflicts(chord, exclude: lens);
            switch (verdict)
            {
                case ConflictVerdict.NonBindable:
                    string? refusal = mappings.ResolveTemplate(
                        "ID_ActionKeyMap_NonUserBindableBinding",
                        new Dictionary<uint, string> { [TagVariable] = _depict(chord) });
                    if (refusal is not null)
                        mappings.ShowMessage(refusal);
                    return;

                case ConflictVerdict.Rows:
                    string? msg = ConstructOverwriteMsg(chord, conflictRanks, mappings);
                    if (msg is null)
                        return;
                    mappings.ConfirmOverwrite(msg, approved =>
                    {
                        if (!approved) return;
                        foreach (RankLens conflictRank in conflictRanks)
                        {
                            ReplaceSocketVal(conflictRank, DropChord(conflictRank.Model.Current, chord));
                            RenewRankBtns(conflictRank);
                        }
                        ImposeSocket(lens, socket, chord);
                    });
                    return;

                case ConflictVerdict.None:
                    ImposeSocket(lens, socket, chord);
                    return;
            }
        });

        ArmGrab();
    }

    private static bool IsUnsupportedCanonGrab(KeyStroke chord)
    {
        if (chord.Device > 1)
            return true; // joystick/unknown device
        return chord.Device is 1
            && (chord.Key == InputRouter.PointerBtnToTag(Silk.NET.Input.MouseButton.Left)
                || chord.Key == InputRouter.PointerBtnToTag(Silk.NET.Input.MouseButton.Right))
            ? true
            : !CanonScanCodeMap.TryToFileControl(chord, out _);
    }

    private string? ConstructOverwriteMsg(
        KeyStroke chord,
        IReadOnlyList<RankLens> conflicts,
        Bindings mappings)
    {
        string tagLabel = _depict(chord);
        if (conflicts.Count is 1)
        {
            string? act = conflicts[0].Label;
            return act is null
                ? null
                : mappings.ResolveTemplate(
                "ID_ActionKeyMap_OverwriteExistingBinding",
                new Dictionary<uint, string>
                {
                    [TagVariable] = tagLabel,
                    [ActVariable] = act,
                });
        }

        List<string> strokes = new List<string>(conflicts.Count);
        foreach (RankLens conflict in conflicts)
        {
            if (conflict.Label is null) return null;
            string? stroke = mappings.ResolveTemplate(
                "ID_ActionKeyMap_Binding",
                new Dictionary<uint, string>
                {
                    [ActVariable] = conflict.Label,
                    [TagVariable] = tagLabel,
                });
            if (stroke is null) return null;
            strokes.Add(stroke);
        }

        return mappings.ResolveTemplate(
            "ID_ActionKeyMap_OverwriteExistingBindings",
            new Dictionary<uint, string>
            {
                [TagVariable] = tagLabel,
                [MappingsVariable] = string.Join("\n", strokes),
            });
    }

    private void ImposeSocket(RankLens lens, int socket, KeyStroke chord)
    {
        List<KeyStroke> updated = [.. lens.Model.Current];
        int markSocket = Math.Clamp(socket, 0, updated.Count);
        if (markSocket == updated.Count)
            updated.Add(chord);
        else
            updated[markSocket] = chord;
        ReplaceSocketVal(lens, updated);
        RenewRankBtns(lens);
    }

    private void EraseSocket(RankLens lens, int socket)
    {
        if (socket >= lens.Model.Current.Count) return;
        if (lens.Model.Current[socket] == default) return; // nothing bound in this display slot
        List<KeyStroke> updated = new List<KeyStroke>(lens.Model.Current);
        updated.RemoveAt(socket);
        ReplaceSocketVal(lens, updated);
        RenewRankBtns(lens);
    }

    private static void ReplaceSocketVal(RankLens lens, IReadOnlyList<KeyStroke> val)
    {
        int previousReal = -1;
        for (int idx = 0; idx < val.Count; ++idx)
            if (val[idx] != default) previousReal = idx;
        lens.Model.SetCurrentValue(previousReal < 0 ? [] : val.Take(previousReal + 1).ToArray());
    }

    private static IReadOnlyList<KeyStroke> DropChord(IReadOnlyList<KeyStroke> from, KeyStroke chord) =>
        from.Where(stroke => stroke != chord).ToArray();

    private (ConflictVerdict Outcome, List<RankLens> Rows) SeekConflicts(KeyStroke chord, RankLens exclude)
    {
        if (_bindings is not null)
        {
            foreach (FeedAct contender in Enum.GetValues<FeedAct>())
            {
                if (CanonActionIdentityTable.Map.Values.Contains(contender)) continue;
                if (_bindings.CurrentForAction(contender).Any(binding => binding.Chord == chord))
                    return (ConflictVerdict.NonBindable, new List<RankLens>());
            }
        }

        List<RankLens> ranks = new List<RankLens>();
        foreach (RankLens another in _ranks)
        {
            if (ReferenceEquals(another, exclude)) continue;
            if (another.MappedAction is null) continue;
            if (_capture?.FeedLookupsConflict(
                    exclude.InputMapId,
                    another.InputMapId) != true)

                continue;
            if (another.Model.Current.Contains(chord))
                ranks.Add(another);
        }
        return ranks.Count > 0 ? (ConflictVerdict.Rows, rows: ranks) : (ConflictVerdict.None, rows: ranks);
    }

    private static void WireMonitorBtns(
        ImportedArrangement arrangement, KeyboardSettingsDriver driver, Bindings mappings)
    {
        void RenewFilename()
        {
            if (arrangement.SeekElem(FilenameCaptionIdent) is not WidgetPhrase filename || mappings.CurrentKeymapFilename is null) return;
            string val = mappings.CurrentKeymapFilename();
            filename.StrokesSupplier = () =>
                new[] { new WidgetPhrase.Line(val, filename.DefaultTint) };
        }
        RenewFilename();

        if (arrangement.SeekElem(PullBtnIdent) is WidgetBtn pullBtn
            && mappings.OpenLoadKeymap is { } openPull)
        {
            pullBtn.OnClick = () => openPull(() =>
            {
                driver.ReloadRanksFromMappings(mappings);
                RenewFilename();
            });
        }

        if (arrangement.SeekElem(PersistAsBtnIdent) is WidgetBtn persistAsBtn
            && mappings.OpenSaveKeymap is { } openPersist)

            persistAsBtn.OnClick = () => openPersist(RenewFilename);

        if (arrangement.SeekElem(DefaultsBtnIdent) is WidgetBtn defaultsBtn)
            defaultsBtn.OnClick = () =>
            {
                driver.Page.Defaults();
                foreach (RankLens rank in driver._ranks)
                    driver.RenewRankBtns(rank);
            };

        if (arrangement.SeekElem(UndoBtnIdent) is WidgetBtn undoBtn)
        {
            undoBtn.OnClick = () =>
            {
                driver.Page.Reset();
                foreach (RankLens rank in driver._ranks)
                    driver.RenewRankBtns(rank);
            };

            driver.Page.OnKnobAltered = () =>
                undoBtn.Enabled = driver.Page.Changed;
            driver.Page.OnKnobAltered();
        }

        if (arrangement.SeekElem(OkBtnIdent) is WidgetBtn okBtn)
            okBtn.OnClick = () =>
            {
                bool altered = driver.Page.Changed;
                if (altered)
                    mappings.Save();
                driver.Page.Apply();
                mappings.Toggle();
            };

        if (arrangement.SeekElem(AbortBtnIdent) is WidgetBtn abortBtn)
            abortBtn.OnClick = () =>
            {
                driver.Page.Reset();
                foreach (RankLens rank in driver._ranks)
                    driver.RenewRankBtns(rank);
                mappings.Toggle();
            };
    }

    private void ReloadRanksFromMappings(Bindings mappings)
    {
        foreach (RankLens rank in _ranks)
        {
            IReadOnlyList<KeyStroke> chords = rank.MappedAction is { } act
                ? mappings.CurrentForAction(act).Select(static val => val.Chord).ToArray()
                : mappings.CurrentForUnmapped((rank.InputMapId, rank.ActionId));
            rank.Model.ReloadLatestAndStored(chords);
            RenewRankBtns(rank);
        }
        Page.OnKnobAltered?.Invoke();
    }
}
