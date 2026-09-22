using MacAC.Mechanics.Genesis;
using MacAC.Sim;
using MacAC.Sim.Presence;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonCreationWidgetDriver
{
    internal WidgetElem Root => _arrangement.Root;

    internal WidgetViewport? LooksViewRect => _looksSheet.Viewport;

    internal MacAC.Client.Graphics.IClientChargenPreviewControl? LooksPreviewControl
    {
        get => _looksSheet.PreviewControl;
        set => _looksSheet.PreviewControl = value;
    }

    internal IGenesisPalSetSource? LooksPalSetSrc
    {
        get => _looksSheet.PalSetSrc;
        set => _looksSheet.PalSetSrc = value;
    }

    internal IGenesisGarbTableSource? LooksClothingChartSrc
    {
        get => _looksSheet.ClothingChartSrc;
        set => _looksSheet.ClothingChartSrc = value;
    }

    internal IGenesisPaletteColorSource? LooksSwatchTintSrc
    {
        get => _looksSheet.SwatchTintSrc;
        set => _looksSheet.SwatchTintSrc = value;
    }

    internal IChargenSwatchBitmapOrigin? LooksSwatchTextureSrc
    {
        get => _looksSheet.SwatchTextureSrc;
        set => _looksSheet.SwatchTextureSrc = value;
    }

    internal bool IsLooksSheetShown => Root.Visible && _looksSheetTrunk.Visible;

    internal bool IsSummarySheetShown => Root.Visible && _summarySheetTrunk.Visible;

    internal WidgetViewport? SummaryViewRect => _summarySheet.Viewport;

    internal MacAC.Client.Graphics.IClientChargenPreviewControl? SummaryPreviewControl
    {
        get => _summarySheet.PreviewControl;
        set => _summarySheet.PreviewControl = value;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        try
        {
            ShutAllPopups(suppressHooks: true);
        }
        finally
        {
            _hub.RevokeFixedCanvas(this);
            _back.OnClick = null;
            _upcoming.OnClick = null;
            _complete.OnClick = null;
            _help.OnClick = null;
            _quit.OnClick = null;
            _random.OnClick = null;
            _lineageTab.OnClick = null;
            _professionTab.OnClick = null;
            _aptitudesTab.OnClick = null;
            _looksTab.OnClick = null;
            _townTab.OnClick = null;
            _summaryTab.OnClick = null;
            _lineageSheet.Dispose();
            _professionSheet.Dispose();
            _aptitudesSheet.Dispose();
            _townSheet.Dispose();
            _looksSheet.Dispose();
            _summarySheet.Dispose();
            _hub.DropDescendant(Root);
        }
    }

    internal static ToonCreationWidgetDriver? BuildDetached(
        WidgetTrunk hub,
        ImportedArrangement arrangement,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        CanonPromptMint popups,
        ToonCreationEngineWiring mappings,
        PromptStrings texts)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(popups);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(texts);

        if (arrangement.Root.DatElemIdent != TrunkElemIdent
            || arrangement.SeekElem(HeadwayBarElemIdent) is not { } headwayBar
            || arrangement.SeekElem(BackElemIdent) is not WidgetBtn back
            || arrangement.SeekElem(UpcomingElemIdent) is not WidgetBtn upcoming
            || arrangement.SeekElem(CompleteElemIdent) is not WidgetBtn complete
            || arrangement.SeekElem(HelpElemIdent) is not WidgetBtn help
            || arrangement.SeekElem(QuitElemIdent) is not WidgetBtn quit
            || arrangement.SeekElem(RandomElemIdent) is not WidgetBtn random
            || arrangement.SeekElem(MasterSheetElemIdent) is not { } masterSheet
            || arrangement.SeekElem(LineageSheetElemIdent) is not { } lineageSheetTrunk
            || arrangement.SeekElem(ProfessionSheetElemIdent) is not { } professionSheetTrunk
            || arrangement.SeekElem(AptitudesSheetElemIdent) is not { } aptitudesSheetTrunk
            || arrangement.SeekElem(LooksSheetElemIdent) is not { } looksSheetTrunk
            || arrangement.SeekElem(TownSheetElemIdent) is not { } townSheetTrunk
            || arrangement.SeekElem(SummarySheetElemIdent) is not { } summarySheetTrunk
            || arrangement.SeekElem(LineageTabElemIdent) is not WidgetBtn lineageTab
            || arrangement.SeekElem(ProfessionTabElemIdent) is not WidgetBtn professionTab
            || arrangement.SeekElem(AptitudesTabElemIdent) is not WidgetBtn aptitudesTab
            || arrangement.SeekElem(LooksTabElemIdent) is not WidgetBtn looksTab
            || arrangement.SeekElem(TownTabElemIdent) is not WidgetBtn townTab
            || arrangement.SeekElem(SummaryTabElemIdent) is not WidgetBtn summaryTab)
        {
            Console.WriteLine(
                "[UI] character creation: the authored root/master-shell contract is incomplete");
            return null;
        }

        return new ToonCreationWidgetDriver(
            hub,
            arrangement,
            headwayBar,
            back,
            upcoming,
            complete,
            help,
            quit,
            random,
            masterSheet,
            lineageSheetTrunk,
            professionSheetTrunk,
            aptitudesSheetTrunk,
            looksSheetTrunk,
            townSheetTrunk,
            summarySheetTrunk,
            lineageTab,
            professionTab,
            aptitudesTab,
            looksTab,
            townTab,
            summaryTab,
            blueprintLocator,
            popups,
            mappings,
            texts);
    }

    internal void FastenAndBeat()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (Root.Ancestor is null)
            _hub.AddChild(Root);
        Tick();
    }

    internal void Tick()
    {
        if (_destroyed)
            return;

        var lens = _bindings.View();
        SimToonGenesisCapture capture = lens?.Snapshot ?? default;
        if (lens is null || !capture.IsActive)
        {
            Disengage();
            _previousGen = capture.Generation;
            _previousRev = capture.Revision;
            return;
        }

        if (!_engaged)
        {
            _engaged = true;
            if (_bindings.OpenOnStart && !_openOnBeginConsumed)
            {
                _openOnBeginConsumed = true;
                Open();
            }
        }

        if (_isOpen)
        {
            Root.Visible = true;
            _hub.BringToFront(Root);
        }
        else
        {
            Root.Visible = false;
        }

        if (_previousGen != capture.Generation
            || _previousRev != capture.Revision)
        {
            _lineageSheet.Refresh(lens, capture);
            _professionSheet.Refresh(lens, capture);
            _aptitudesSheet.Refresh(lens, capture);
            _townSheet.Refresh(lens, capture);
            _looksSheet.Refresh(lens, capture);
            _summarySheet.Refresh(lens, capture);
            _previousGen = capture.Generation;
            _previousRev = capture.Revision;
        }

        SettlePopups(capture);
    }

    internal void Open()
    {
        if (_destroyed)
            return;
        _isOpen = true;
        _hub.DeclareFixedCanvas(this, _authoredCanvas);
        RollOpeningToon();
        ImposeHeadwayPhase(Page.Heritage);
    }

    private void RollOpeningToon()
    {
        if (_bindings.RandomizeCharacter?.Invoke().Status != SimDirectiveStatus.Accepted)
            return;

        uint gender = _bindings.View()?.Snapshot.GenderKey ?? 0u;
        if (gender is 1u)
            _bindings.SelectGender(2u);
        else if (gender is 2u)
            _bindings.SelectGender(1u);
    }

    private void Close()
    {
        if (!_isOpen)
            return;
        _isOpen = false;
        Root.Visible = false;
        _hub.RevokeFixedCanvas(this);
    }

    private void ShutAllPopups(bool suppressHooks)
    {
        bool earlier = _suppressPopupHooks;
        _suppressPopupHooks |= suppressHooks;
        try
        {
            if (_quitPopupCtx is not 0u)
            {
                uint closing = _quitPopupCtx;
                _quitPopupCtx = 0u;
                _popups.ShutPopup(closing);
            }
            if (_creditWarningPopupCtx is not 0u)
            {
                uint closing = _creditWarningPopupCtx;
                _creditWarningPopupCtx = 0u;
                _popups.ShutPopup(closing);
            }
            if (_randomizeWarningPopupCtx is not 0u)
            {
                uint closing = _randomizeWarningPopupCtx;
                _randomizeWarningPopupCtx = 0u;
                _popups.ShutPopup(closing);
            }
            if (_noLabelWarningPopupCtx is not 0u)
            {
                uint closing = _noLabelWarningPopupCtx;
                _noLabelWarningPopupCtx = 0u;
                _popups.ShutPopup(closing);
            }
            if (_problemMsgPopupCtx is not 0u)
            {
                uint closing = _problemMsgPopupCtx;
                _problemMsgPopupCtx = 0u;
                _popups.ShutPopup(closing);
            }
        }
        finally
        {
            _suppressPopupHooks = earlier;
        }
    }

    private void OnBack()
    {
        if (_destroyed)
            return;
        if (_latestSheet <= Page.Heritage)
        {
            OnQuit();
            return;
        }
        ImposeHeadwayPhase(_latestSheet - 1);
    }

    private void OnUpcoming()
    {
        if (_destroyed)
            return;
        if (_latestSheet < Page.Summary)
            ImposeHeadwayPhase(_latestSheet + 1);
    }

    private void OnQuit()
    {
        if (_destroyed)
            return;
        if (_quitPopupCtx is not 0u)
            return;

        _quitPopupCtx = _popups.CraftAck(
            _texts.ExitWarning,
            blob =>
            {
                _quitPopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;

                if (blob.FetchBoolean(CanonPromptProperty.AckOutcome))
                {
                    Close();
                    _bindings.RequestExit();
                }
            });
    }

    private void OnRandom()
    {
        if (_destroyed)
            return;

        var lens = _bindings.View();
        if (lens is null)
            return;
        var capture = lens.Snapshot;

        switch (_latestSheet)
        {
            case Page.Heritage:
                _lineageSheet.Randomize(capture);
                break;
            case Page.Profession:
                _professionSheet.Randomize(capture);
                break;
            case Page.Appearance:
                _looksSheet.Randomize();
                break;
            case Page.Town:
                _townSheet.Randomize(lens);
                break;
            case Page.Summary:
                RevealRandomizeWarningPopup();
                break;
        }
    }

    private void OnComplete()
    {
        if (_destroyed || _latestSheet != Page.Summary)
            return;
        TryComplete(confirmedUnspentCredits: false);
    }

    private void RevealRandomizeWarningPopup()
    {
        if (_randomizeWarningPopupCtx is not 0u)
            return;

        _randomizeWarningPopupCtx = _popups.CraftAck(
            _texts.RandomizeWarning,
            blob =>
            {
                _randomizeWarningPopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;
                if (blob.FetchBoolean(CanonPromptProperty.AckOutcome))
                    _bindings.RandomizeCharacter?.Invoke();
            });
    }

    private void RevealNoLabelWarningPopup()
    {
        if (_noLabelWarningPopupCtx is not 0u)
            return;
        _noLabelWarningPopupCtx = _popups.CraftMsg(
            _texts.NoNameWarning,
            blob =>
            {
                _ = blob;
                _noLabelWarningPopupCtx = 0u;
            });
    }

    private void RevealCreditWarningPopup()
    {
        if (_creditWarningPopupCtx is not 0u)
            return;
        _creditWarningPopupCtx = _popups.CraftAck(
            _texts.CreditWarning,
            blob =>
            {
                _creditWarningPopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;
                if (blob.FetchBoolean(CanonPromptProperty.AckOutcome))
                    TryComplete(confirmedUnspentCredits: true);
            });
    }

    private void ImposeHeadwayPhase(Page mark)
    {
        _lineageSheetTrunk.Visible = false;
        _professionSheetTrunk.Visible = false;
        _aptitudesSheetTrunk.Visible = false;
        _looksSheetTrunk.Visible = false;
        _townSheetTrunk.Visible = false;
        _summarySheetTrunk.Visible = false;
        _upcoming.Visible = true;
        _complete.Visible = false;

        Page earlier = _latestSheet;
        _latestSheet = mark;
        _lineageTab.Selected = false;
        _professionTab.Selected = false;
        _aptitudesTab.Selected = false;
        _looksTab.Selected = false;
        _townTab.Selected = false;
        _summaryTab.Selected = false;

        uint lineageIdent = _bindings.View()?.Snapshot.HeritageId ?? 0u;
        bool isOlthoi = lineageIdent is ((uint)GenesisHeritage.Olthoi)
            or ((uint)GenesisHeritage.OlthoiAcid);
        if (isOlthoi)
        {
            _professionTab.Visible = false;
            _aptitudesTab.Visible = false;
            _townTab.Visible = false;
            if (_latestSheet < earlier)
            {
                if (_latestSheet is Page.Profession or Page.Skills)
                    _latestSheet = Page.Heritage;
                else if (_latestSheet == Page.Town)
                    _latestSheet = Page.Appearance;
            }
            else
            {
                if (_latestSheet is Page.Profession or Page.Skills)
                    _latestSheet = Page.Appearance;
                else if (_latestSheet == Page.Town)
                    _latestSheet = Page.Summary;
            }
        }
        else
        {
            _professionTab.Visible = true;
            _aptitudesTab.Visible = true;
            _townTab.Visible = true;
        }

        AssignMasterSheetPhase(0x10000025u + (uint)_latestSheet - 1u);
        switch (_latestSheet)
        {
            case Page.Heritage:
                _lineageSheetTrunk.Visible = true;
                _lineageTab.Selected = true;
                break;
            case Page.Profession:
                _professionSheetTrunk.Visible = true;
                _professionTab.Selected = true;
                break;
            case Page.Skills:
                _aptitudesSheetTrunk.Visible = true;
                _aptitudesTab.Selected = true;
                break;
            case Page.Appearance:
                _looksSheetTrunk.Visible = true;
                _looksTab.Selected = true;
                break;
            case Page.Town:
                _townSheetTrunk.Visible = true;
                _townTab.Selected = true;
                break;
            case Page.Summary:
                _summarySheetTrunk.Visible = true;
                _summaryTab.Selected = true;
                _upcoming.Visible = false;
                _complete.Visible = true;
                break;
        }

        _random.Enabled = _latestSheet is not Page.Skills;
        _complete.Enabled = _latestSheet == Page.Summary;

        _previousRev = long.MinValue;
        Tick();
    }

    private void ImposeLineageTabRevert(uint btnElemIdent)
    {
        if (LineageTabUnhideBtnIdents.Contains(btnElemIdent))
        {
            _professionTab.Visible = true;
            _aptitudesTab.Visible = true;
            _townTab.Visible = true;
        }
        else if (LineageTabConcealBtnIdents.Contains(btnElemIdent))
        {
            _professionTab.Visible = false;
            _aptitudesTab.Visible = false;
            _townTab.Visible = false;
        }
    }

    private void AssignMasterSheetPhase(uint phaseIdent)
    {
        if (_masterSheet is IWidgetDatStateful stateful)
            stateful.TrySetCanonPhase(phaseIdent);
    }

    private void TryComplete(bool confirmedUnspentCredits)
    {
        if (_bindings.Finish(confirmedUnspentCredits).Status != SimDirectiveStatus.Rejected)
            return;

        SimToonGenesisLocalRefusal refusal =
            _bindings.View()?.Snapshot.LastLocalRefusal ?? default;
        if (refusal.NoName)
            RevealNoLabelWarningPopup();
        else if (refusal.AttributeCreditsUnspent)
            RevealCreditWarningPopup();
    }

    private void SettlePopups(SimToonGenesisCapture capture)
    {
        var rejection = capture.LastRejection;
        if (rejection is null)
        {
            _previousShownRejection = null;
            return;
        }
        if (_previousShownRejection == rejection)
            return;
        _previousShownRejection = rejection;

        if (_problemMsgPopupCtx is not 0u)
            return;

        string tag = rejection.Value.Code switch
        {
            GenesisVerdict.Opcode.NameInUse => "ID_Character_Err_NameReserved",
            GenesisVerdict.Opcode.NameBanned => "ID_Character_Err_NameBanned",
            GenesisVerdict.Opcode.AdminPrivilegeDenied => "ID_Character_Err_NameAdminDenied",
            _ => "ID_Character_Err_NameDBDown",
        };
        string? msg = _bindings.ResolveText?.Invoke(tag);
        if (msg is null)
            return;

        _problemMsgPopupCtx = _popups.CraftMsg(msg, blob =>
        {
            _problemMsgPopupCtx = 0u;
            _ = blob;
            if (_destroyed || _suppressPopupHooks)
                return;
            _bindings.AcknowledgeRejection?.Invoke();
        });
    }

    private void Disengage()
    {
        if (_engaged)
        {
            _engaged = false;
            _openOnBeginConsumed = false;
            Close();
        }
        ShutAllPopups(suppressHooks: true);
    }
}
