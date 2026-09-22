using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class CanonWidgetEngine
{
    private void OpenToonBoard(ToonStatDriver.ToonStatTab tab)
    {
        bool shown = Host.IsPaneShown(PaneLabels.Character);
        bool onMarkTab = _toonStatMapping?.CurrentTab() == tab;
        if (shown && onMarkTab)
        {
            ShutPane(PaneLabels.Character);
            return;
        }

        _toonStatMapping?.ShowTab(tab);
        _boardWidget.AssignBoardVis(CanonPaneRegistry.Character, shown: true);
    }

    private void OpenRealmBoard(bool unhideHouse)
    {
        bool shown = Host.IsPaneShown(PaneLabels.MapHouse);
        bool onMarkTab = unhideHouse
            ? MapHousePanelController?.IsShowingHouse == true
            : MapHousePanelController?.IsShowingLookup == true;
        if (shown && onMarkTab)
        {
            ShutPane(PaneLabels.MapHouse);
            return;
        }

        if (unhideHouse)
            MapHousePanelController?.RevealHouse();
        else
            MapHousePanelController?.RevealLookup();
        _boardWidget.AssignBoardVis(CanonPaneRegistry.LookupHouse, shown: true);
    }

    private void OpenKnobsSheet(KnobsPanePage sheet)
    {
        bool shown = Host.IsPaneShown(PaneLabels.Options);
        bool onMarkTab = sheet switch
        {
            KnobsPanePage.Gameplay => OptionsPanelController?.IsShowingGameplay == true,
            KnobsPanePage.Character => OptionsPanelController?.IsShowingToon == true,
            KnobsPanePage.Configuration => OptionsPanelController?.IsShowingConfiguration == true,
            _ => false,
        };
        if (shown && onMarkTab)
        {
            ShutPane(PaneLabels.Options);
            return;
        }

        switch (sheet)
        {
            case KnobsPanePage.Gameplay: OptionsPanelController?.RevealGameplay(); break;
            case KnobsPanePage.Character: OptionsPanelController?.RevealToon(); break;
            case KnobsPanePage.Configuration: OptionsPanelController?.RevealConfiguration(); break;
        }
        _boardWidget.AssignBoardVis(CanonPaneRegistry.Options, shown: true);
    }

    private void OpenSocialBoard(SocialPanePage sheet)
    {
        bool shown = Host.IsPaneShown(PaneLabels.SocialPanel);
        bool onMarkTab = sheet switch
        {
            SocialPanePage.Friends => SocialPanelController?.IsShowingFriends == true,
            SocialPanePage.Allegiance => SocialPanelController?.IsShowingAllegiance == true,
            SocialPanePage.Fellowship => SocialPanelController?.IsShowingFellowship == true,
            _ => false,
        };
        if (shown && onMarkTab)
        {
            ShutPane(PaneLabels.SocialPanel);
            return;
        }

        switch (sheet)
        {
            case SocialPanePage.Friends: SocialPanelController?.RevealFriends(); break;
            case SocialPanePage.Allegiance: SocialPanelController?.RevealAllegiance(); break;
            case SocialPanePage.Fellowship: SocialPanelController?.RevealFellowship(); break;
        }
        _boardWidget.AssignBoardVis(CanonPaneRegistry.SocialBoard, shown: true);
    }

    private void OpenJournalBoard(DiaryPanePage sheet)
    {
        switch (sheet)
        {
            case DiaryPanePage.Contracts: JournalPanelController?.RevealContracts(); break;
            case DiaryPanePage.Notes: JournalPanelController?.RevealNotes(); break;
            case DiaryPanePage.PageList: JournalPanelController?.RevealSheetRoster(); break;
        }
        _boardWidget.AssignBoardVis(CanonPaneRegistry.Journal, shown: true);
    }

    private void OpenGrimoire(ArcanabookWindowPage sheet)
    {
        bool shown = Host.IsPaneShown(PaneLabels.Spellbook);
        if (shown && ArcanabookPaneDriver?.LatestSheet == sheet)
            ShutPane(PaneLabels.Spellbook);
        else
        {
            ArcanabookPaneDriver?.RevealSheet(sheet);
            _boardWidget.AssignBoardVis(CanonPaneRegistry.Magic, shown: true);
        }
    }

    private void OpenCredits()
    {
        if (IsDisposalComplete)
            return;

        var toons =
            ToonManagementDriver;
        if (toons is null)
            return;

        if (CreditsDriver is null)
        {
            var popups = SecurePopupMaker();
            var assetList = PullCreditsAssetList();
            if (popups is null || assetList is null)
                return;

            CreditsDriver = CreditsWidgetDriver.BuildDetached(
                Host.Root,
                assetList,
                popups,
                static () => System.Diagnostics.Stopwatch.GetTimestamp()
                    / (double)System.Diagnostics.Stopwatch.Frequency,
                _bindings.Assets.ResolveSprite,
                YieldFromCredits);
            if (CreditsDriver is null)
                return;
        }

        toons.AssignExhibitSuppressed(true);
        try
        {
            CreditsDriver.Engage();
        }
        catch (Exception problem)
        {
            toons.AssignExhibitSuppressed(false);
            Console.WriteLine(
                $"[UI] credits activation failed: {problem.Message}");
        }
    }
}
