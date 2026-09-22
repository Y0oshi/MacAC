using MacAC.Client.Shell.Panels;
using MacAC.Mechanics.Comms;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell;

public sealed partial class CanonWidgetEngine
{
    public bool ProcessInputAction(MacAC.Cockpit.Input.FeedAct act)
    {
        if (AppraisalDriver?.HandleFeedAction(act) == true)
            return true;

        if (ArcanacastingWidgetDriver?.Handle(act) == true)
            return true;

        switch (act)
        {
            case MacAC.Cockpit.Input.FeedAct.CaptureScreenshot:
                _bindings.CaptureScreenshot?.Invoke();
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleHelp:
                _bindings.Options.DisplaySystemMessage(
                    "In-game help is unavailable because the retail help plugin is not installed.");
                return true;
            case MacAC.Cockpit.Input.FeedAct.TogglePluginManager:
                if (_extensionFlankBoard is not { EntryTally: > 0 } shelf)
                {
                    _bindings.Options.DisplaySystemMessage(
                        "No plugin windows are registered.");
                    return true;
                }
                if (shelf.Visible)
                {
                    shelf.Hide();
                    _bindings.Options.DisplaySystemMessage(
                        ExtensionShelfConcealedMsg());
                }
                else
                {
                    shelf.Show();
                }
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleAbuseReportingPanel:
                _bindings.Options.DisplaySystemMessage(OptionsPaneText.DossierAbuseUnavailable);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleUrgentAssistancePanel:
                _bindings.Options.DisplaySystemMessage(OptionsPaneText.UrgentAssistanceUnavailable);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ChatReply:
                _commsPaneDriver?.BeginReply(_bindings.Chat.ViewModel.LastIncomingTellSender);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ChatMonarchReply:
                _commsPaneDriver?.BeginReply(_bindings.Chat.ViewModel.PreviousMonarchSender);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ChatPatronReply:
                _commsPaneDriver?.BeginReply(_bindings.Chat.ViewModel.PreviousPatronSender);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ChatStartCommand:
                _commsPaneDriver?.BeginDirective();
                return true;
            case MacAC.Cockpit.Input.FeedAct.ChatTellToSelected:
                {
                    uint chosen = _bindings.Toolbar.Selection.ChosenObjectTag ?? 0u;
                    if (chosen is >= 0x50000001u and <= 0x6FFFFFFFu)
                    {
                        string? label = _bindings.Toolbar.ResolveName(chosen);
                        if (!string.IsNullOrEmpty(label))
                            _commsPaneDriver?.BeginTell(label);
                    }
                    return true;
                }
            case MacAC.Cockpit.Input.FeedAct.EnterChatMode:
                _commsPaneDriver?.JoinCommsManner(
                    _bindings.Keyboard?.Dispatcher?.LatestPhysicalChord);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleChatEntry:
                _commsPaneDriver?.ToggleChatEntry(
                    _bindings.Keyboard?.Dispatcher?.LatestPhysicalChord);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleCharacterInfoPanel:
                FlipPane(PaneLabels.CharacterInformation);
                return true;
            case MacAC.Cockpit.Input.FeedAct.TogglePositiveMagicPanel:
                FlipPane(PaneLabels.PositiveEffects);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleNegativeMagicPanel:
                FlipPane(PaneLabels.NegativeEffects);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleLinkStatusPanel:
                FlipPane(PaneLabels.LinkCondition);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleVitaePanel:
                FlipPane(PaneLabels.Vitae);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleSocialPanel:
                FlipPane(PaneLabels.SocialPanel);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleAllegiancePanel:
                OpenSocialBoard(SocialPanePage.Allegiance);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleFellowshipPanel:
                OpenSocialBoard(SocialPanePage.Fellowship);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleFriendsPage:
                OpenSocialBoard(SocialPanePage.Friends);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleSpellManagementPanel:
                FlipPane(PaneLabels.Spellbook);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleSpellbookPanel:
                OpenGrimoire(ArcanabookWindowPage.Spells);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleSpellComponentsPanel:
                OpenGrimoire(ArcanabookWindowPage.Components);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleCharacterDetailPanel:
                FlipPane(PaneLabels.Character);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleAttributesPanel:
                OpenToonBoard(ToonStatDriver.ToonStatTab.Attributes);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleSkillsPanel:
                OpenToonBoard(ToonStatDriver.ToonStatTab.Skills);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleCharacterTitlesPage:
                OpenToonBoard(ToonStatDriver.ToonStatTab.Titles);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleWorldPanel:
                FlipPane(PaneLabels.MapHouse);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleMapPage:
                OpenRealmBoard(unhideHouse: false);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleHousePage:
                OpenRealmBoard(unhideHouse: true);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleOptionsPanel:
                FlipPane(PaneLabels.Options);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleGameplayOptionsPage:
                OpenKnobsSheet(KnobsPanePage.Gameplay);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleCharacterSettingsPage:
                OpenKnobsSheet(KnobsPanePage.Character);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleConfigurationPage:
                OpenKnobsSheet(KnobsPanePage.Configuration);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleCompass:
                Host.SwitchPane(PaneLabels.Radar);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleKeyboardConfiguration:
                FlipPane(PaneLabels.KeyboardSettings);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleQuestJournalPage:
                OpenJournalBoard(DiaryPanePage.Notes);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleQuestDetailPanel:
                OpenJournalBoard(DiaryPanePage.Contracts);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleJournalPageList:
                OpenJournalBoard(DiaryPanePage.PageList);
                return true;
            case MacAC.Cockpit.Input.FeedAct.ToggleContractsPage:
                OpenJournalBoard(DiaryPanePage.Contracts);
                return true;
        }

        return ToolbarFeedDriver?.Handle(act) == true;
    }

    public bool ProcessAckReq(PlaySignals.ToonAckRequest req)
        => _gameplayAckDriver?.ProcessReq(req) == true;

    public bool ProcessAckDone(PlaySignals.ToonAckFinished done)
        => _gameplayAckDriver?.ProcessDone(done) == true;

    public bool ProcessAppraisal(AppraisalReader.WireParsed appraisal)
    {
        return AppraisalDriver is { } driver
                ? driver.Apply(appraisal)
                : GearDealing.AdmitAppraisalResponse(appraisal.Guid).Accepted;
    }

    private void ProcessToonEmit(
        ToonSheetSupplier supplier,
        ToonStatDriver.RaiseAsk req,
        Action finished)
    {
        if (req.Kind != ToonStatDriver.EmitMarkFlavor.TrainSkill)
        {
            supplier.ProcessEmitReq(req);
            finished();
            return;
        }

        CanonSkillTrainingConfirmationDriver confirmations =
            _aptitudeTrainingAckDriver
            ?? throw new InvalidOperationException(
                "The retail dialog factory has to be mounted prior to the character panel");

        confirmations.Request(
            req,
            supplier.AssembleSheet(),
            supplier.ProcessEmitReq,
            finished);
    }
}
