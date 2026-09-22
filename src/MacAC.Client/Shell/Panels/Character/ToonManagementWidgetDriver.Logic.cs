using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed partial class ToonManagementWidgetDriver
{
    internal WidgetElem Root => _arrangement.Root;

    internal IReadOnlyList<WidgetBtn> Ranks => _ranks;

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
            _join.OnClick = null;
            _erase.OnClick = null;
            _revert.OnClick = null;
            _credits.OnClick = null;
            _quit.OnClick = null;
            foreach (WidgetBtn rank in _ranks)
            {
                rank.OnClick = null;
                rank.OnDoublePress = null;
            }
            _ranks.Clear();
            _rankIdents.Clear();
            _roster.Flush();
            _roster.TemplateResolver = null;
            _hub.DropDescendant(Root);
        }
    }

    internal uint ErasePopupCtx => _erasePopupCtx;

    internal uint OpPauseCtx => _opPauseCtx;

    internal uint JoinPauseCtx => _joinPauseCtx;

    internal void RestartSess()
    {
        if (_destroyed)
            return;
        _exhibitSuppressed = false;
        Disengage();
        _previousRev = long.MinValue;
    }

    internal uint ProblemPopupCtx => _problemPopupCtx;

    internal uint ConfirmQuitPopupCtx => _confirmQuitPopupCtx;

    internal static ToonManagementWidgetDriver? Bind(
        WidgetTrunk hub,
        ImportedArrangement arrangement,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        CanonPromptMint popups,
        ToonPickingEngineWiring mappings,
        PromptStrings texts,
        Action? openCredits = null,
        BitmapFont? verTypeface = null)
    {
        var driver = BuildDetached(
            hub,
            arrangement,
            blueprintLocator,
            popups,
            mappings,
            texts,
            openCredits,
            verTypeface);
        if (driver is null)
            return null;

        try
        {
            driver.FastenAndBeat();
            return driver;
        }
        catch
        {
            driver.Dispose();
            throw;
        }
    }

    internal static ToonManagementWidgetDriver? BuildDetached(
        WidgetTrunk hub,
        ImportedArrangement arrangement,
        Func<uint, uint, WidgetElem?> blueprintLocator,
        CanonPromptMint popups,
        ToonPickingEngineWiring mappings,
        PromptStrings texts,
        Action? openCredits = null,
        BitmapFont? verTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(blueprintLocator);
        ArgumentNullException.ThrowIfNull(popups);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(texts);

        if (ContainsViewRect(arrangement.Root))
        {
            Console.WriteLine(
                "[UI] character management: refusing an unapproved model-preview viewport");
            return null;
        }

        if (arrangement.Root.DatElemIdent != TrunkElemIdent
            || arrangement.SeekElem(RealmPhraseElemIdent) is not WidgetPhrase realmPhrase
            || arrangement.SeekElem(RosterElemIdent) is not WidgetBlueprintRosterBbox roster
            || arrangement.SeekElem(BuildElemIdent) is not WidgetBtn build
            || arrangement.SeekElem(JoinElemIdent) is not WidgetBtn enter
            || arrangement.SeekElem(EraseElemIdent) is not WidgetBtn erase
            || arrangement.SeekElem(RevertElemIdent) is not WidgetBtn revert
            || arrangement.SeekElem(CreditsElemIdent) is not WidgetBtn credits
            || arrangement.SeekElem(QuitElemIdent) is not WidgetBtn quit)
        {
            Console.WriteLine(
                "[UI] character management: the authored root/list/button contract is incomplete");
            return null;
        }

        roster.TemplateResolver = blueprintLocator;
        try
        {
            return new ToonManagementWidgetDriver(
                hub,
                arrangement,
                realmPhrase,
                roster,
                build,
                enter,
                erase,
                revert,
                credits,
                quit,
                popups,
                mappings,
                texts,
                openCredits,
                verTypeface);
        }
        catch
        {
            roster.TemplateResolver = null;
            build.OnClick = null;
            enter.OnClick = null;
            erase.OnClick = null;
            revert.OnClick = null;
            credits.OnClick = null;
            quit.OnClick = null;
            throw;
        }
    }

    internal void FastenAndBeat()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (Root.Ancestor is null)
            _hub.AddChild(Root);
        Tick();
    }

    internal void Tick(bool exhibitBlocked = false)
    {
        if (_destroyed)
            return;

        var lens = _bindings.View();
        SimToonPickCapture capture = lens?.Snapshot ?? default;
        if (_bindings.DirectCharacterLaunch
            && capture.Lifecycle == SimToonPickLifespan.Connecting)
            _straightLaunchQueued = true;
        if (capture.Lifecycle == SimToonPickLifespan.InWorld
            || capture.Error is not null)
            _straightLaunchQueued = false;

        if (_exhibitSuppressed || exhibitBlocked || _straightLaunchQueued)
        {
            Disengage();
            _previousRev = long.MinValue;
            return;
        }

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
            Root.Visible = true;
            _hub.DeclareFixedCanvas(this, _authoredCanvas);
            _hub.BringToFront(Root);
        }

        _previousRealmLabel = capture.WorldName;

        if (_previousGen != capture.Generation
            || _previousRev != capture.Revision)
        {
            if (TryGrabLineup(lens, capture, out SimToonPickEntry[] lineup))
            {
                bool ranksPrimed;
                if (RanksFitLineup(lineup, capture.SlotCount))
                {
                    ImposeHighlight(capture.HighlightedCharacterId);
                    ranksPrimed = true;
                }
                else
                {
                    ranksPrimed = ReassembleRanks(
                        lineup,
                        capture.SlotCount,
                        capture.HighlightedCharacterId);
                }

                if (ranksPrimed)
                {
                    _previousGen = capture.Generation;
                    _previousRev = capture.Revision;
                }
            }
            else
            {
                _previousRev = long.MinValue;
                capture = lens.Snapshot;
                if (!capture.IsActive)
                {
                    Disengage();
                    _previousGen = capture.Generation;
                    _previousRev = capture.Revision;
                    return;
                }
            }
        }
        else
        {
            ImposeHighlight(capture.HighlightedCharacterId);
        }

        ImposeBtns(capture.Buttons);
        SettlePopups(lens, capture);
    }

    internal void AssignExhibitSuppressed(bool suppressed)
    {
        if (_destroyed || _exhibitSuppressed == suppressed)
            return;
        _exhibitSuppressed = suppressed;
        _previousRev = long.MinValue;
        Tick();
    }

    internal static int CalculateRankHeight(
        float rosterHeight,
        int lineupTally,
        int allowedSocketTally)
    {
        int height = (int)MathF.Truncate(rosterHeight);
        int denominator = Math.Max(lineupTally, allowedSocketTally);
        return denominator <= 0 ? height / 10 : Math.Max(height / denominator, height / 10);
    }

    private bool RanksFitLineup(
        IReadOnlyList<SimToonPickEntry> lineup,
        int allowedSocketTally)
    {
        if (_ranks.Count != lineup.Count)
            return false;

        int rankHeight = CalculateRankHeight(
            _roster.Height,
            lineup.Count,
            allowedSocketTally);
        for (int idx = 0; idx < lineup.Count; ++idx)
        {
            WidgetBtn rank = _ranks[idx];
            var toon = lineup[idx];
            if (!_rankIdents.TryGetValue(rank, out uint toonIdent)
                || toonIdent != toon.CharacterId
                || !string.Equals(rank.Label, toon.Name, StringComparison.Ordinal)
                || (int)rank.Height != rankHeight
                || rank.CaptionColor != (toon.IsQueuedErase
                    ? new Vector4(1f, 0f, 0f, 1f)
                    : Vector4.One))

                return false;
        }

        return true;
    }

    private void JoinChosen()
    {
        if (_destroyed)
            return;

        SecureJoinPause();
        var outcome = _bindings.Enter();
        Console.WriteLine(outcome.Accepted
            ? "[UI] character enter accepted"
            : $"[UI] character enter rejected status={outcome.Status}");
        if (!outcome.Accepted)
            ShutCtx(ref _joinPauseCtx, suppressHook: true);
        DirtyAndBeat();
    }

    private static bool ContainsViewRect(WidgetElem elem)
    {
        if (elem is WidgetViewport)
            return true;
        foreach (WidgetElem descendant in elem.Children)
            if (ContainsViewRect(descendant))
                return true;
        return false;
    }

    private static bool TryGrabLineup(
        ISimToonPickLens lens,
        SimToonPickCapture anticipated,
        out SimToonPickEntry[] lineup)
    {
        lineup = new SimToonPickEntry[anticipated.RosterCount];
        for (int idx = 0; idx < lineup.Length; ++idx)
        {
            if (!lens.TryFetchAt(idx, out lineup[idx]))
                return false;
        }

        var following = lens.Snapshot;
        return following.Generation == anticipated.Generation
            && following.Revision == anticipated.Revision
            && following.RosterCount == anticipated.RosterCount;
    }

    private bool ReassembleRanks(
        IReadOnlyList<SimToonPickEntry> lineup,
        int allowedSocketTally,
        uint highlightedToonIdent)
    {
        foreach (WidgetBtn rank in _ranks)
        {
            rank.OnClick = null;
            rank.OnDoublePress = null;
        }
        _ranks.Clear();
        _rankIdents.Clear();
        _roster.Flush();

        int rankHeight = CalculateRankHeight(
            _roster.Height,
            lineup.Count,
            allowedSocketTally);
        _roster.LineHeight = rankHeight;
        bool done = _roster.Templates.Count > 0
            && _roster.TemplateResolver is not null;
        foreach (SimToonPickEntry toon in lineup)
        {
            if (!done)
                break;

            var blueprint = _roster.Templates[0];
            if (_roster.TemplateResolver!(
                    blueprint.TemplateLayoutId,
                    blueprint.TemplateElementId) is not WidgetBtn rank)
            {
                done = false;
                break;
            }

            rank.Height = rankHeight;
            _roster.AppendPrebuiltRank(rank);
            uint toonIdent = toon.CharacterId;
            rank.Label = toon.Name;
            rank.CaptionColor = toon.IsQueuedErase
                ? new Vector4(1f, 0f, 0f, 1f)
                : Vector4.One;
            rank.Enabled = true;
            rank.SuppressSelfFlip = true;
            rank.Selected = toonIdent == highlightedToonIdent;
            rank.OnClick = () => Highlight(toonIdent);
            rank.OnDoublePress = JoinChosen;
            _ranks.Add(rank);
            _rankIdents.Add(rank, toonIdent);
        }

        if (done)
            return true;

        foreach (WidgetBtn rank in _ranks)
        {
            rank.OnClick = null;
            rank.OnDoublePress = null;
        }
        _ranks.Clear();
        _rankIdents.Clear();
        _roster.Flush();
        _previousRev = long.MinValue;
        return false;
    }

    private void ImposeHighlight(uint highlightedToonIdent)
    {
        foreach (WidgetBtn rank in _ranks)
            rank.Selected = _rankIdents.TryGetValue(rank, out uint toonIdent)
                && toonIdent == highlightedToonIdent;
    }

    private void ImposeBtns(SimToonPickButtons btns)
    {
        _build.Visible = true;
        _build.Enabled = btns.CanCreate;
        _join.Enabled = btns.CanEnter;
        _erase.Visible = btns.DeleteVisible;
        _erase.Enabled = btns.CanDelete;
        _revert.Visible = btns.RestoreVisible;
        _revert.Enabled = btns.CanRestore;
    }

    private void Highlight(uint toonIdent)
    {
        if (_destroyed)
            return;
        _bindings.Highlight(toonIdent);
        DirtyAndBeat();
    }

    private void ReqBuild()
    {
        if (_destroyed)
            return;
        _bindings.RequestCreate?.Invoke();
    }

    private void ReqErase()
    {
        if (_destroyed)
            return;
        _bindings.RequestDelete();
        DirtyAndBeat();
    }

    private void ReqQuit()
    {
        if (_destroyed)
            return;

        if (_confirmQuitPopupCtx is not 0u)
            return;

        _confirmQuitPopupCtx = _popups.CraftAck(
            _texts.ConfirmExit,
            blob =>
            {
                _confirmQuitPopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;

                if (blob.FetchBoolean(CanonPromptProperty.AckOutcome))
                    _bindings.RequestExit();
            });
    }

    private void ReinstateChosen()
    {
        if (_destroyed)
            return;

        SecureOpPause();
        SimDirectiveResult outcome = default;
        Exception? miss = null;
        _revertDirectiveInFlight = true;
        try
        {
            outcome = _bindings.Restore();
        }
        catch (Exception problem)
        {
            miss = problem;
        }
        finally
        {
            _revertDirectiveInFlight = false;
        }

        if (miss is not null)
        {
            Console.WriteLine(
                $"[UI] character restore command failed: {miss.Message}");
            ShutCtx(ref _opPauseCtx, suppressHook: true);
            DirtyAndBeat();
            return;
        }

        if (!outcome.Accepted)
            ShutCtx(ref _opPauseCtx, suppressHook: true);
        DirtyAndBeat();
    }

    private void SettlePopups(
        ISimToonPickLens lens,
        SimToonPickCapture capture)
    {
        if (capture.Error is { } problem)
        {
            ShutCtx(ref _erasePopupCtx, suppressHook: true);
            ShutCtx(ref _opPauseCtx, suppressHook: true);
            ShutCtx(ref _joinPauseCtx, suppressHook: true);
            SecureProblem(problem.Message);
            return;
        }

        ShutCtx(ref _problemPopupCtx, suppressHook: true);
        if (capture.Lifecycle == SimToonPickLifespan.EnteringWorld)
        {
            ShutCtx(ref _erasePopupCtx, suppressHook: true);
            ShutCtx(ref _opPauseCtx, suppressHook: true);
            SecureJoinPause();
            return;
        }

        ShutCtx(ref _joinPauseCtx, suppressHook: true);
        if (capture.PendingDeleteCharacterId is not 0u
            && lens.TryGet(capture.PendingDeleteCharacterId, out SimToonPickEntry queued))
        {
            SecureEraseAck(queued.Name);
        }
        else
        {
            ShutCtx(ref _erasePopupCtx, suppressHook: true);
        }

        if (_revertDirectiveInFlight
            || capture.Operation is SimToonPickOperation.DeleteRequested
            or SimToonPickOperation.DeleteAcknowledged
            or SimToonPickOperation.RestoreRequested)
        {
            SecureOpPause();
        }
        else
        {
            ShutCtx(ref _opPauseCtx, suppressHook: true);
        }
    }

    private void SecureEraseAck(string toonLabel)
    {
        if (_erasePopupCtx is not 0u)
            return;

        _erasePopupCtx = _popups.CraftAckPhraseFeed(
            _texts.DeleteConfirmation(toonLabel),
            blob =>
            {
                _erasePopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;

                string response = blob.FetchString(
                    CanonPromptProperty.PhraseFeedOutcome) ?? string.Empty;
                if (string.Equals(
                    response,
                    _texts.DeleteResponse,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _bindings.ConfirmDelete();
                }
                else
                {
                    _bindings.Cancel();
                }
                DirtyAndBeat();
            });
    }

    private void SecureOpPause()
    {
        if (_opPauseCtx is 0u)
            _opPauseCtx = _popups.CraftPause(_texts.PleaseWait);
    }

    private void SecureJoinPause()
    {
        if (_joinPauseCtx is 0u)
            _joinPauseCtx = _popups.CraftPause(_texts.EnteringWorld);
    }

    private void SecureProblem(string msg)
    {
        if (_problemPopupCtx is not 0u)
            return;
        _problemPopupCtx = _popups.CraftMsg(
            msg,
            _ =>
            {
                _problemPopupCtx = 0u;
                if (_destroyed || _suppressPopupHooks)
                    return;
                _bindings.Cancel();
                DirtyAndBeat();
            });
    }

    private void DirtyAndBeat()
    {
        _previousRev = long.MinValue;
        Tick();
    }

    private void Disengage()
    {
        if (_engaged)
        {
            _engaged = false;
            Root.Visible = false;
            _hub.RevokeFixedCanvas(this);
        }
        foreach (WidgetBtn rank in _ranks)
        {
            rank.OnClick = null;
            rank.OnDoublePress = null;
        }
        _ranks.Clear();
        _rankIdents.Clear();
        _roster.Flush();
        ShutAllPopups(suppressHooks: true);
    }

    private void ShutAllPopups(bool suppressHooks)
    {
        bool earlier = _suppressPopupHooks;
        _suppressPopupHooks |= suppressHooks;
        try
        {
            ShutCtx(ref _erasePopupCtx, suppressHook: false);
            ShutCtx(ref _opPauseCtx, suppressHook: false);
            ShutCtx(ref _joinPauseCtx, suppressHook: false);
            ShutCtx(ref _problemPopupCtx, suppressHook: false);
            ShutCtx(ref _confirmQuitPopupCtx, suppressHook: false);
        }
        finally
        {
            _suppressPopupHooks = earlier;
        }
    }

    private void ShutCtx(ref uint ctx, bool suppressHook)
    {
        uint closing = ctx;
        if (closing is 0u)
            return;
        ctx = 0u;

        bool earlier = _suppressPopupHooks;
        _suppressPopupHooks |= suppressHook;
        try
        {
            _popups.ShutPopup(closing);
        }
        finally
        {
            _suppressPopupHooks = earlier;
        }
    }
}
