using MacAC.Mechanics.Targeting;
using MacAC.Sim;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed partial class SocialFellowshipSheetDriver
{
    public static SocialFellowshipSheetDriver? Bind(WidgetElem sheetTrunk, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(sheetTrunk);
        ArgumentNullException.ThrowIfNull(mappings);

        if (WidgetElem.SeekDescendant(sheetTrunk, 0x1000026Bu) is not { } notIn
            || WidgetElem.SeekDescendant(sheetTrunk, 0x10000275u) is not { } inFellowship)
        {
            Console.WriteLine(
                "[UI] SocialFellowshipSheetDriver: empty/full frame pair "
                + "(0x1000026B/0x10000275) not found - fellowship page will not "
                + "swap its empty state");
            return null;
        }

        WidgetBlueprintRosterBbox? rosterBbox = WidgetElem.SeekDescendant(sheetTrunk, RosterBboxIdent) as WidgetBlueprintRosterBbox;
        if (rosterBbox is null)
            Console.WriteLine(
                $"[UI] SocialFellowshipSheetDriver: ListBox 0x{RosterBboxIdent:X8} not "
                + "found - the fellowship roster will not populate");
        else
        {
            BindBranch(rosterBbox, mappings, sheetTrunk);
        }

        WidgetField? labelField = WidgetElem.SeekDescendant(sheetTrunk, LabelListingBboxIdent) as WidgetField;
        if (labelField is null)
            Console.WriteLine(
                $"[UI] SocialFellowshipSheetDriver: name-entry field 0x{LabelListingBboxIdent:X8} "
                + "not found (or not authored Editable) - Create will not read a typed name");

        WidgetBtn? buildBtn = WidgetElem.SeekDescendant(sheetTrunk, BuildBtnIdent) as WidgetBtn;
        WidgetPhrase? fellowshipLabelPhrase = WidgetElem.SeekDescendant(sheetTrunk, FellowshipLabelPhraseIdent) as WidgetPhrase;
        WidgetBtn? leaderBtn = WidgetElem.SeekDescendant(sheetTrunk, LeaderBtnIdent) as WidgetBtn;
        WidgetBtn? quitBtn = WidgetElem.SeekDescendant(sheetTrunk, QuitBtnIdent) as WidgetBtn;
        WidgetBtn? openBtn = WidgetElem.SeekDescendant(sheetTrunk, OpenBtnIdent) as WidgetBtn;
        WidgetBtn? recruitBtn = WidgetElem.SeekDescendant(sheetTrunk, RecruitBtnIdent) as WidgetBtn;
        WidgetBtn? dismissBtn = WidgetElem.SeekDescendant(sheetTrunk, DismissBtnIdent) as WidgetBtn;
        WidgetBtn? disbandBtn = WidgetElem.SeekDescendant(sheetTrunk, DisbandBtnIdent) as WidgetBtn;
        WidgetBtn? ignoreReqsTickbox = WidgetElem.SeekDescendant(sheetTrunk, IgnoreReqsTickboxIdent) as WidgetBtn;
        WidgetBtn? autoAdmitTickbox = WidgetElem.SeekDescendant(sheetTrunk, AutoAdmitTickboxIdent) as WidgetBtn;
        WidgetBtn? portionXpTickbox = WidgetElem.SeekDescendant(sheetTrunk, PortionXpTickboxIdent) as WidgetBtn;
        WidgetBtn? portionLootTickbox = WidgetElem.SeekDescendant(sheetTrunk, PortionLootTickboxIdent) as WidgetBtn;

        string? openLegend = mappings.ResolveString(
            FellowshipStringChartIdent, DatStringPicker.CalculateDigest("ID_Fellowship_OpenFellowshipButtonText"));
        string? shutLegend = mappings.ResolveString(
            FellowshipStringChartIdent, DatStringPicker.CalculateDigest("ID_Fellowship_CloseFellowshipButtonText"));
        if (openLegend is null || shutLegend is null)
            Console.WriteLine(
                "[UI] SocialFellowshipSheetDriver: Open/Close Fellowship button caption(s) "
                + "didn't resolve - the button keeps its imported caption rather than "
                + "invented English");

        var driver = new SocialFellowshipSheetDriver(
            notIn,
            inFellowship,
            mappings,
            rosterBbox,
            labelField,
            buildBtn,
            fellowshipLabelPhrase,
            leaderBtn,
            quitBtn,
            openBtn,
            recruitBtn,
            dismissBtn,
            disbandBtn,
            ignoreReqsTickbox,
            autoAdmitTickbox,
            portionXpTickbox,
            portionLootTickbox,
            openLegend,
            shutLegend);

        driver.WireBtns();
        driver.WireTickboxes();
        driver.Tick();
        return driver;
    }

    private static void BindBranch(WidgetBlueprintRosterBbox rosterBbox, Bindings mappings, WidgetElem sheetTrunk)
    {
        rosterBbox.TemplateResolver = mappings.TemplateResolver;
        uint scrollerElemIdent = rosterBbox.ScrollbarElementId;
        WidgetElem? scrollerElem = scrollerElemIdent is 0
                        ? null
                        : WidgetElem.SeekDescendant(sheetTrunk, scrollerElemIdent);
        if (scrollerElem is WidgetScroller scroller)
            scroller.Model = rosterBbox.Scroll;
        else
            Console.WriteLine(
                $"[UI] SocialFellowshipSheetDriver: scrollbar 0x{scrollerElemIdent:X8} "
                + "not found - the fellowship roster will not scroll");
    }

    public void Tick()
    {
        var capture = _bindings.Snapshot();
        bool inFellowship = capture.IsInFellowship;
        _notInFellowshipCycle.Visible = !inFellowship;
        _inFellowshipCycle.Visible = inFellowship;

        RenewTickboxSelections();
        RenewBuildBtnPhase();

        if (!inFellowship)
        {
            if (_ranks.Count is not 0)
            {
                _rosterBbox?.Flush();
                _ranks.Clear();
            }
            _participantOids.Clear();
            _chosenFellowOid = 0u;
            _previousLineupRev = long.MinValue;
            _previousOpenPhase = null;
            _previousFellowshipLabel = null;
            _fellowshipLabelStrokesSupplier = null;
            return;
        }

        RenewFellowshipLabel(capture.Name);

        if (capture.Revision != _previousLineupRev)
        {
            _previousLineupRev = capture.Revision;
            RenewLineup(capture);
        }

        SynchronizePickFromRealm();

        RenewBtnPhases(capture);
        RenewOpenLegend(capture);
    }

    public void ApplySheetShown(bool shown)
    {
        if (_sheetShown == shown) return;
        if (_bindings.SetPanelOpen(shown).Status == SimDirectiveStatus.Accepted)
            _sheetShown = shown;
    }

    public void RewindSheetShownLatch() => _sheetShown = false;

    private void AttachTickbox(WidgetBtn? tickbox, CharacterOptionId ident, string canonLabel)
    {
        if (tickbox is null) return;

        string captionTag = $"ID_PlayerOption_{canonLabel}";
        string? caption = _bindings.ResolveString(KnobStringChartIdent, DatStringPicker.CalculateDigest(captionTag));
        if (caption is not null)
            tickbox.Label = caption;
        else
            Console.WriteLine(
                $"[UI] SocialFellowshipSheetDriver: label '{captionTag}' didn't resolve - "
                + "checkbox renders with no caption rather than invented English");

        string? hint = _bindings.ResolveString(
            KnobStringChartIdent, DatStringPicker.CalculateDigest(captionTag + "_Help"));
        if (hint is not null)
            tickbox.TooltipText = hint;

        tickbox.SuppressSelfFlip = true;
        tickbox.OnClick = () =>
        {
            bool upcoming = !_bindings.CurrentCharacterOption(ident);
            _bindings.SetCharacterOption(ident, upcoming);
        };
    }

    private void WireBtns()
    {
        _buildBtn?.OnClick = () =>
            {
                string moniker = _labelField?.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(moniker)) return;
                bool portionXp = _bindings.CurrentCharacterOption(CharacterOptionId.FellowshipShareXP);
                _bindings.Create(moniker, portionXp);
            };

        _recruitBtn?.OnClick = () =>
            {
                if (_bindings.Selection.ChosenObjectTag is { } markOid)
                    _bindings.Recruit(markOid);
            };

        _dismissBtn?.OnClick = () =>
            {
                if (_chosenFellowOid is not 0u) _bindings.Dismiss(_chosenFellowOid);
            };

        _leaderBtn?.OnClick = () =>
            {
                if (_chosenFellowOid is not 0u) _bindings.AssignLeader(_chosenFellowOid);
            };

        _quitBtn?.OnClick = () => _bindings.Quit(false);
        _disbandBtn?.OnClick = () => _bindings.Quit(true);

        _openBtn?.OnClick = () =>
            {
                var capture = _bindings.Snapshot();
                bool newOpenPhase = !capture.IsOpen;
                _bindings.SetOpen(newOpenPhase);

                _previousOpenPhase = newOpenPhase;
                string? caption = newOpenPhase ? _shutLegend : _openLegend;
                if (caption is not null)
                    _openBtn.Label = caption;
            };
    }

    private void WireTickboxes()
    {
        AttachTickbox(_ignoreReqsTickbox, CharacterOptionId.IgnoreFellowshipRequests, "IgnoreFellowshipRequests");
        AttachTickbox(_autoAdmitTickbox, CharacterOptionId.FellowshipAutoAcceptRequests, "FellowshipAutoAcceptRequests");
        AttachTickbox(_portionXpTickbox, CharacterOptionId.FellowshipShareXP, "FellowshipShareXP");
        AttachTickbox(_portionLootTickbox, CharacterOptionId.FellowshipShareLoot, "FellowshipShareLoot");
    }

    private static void AssignVitals(WidgetGauge? gauge, uint latest, uint upper)
    {
        if (gauge is null) return;
        gauge.Populate = () => upper > 0u ? (float)latest / upper : 0f;
        gauge.Label = () => $"{latest}/{upper}";
    }

    private void AssignChosenFellow(uint oid)
    {
        if (_chosenFellowOid == oid) return;
        _chosenFellowOid = oid;

        if (_ranks.Count is 0) return;
        var capture = _bindings.Snapshot();
        foreach (SimFellowMemberCapture participant in _bindings.Members())
            RefreshRank(participant, capture);
    }

    private void RenewBuildBtnPhase()
    {
        if (_buildBtn is null) return;
        string label = _labelField?.Text ?? string.Empty;
        _buildBtn.Enabled = !string.IsNullOrWhiteSpace(label);
    }

    private void RenewTickboxSelections()
    {
        _ignoreReqsTickbox?.Selected =
                _bindings.CurrentCharacterOption(CharacterOptionId.IgnoreFellowshipRequests);
        _autoAdmitTickbox?.Selected =
                _bindings.CurrentCharacterOption(CharacterOptionId.FellowshipAutoAcceptRequests);
        _portionXpTickbox?.Selected =
                _bindings.CurrentCharacterOption(CharacterOptionId.FellowshipShareXP);
        _portionLootTickbox?.Selected =
                _bindings.CurrentCharacterOption(CharacterOptionId.FellowshipShareLoot);
    }

    private void RenewFellowshipLabel(string label)
    {
        if (_fellowshipLabelPhrase is null) return;
        if (_fellowshipLabelStrokesSupplier is not null && _previousFellowshipLabel == label) return;

        _previousFellowshipLabel = label;
        _fellowshipLabelStrokesSupplier = () => [new WidgetPhrase.Line(label, ParticipantLabelTint)];
        _fellowshipLabelPhrase.StrokesSupplier = _fellowshipLabelStrokesSupplier;
    }

    private void RenewOpenLegend(SimFellowsCapture capture)
    {
        if (_openBtn is null) return;
        if (_previousOpenPhase == capture.IsOpen) return;
        _previousOpenPhase = capture.IsOpen;

        string? caption = capture.IsOpen ? _shutLegend : _openLegend;
        if (caption is not null)
            _openBtn.Label = caption;
    }

    private void RenewBtnPhases(SimFellowsCapture capture)
    {
        uint selfOid = _bindings.LocalPlayerGuid();
        bool isLeader = capture.LeaderGuid is not 0u && capture.LeaderGuid == selfOid;
        bool hasPick = _chosenFellowOid is not 0u;
        bool chosenIsSelf = hasPick && _chosenFellowOid == selfOid;

        _quitBtn?.Enabled = true; // always, while in a fellowship
        _disbandBtn?.Enabled = isLeader;
        _openBtn?.Enabled = isLeader;
        _leaderBtn?.Enabled = isLeader && hasPick && !chosenIsSelf;
        _dismissBtn?.Enabled = isLeader && hasPick && !chosenIsSelf;

        if (_recruitBtn is not null)
        {
            uint? markOid = _bindings.Selection.ChosenObjectTag;
            bool markValid = markOid is { } ident && ident != selfOid && !_participantOids.Contains(ident);
            bool notWhole = capture.MemberCount < UpperFellowshipDims;
            _recruitBtn.Enabled = markValid && notWhole && (isLeader || capture.IsOpen);
        }
    }

    private void RenewLineup(SimFellowsCapture capture)
    {
        if (_rosterBbox is null) return;

        var participants = new List<SimFellowMemberCapture>(_bindings.Members());

        bool membershipAltered = participants.Count != _participantOids.Count;
        if (!membershipAltered)
        {
            foreach (SimFellowMemberCapture participant in participants)
            {
                if (_participantOids.Contains(participant.Guid)) continue;
                membershipAltered = true;
                break;
            }
        }

        if (membershipAltered)
            ReassembleLineup(participants, capture);
        else
            foreach (SimFellowMemberCapture participant in participants)
                RefreshRank(participant, capture);

        if (_chosenFellowOid is not 0u && !_participantOids.Contains(_chosenFellowOid))
            _chosenFellowOid = 0u;
    }

    private void ReassembleLineup(List<SimFellowMemberCapture> participants, SimFellowsCapture capture)
    {
        _rosterBbox!.DrainPreservingRoll();
        _ranks.Clear();

        _participantOids.Clear();
        foreach (SimFellowMemberCapture participant in participants)
            _participantOids.Add(participant.Guid);

        foreach (SimFellowMemberCapture participant in participants)
        {
            WidgetElem? rank = _rosterBbox.AppendGearFromBlueprintRoster(0);
            if (rank is null)
            {
                Console.WriteLine(
                    "[UI] SocialFellowshipSheetDriver: fellow row template didn't "
                    + $"build for guid 0x{participant.Guid:X8}.");
                continue;
            }

            FellowRankWidgets widgets = new FellowRankWidgets(
                WidgetElem.SeekDescendant(rank, RankLabelBandIdent) as WidgetDatElement,
                WidgetElem.SeekDescendant(rank, RankLabelPhraseIdent) as WidgetPhrase,
                WidgetElem.SeekDescendant(rank, RankStatsPhraseIdent) as WidgetPhrase,
                WidgetElem.SeekDescendant(rank, RankHealthGaugeIdent) as WidgetGauge,
                WidgetElem.SeekDescendant(rank, RankStaminaGaugeIdent) as WidgetGauge,
                WidgetElem.SeekDescendant(rank, RankManaGaugeIdent) as WidgetGauge);
            _ranks[participant.Guid] = widgets;

            if (widgets.Name is { } labelPhrase)
            {
                uint oid = participant.Guid;
                labelPhrase.OnClick = () => PickFellow(oid);
            }
            if (widgets.Stats is { } statsPhrase)
            {
                uint oid = participant.Guid;
                statsPhrase.OnClick = () => PickFellow(oid);
            }
        }

        foreach (SimFellowMemberCapture participant in participants)
            RefreshRank(participant, capture);
    }

    private void RefreshRank(SimFellowMemberCapture participant, SimFellowsCapture capture)
    {
        if (!_ranks.TryGetValue(participant.Guid, out FellowRankWidgets widgets)) return;

        if (widgets.Name is { } labelPhrase)
        {
            string label = participant.Name;
            labelPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(label, ParticipantLabelTint)];
        }

        if (widgets.NameBand is { } labelBand)
            labelBand.EngagedPhase = _chosenFellowOid == participant.Guid ? "Highlight" : "";

        if (widgets.Stats is { } statsPhrase)
        {
            string phrase = ComposeStatsPhrase(participant, capture);
            statsPhrase.StrokesSupplier = () => [new WidgetPhrase.Line(phrase, ParticipantLabelTint)];
        }

        AssignVitals(widgets.Health, participant.CurrentHealth, participant.MaxHealth);
        AssignVitals(widgets.Stamina, participant.CurrentStamina, participant.MaxStamina);
        AssignVitals(widgets.Mana, participant.CurrentMana, participant.MaxMana);
    }

    private static string ComposeStatsPhrase(SimFellowMemberCapture participant, SimFellowsCapture capture)
    {
        if (!capture.ShareXp)
            return $"{participant.Level}  0%";
        if (capture.EvenXpSplit)
        {
            float pct = EvenDividePct(capture.MemberCount);
            return $"{participant.Level}  {(int)((double)pct * 100.0)}%";
        }
        return participant.Level.ToString();
    }

    private static float EvenDividePct(int participantTally)
    {
        return participantTally is >= 1 and <= 10 ? EvenDividePctChart[participantTally - 1] : 0f;
    }

    private void PickFellow(uint oid)
    {
        AssignChosenFellow(oid);
        _bindings.Selection.Select(oid, PickChangeSource.Social);
    }

    private void SynchronizePickFromRealm()
    {
        if (_bindings.Selection.ChosenObjectTag is { } ident && _participantOids.Contains(ident))
            AssignChosenFellow(ident);
    }
}
