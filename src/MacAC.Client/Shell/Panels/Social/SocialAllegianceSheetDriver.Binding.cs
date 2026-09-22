using System.Numerics;
using MacAC.Sim;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed partial class SocialAllegianceSheetDriver
{
    public static SocialAllegianceSheetDriver? Bind(WidgetElem sheetTrunk, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(sheetTrunk);
        ArgumentNullException.ThrowIfNull(mappings);

        if (WidgetElem.SeekDescendant(sheetTrunk, MonarchFieldIdent) is not { } monarchField
            || WidgetElem.SeekDescendant(sheetTrunk, PatronFieldIdent) is not { } patronField)
        {
            Console.WriteLine(
                "[UI] SocialAllegianceSheetDriver: monarch/patron field "
                + $"containers (0x{MonarchFieldIdent:X8}/0x{PatronFieldIdent:X8}) not found - "
                + "allegiance page will not present");
            return null;
        }

        WidgetElem? monarchIsPatronSubChunk =
            WidgetElem.SeekDescendant(monarchField, MonarchIsPatronSubChunkIdent);
        WidgetPhrase? monarchExperiencePassedUp = monarchIsPatronSubChunk is null
            ? null
            : WidgetElem.SeekDescendant(monarchIsPatronSubChunk, ExperiencePassedUpPhraseIdent) as WidgetPhrase;
        WidgetPhrase? patronExperiencePassedUp =
            WidgetElem.SeekDescendant(patronField, ExperiencePassedUpPhraseIdent) as WidgetPhrase;

        WidgetPhrase? selfLabel = WidgetElem.SeekDescendant(sheetTrunk, SelfLabelPhraseIdent) as WidgetPhrase;
        WidgetPhrase? selfFollowers = WidgetElem.SeekDescendant(sheetTrunk, SelfFollowersPhraseIdent) as WidgetPhrase;
        WidgetPhrase? selfGrade = WidgetElem.SeekDescendant(sheetTrunk, SelfGradePhraseIdent) as WidgetPhrase;
        WidgetPhrase? monarchCaption = WidgetElem.SeekDescendant(monarchField, MonarchCaptionPhraseIdent) as WidgetPhrase;
        WidgetPhrase? monarchMoniker = WidgetElem.SeekDescendant(monarchField, MonarchLabelPhraseIdent) as WidgetPhrase;
        WidgetPhrase? monarchFollowers = WidgetElem.SeekDescendant(monarchField, MonarchFollowersPhraseIdent) as WidgetPhrase;
        WidgetPhrase? patronLabel = WidgetElem.SeekDescendant(patronField, PatronLabelPhraseIdent) as WidgetPhrase;

        WidgetBlueprintRosterBbox? vassalRosterBbox =
            WidgetElem.SeekDescendant(sheetTrunk, VassalRosterBboxIdent) as WidgetBlueprintRosterBbox;
        if (vassalRosterBbox is null)
            Console.WriteLine(
                $"[UI] SocialAllegianceSheetDriver: ListBox 0x{VassalRosterBboxIdent:X8} not "
                + "found - the vassal list will not populate");
        else
        {
            BindBranch(vassalRosterBbox, mappings, sheetTrunk);
        }

        WidgetBtn? ignoreReqsTickbox =
            WidgetElem.SeekDescendant(sheetTrunk, IgnoreReqsTickboxIdent) as WidgetBtn;
        WidgetBtn? swearBtn = WidgetElem.SeekDescendant(sheetTrunk, SwearBtnIdent) as WidgetBtn;
        WidgetBtn? breakBtn = WidgetElem.SeekDescendant(sheetTrunk, BreakBtnIdent) as WidgetBtn;
        WidgetBtn? kickBtn = WidgetElem.SeekDescendant(sheetTrunk, KickBtnIdent) as WidgetBtn;

        string? monarchCaptionLegend = mappings.ResolveString(
            StringChartIdent, DatStringPicker.CalculateDigest("ID_Allegiance_MonarchLabel"));
        string? patronSlashMonarchCaptionLegend = mappings.ResolveString(
            StringChartIdent, DatStringPicker.CalculateDigest("ID_Allegiance_PatronSlashMonarchLabel"));

        var driver = new SocialAllegianceSheetDriver(
            mappings,
            selfLabel, selfFollowers, selfGrade,
            monarchField, monarchCaption, monarchMoniker, monarchFollowers,
            monarchIsPatronSubChunk, monarchExperiencePassedUp,
            patronField, patronLabel, patronExperiencePassedUp,
            vassalRosterBbox, ignoreReqsTickbox, swearBtn, breakBtn, kickBtn,
            monarchCaptionLegend, patronSlashMonarchCaptionLegend);

        driver.WireBtns();
        driver.WireTickbox();
        driver.Tick();

        driver.AssignSheetShown(true);

        return driver;
    }

    private static void BindBranch(WidgetBlueprintRosterBbox vassalRosterBbox, Bindings mappings, WidgetElem sheetTrunk)
    {
        vassalRosterBbox.TemplateResolver = mappings.TemplateResolver;
        uint scrollerElemIdent = vassalRosterBbox.ScrollbarElementId;
        WidgetElem? scrollerElem = scrollerElemIdent is 0
                        ? null
                        : WidgetElem.SeekDescendant(sheetTrunk, scrollerElemIdent);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = vassalRosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] SocialAllegianceSheetDriver: scrollbar 0x{scrollerElemIdent:X8} "
                + "not found - the vassal list will not scroll");
    }

    public void Tick()
    {
        var capture = _bindings.Snapshot();
        uint selfOid = _bindings.LocalPlayerGuid();
        SimAllegianceMemberCapture? monarch = capture.HasProfile ? _bindings.Monarch() : null;
        SimAllegianceMemberCapture? patron = capture.HasProfile ? _bindings.Patron(selfOid) : null;

        RenewSelfChunk(capture);
        RenewMonarchChunk(capture, monarch, patron);
        RenewPatronChunk(capture, monarch, patron);

        if (capture.Revision != _previousLineupRev)
        {
            _previousLineupRev = capture.Revision;
            RenewLineup(selfOid);
        }

        RenewTickboxPick();
        RenewBtnPhases(capture, selfOid, patron);
    }

    public void AssignSheetShown(bool shown)
    {
        if (_subscribed == shown) return;
        if (_bindings.SetUpdateSubscription(shown).Status == SimDirectiveStatus.Accepted)
            _subscribed = shown;
    }

    public void RestartSheetShownLatch() => _subscribed = false;

    public void RedeclareFollowingRealmListing()
    {
        if (_bindings.SetUpdateSubscription(true).Status == SimDirectiveStatus.Accepted)
            _subscribed = true;
    }

    private void WireBtns()
    {
        _swearBtn?.OnClick = OnSwearPress;
        _breakBtn?.OnClick = OnBreakPress;
        _kickBtn?.OnClick = OnKickPress;
    }

    private void WireTickbox()
    {
        if (_ignoreReqsTickbox is null) return;

        const string captionTag = "ID_PlayerOption_IgnoreAllegianceRequests";
        string? caption = _bindings.ResolveString(KnobStringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (caption is not null)
            _ignoreReqsTickbox.Label = caption;
        else
            Console.WriteLine(
                $"[UI] SocialAllegianceSheetDriver: label '{captionTag}' didn't resolve - "
                + "checkbox renders with no caption rather than invented English");

        string? hint = _bindings.ResolveString(
            KnobStringChartIdent, DatStringPicker.CalculateDigest(captionTag + "_Help"));
        if (hint is not null)
            _ignoreReqsTickbox.TooltipText = hint;

        _ignoreReqsTickbox.SuppressSelfFlip = true;
        _ignoreReqsTickbox.OnClick = () =>
        {
            bool upcoming = !_bindings.CurrentCharacterOption(CharacterOptionId.IgnoreAllegianceRequests);
            _bindings.SetCharacterOption(CharacterOptionId.IgnoreAllegianceRequests, upcoming);
        };
    }

    private void OnSwearPress()
    {
        if (_bindings.Selection.ChosenObjectTag is not { } markOid) return;
        string? label = _bindings.ResolveWorldObjectName(markOid);
        if (string.IsNullOrEmpty(label)) return;

        string msg = _bindings.ResolvePlayerTemplate?.Invoke(
            "ID_Allegiance_SwearConfirmation", label) ?? label;
        _bindings.ShowConfirmation(msg, approved =>
        {
            if (approved) _bindings.Swear(markOid);
        });
    }

    private void OnBreakPress()
    {
        uint selfOid = _bindings.LocalPlayerGuid();
        if (_bindings.Patron(selfOid) is not { } patron) return;

        string msg = _bindings.ResolvePlayerTemplate?.Invoke(
            "ID_Allegiance_BreakConfirmation", patron.Name) ?? patron.Name;
        _bindings.ShowConfirmation(msg, approved =>
        {
            if (approved) _bindings.Break(patron.CharacterId);
        });
    }

    private void OnKickPress()
    {
        if (_chosenVassalOid is 0u) return;
        if (_bindings.Member(_chosenVassalOid) is not { } vassal) return;

        uint vassalOid = _chosenVassalOid;
        string msg = _bindings.ResolvePlayerTemplate?.Invoke(
            "ID_Allegiance_KickConfirmation", vassal.Name) ?? vassal.Name;
        _bindings.ShowConfirmation(msg, approved =>
        {
            if (approved) _bindings.Kick(vassalOid);
        });
    }

    private void RenewSelfChunk(SimAllegianceCapture capture)
    {
        AssignStroke(_selfLabel, ref _previousSelfLabel, capture.AllegianceName, PhraseTint);
        AssignStroke(_selfFollowers, ref _previousSelfFollowers, $"Followers: {capture.TotalVassals}", PhraseTint);
        AssignStroke(_selfGrade, ref _previousSelfGrade, $"Rank: [{capture.Rank}]", PhraseTint);
    }

    private void RenewMonarchChunk(
        SimAllegianceCapture capture,
        SimAllegianceMemberCapture? monarch,
        SimAllegianceMemberCapture? patron)
    {
        bool hasMonarch = monarch is { } m && m.CharacterId != _bindings.LocalPlayerGuid();
        _monarchField.Visible = hasMonarch;

        if (!hasMonarch)
        {
            AssignSupplier(_monarchMoniker, ref _previousMonarchLabel, BlankSentinel, BlankStrokeSupplier);
            AssignSupplier(_monarchFollowers, ref _previousMonarchFollowers, BlankSentinel, BlankStrokeSupplier);
            _monarchIsPatronSubChunk?.Visible = false;
            return;
        }

        var monarchBlob = monarch!.Value;
        AssignStroke(_monarchMoniker, ref _previousMonarchLabel, monarchBlob.Name, PhraseTint);
        AssignStroke(
            _monarchFollowers,
            ref _previousMonarchFollowers,
            $"Followers: {(capture.TotalMembers >= 1u ? capture.TotalMembers - 1u : 0u)}",
            PhraseTint);
        _monarchField.Enabled = monarchBlob.IsLoggedIn;

        bool patronIsMonarch = patron is { } p && p.CharacterId == monarchBlob.CharacterId;
        string? caption = patronIsMonarch ? _patronSlashMonarchCaptionLegend : _monarchCaptionLegend;
        if (_monarchCaption is not null && caption is not null)
            _monarchCaption.StrokesSupplier = () => [new WidgetPhrase.Line(caption, PhraseTint)];

        _monarchIsPatronSubChunk?.Visible = patronIsMonarch;

        if (patronIsMonarch)
        {
            uint tithed = _bindings.Member(_bindings.LocalPlayerGuid())?.CpTithed ?? 0u;
            AssignStroke(_monarchExperiencePassedUp, ref _previousMonarchExperiencePassedUp, tithed.ToString(), PhraseTint);
        }
    }

    private void RenewPatronChunk(
        SimAllegianceCapture capture,
        SimAllegianceMemberCapture? monarch,
        SimAllegianceMemberCapture? patron)
    {
        bool hasPatron = patron is { } p
            && (monarch is not { } m || p.CharacterId != m.CharacterId);
        _patronField.Visible = hasPatron;

        if (!hasPatron)
        {
            AssignSupplier(_patronLabel, ref _previousPatronLabel, BlankSentinel, BlankStrokeSupplier);
            return;
        }

        var patronBlob = patron!.Value;
        AssignStroke(_patronLabel, ref _previousPatronLabel, patronBlob.Name, PhraseTint);
        _patronField.Enabled = patronBlob.IsLoggedIn;

        uint tithed = _bindings.Member(_bindings.LocalPlayerGuid())?.CpTithed ?? 0u;
        AssignStroke(_patronExperiencePassedUp, ref _previousPatronExperiencePassedUp, tithed.ToString(), PhraseTint);
    }

    private void RenewTickboxPick()
    {
        if (_ignoreReqsTickbox is null) return;
        _ignoreReqsTickbox.Selected =
            _bindings.CurrentCharacterOption(CharacterOptionId.IgnoreAllegianceRequests);
    }

    private void RenewBtnPhases(
        SimAllegianceCapture capture,
        uint selfOid,
        SimAllegianceMemberCapture? patron)
    {
        if (_swearBtn is not null)
        {
            uint? markOid = _bindings.Selection.ChosenObjectTag;
            bool markValid = markOid is { } ident
                && ident != selfOid
                && _bindings.Member(ident) is null;
            _swearBtn.Enabled = patron is null && markValid;
        }

        _breakBtn?.Enabled = patron is not null;

        _kickBtn?.Enabled = _chosenVassalOid != 0u;
    }

    private void RenewLineup(uint selfOid)
    {
        if (_vassalRosterBbox is null) return;

        var vassals = new List<SimAllegianceMemberCapture>(_bindings.Vassals(selfOid));

        bool membershipAltered = vassals.Count != _vassalOids.Count;
        if (!membershipAltered)
        {
            foreach (SimAllegianceMemberCapture vassal in vassals)
            {
                if (_vassalOids.Contains(vassal.CharacterId)) continue;
                membershipAltered = true;
                break;
            }
        }

        if (membershipAltered)
            ReassembleLineup(vassals);
        else
            foreach (SimAllegianceMemberCapture vassal in vassals)
                RefreshRank(vassal);

        if (_chosenVassalOid is not 0u && !_vassalOids.Contains(_chosenVassalOid))
            _chosenVassalOid = 0u;
    }

    private void ReassembleLineup(List<SimAllegianceMemberCapture> vassals)
    {
        _vassalRosterBbox!.DrainPreservingRoll();
        _ranks.Clear();

        _vassalOids.Clear();
        foreach (SimAllegianceMemberCapture vassal in vassals)
            _vassalOids.Add(vassal.CharacterId);

        foreach (SimAllegianceMemberCapture vassal in vassals)
        {
            WidgetElem? rank = _vassalRosterBbox.AppendGearFromBlueprintRoster(0);
            if (rank is null)
            {
                Console.WriteLine(
                    "[UI] SocialAllegianceSheetDriver: vassal row template didn't "
                    + $"build for guid 0x{vassal.CharacterId:X8}.");
                continue;
            }

            VassalRankWidgets widgets = new VassalRankWidgets(
                WidgetElem.SeekDescendant(rank, RankLabelPhraseIdent) as WidgetPhrase,
                WidgetElem.SeekDescendant(rank, RankExperiencePassedUpPhraseIdent) as WidgetPhrase,
                WidgetElem.SeekDescendant(rank, RankOfflineMarkerIdent));
            _ranks[vassal.CharacterId] = widgets;

            if (widgets.Name is { } labelPhrase)
            {
                uint oid = vassal.CharacterId;
                labelPhrase.OnClick = () => PickVassal(oid);
            }
        }

        foreach (SimAllegianceMemberCapture vassal in vassals)
            RefreshRank(vassal);
    }

    private void RefreshRank(SimAllegianceMemberCapture vassal)
    {
        if (!_ranks.TryGetValue(vassal.CharacterId, out VassalRankWidgets widgets)) return;

        if (widgets.Name is { } labelPhrase)
        {
            string label = vassal.Name;
            labelPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(label, PhraseTint)];
        }

        if (widgets.ExperiencePassedUp is { } xpPhrase)
        {
            string tithed = vassal.CpTithed.ToString();
            xpPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(tithed, PhraseTint)];
        }

        widgets.OfflineMarker?.Visible = !vassal.IsLoggedIn;
    }

    private void PickVassal(uint oid)
    {
        if (_chosenVassalOid == oid) return;
        _chosenVassalOid = oid;
    }

    private static void AssignStroke(WidgetPhrase? phrase, ref string? previousVal, string newVal, Vector4 tint)
    {
        if (phrase is null) return;
        if (previousVal == newVal) return;
        previousVal = newVal;
        phrase.StrokesSupplier = () => [new WidgetPhrase.Line(newVal, tint)];
    }

    private static void AssignSupplier(
        WidgetPhrase? phrase,
        ref string? previousVal,
        string? sentinelVal,
        Func<IReadOnlyList<WidgetPhrase.Line>> supplier)
    {
        if (phrase is null) return;
        if (previousVal == sentinelVal) return;
        previousVal = sentinelVal;
        phrase.StrokesSupplier = supplier;
    }
}
