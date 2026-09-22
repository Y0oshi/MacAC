using MacAC.Client.Graphics;
using MacAC.Cockpit.Input;
using MacAC.Cockpit.Panels.Chat;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public sealed partial class CommsPaneDriver
{
    public CanonWindowHandle? PaneHnd { get; private set; }

    public bool IsMaximized { get; private set; }

    internal WidgetElem? UnreadIndicatorForTest { get; private set; }

    public void FastenPane(CanonWindowHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (!ReferenceEquals(handle.SubstanceTrunk, Root))
            throw new ArgumentException("Chat handle content root doesn't match the bound layout", nameof(handle));
        if (PaneHnd is not null && !ReferenceEquals(PaneHnd, handle))
            throw new InvalidOperationException("Chat controller is by now attached to another window");
        PaneHnd = handle;
    }

    public void AssignIndicatorOpen(int windowId, bool open)
    {
        if (windowId < 1 || windowId > _indicatorBtns.Length)
            throw new ArgumentOutOfRangeException(nameof(windowId));
        _indicatorBtns[windowId - 1]?.TrySetCanonPhase(
            open ? WidgetButtonStateMachine.Highlight : WidgetButtonStateMachine.Normal);
    }

    public void AttachIndicatorPresses(Func<int, bool> flipFloatingPane)
    {
        ArgumentNullException.ThrowIfNull(flipFloatingPane);
        for (int idx = 0; idx < _indicatorBtns.Length; ++idx)
        {
            int paneIdent = idx + 1;
            if (_indicatorBtns[idx] is { } indicator)
                indicator.OnClick = () => flipFloatingPane(paneIdent);
        }
    }

    public RetainedWindowLedger GrabPanePhase()
    {
        return new(
                Maximized: IsMaximized,
                PersistedTop: IsMaximized ? _normTop : null,
                PersistedHeight: IsMaximized ? _normHeight : null);
    }

    public void ReinstatePanePhase(RetainedWindowLedger phase)
    {
        if ((phase.PersistedTop.HasValue || phase.PersistedHeight.HasValue)
            && PaneHnd is { IsRegistered: true } hnd)
        {
            IsMaximized = false;
            hnd.RescaleTo(hnd.Width, phase.PersistedHeight ?? hnd.Height);
            hnd.ShiftTo(hnd.Left, phase.PersistedTop ?? hnd.Top);
            _normTop = hnd.Top;
            _normHeight = hnd.Height;
        }

        if (phase.Maximized != IsMaximized)
            FlipMaximize();
        else
            _upperLowerBtn?.TrySetCanonPhase(
                IsMaximized ? CanonWidgetStateIds.Maximized : CanonWidgetStateIds.Minimized);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
    }

    internal void AssignUnreadForTest(bool unread) => _hasUnseenPhrase = unread;

    internal bool TryBeginTellFromTag(WidgetPhrase.Spot locus)
    {
        if (locus.Line < 0 || locus.Line >= _stashedTranscriptTags.Count)
            return false;
        if (_stashedTranscriptTags[locus.Line] is not { } spans)
            return false;

        foreach ((int begin, int len, TextTag tag) in spans)
        {
            if (locus.Col < begin || locus.Col >= begin + len)
                continue;
            if (!tag.TryFetchIidString(out _, out string label) || label.Length is 0)
                continue;

            BeginTell(label);
            return true;
        }

        return false;
    }

    internal void RollToNewestAndWipeUnread()
    {
        Transcript.Scroll.RollToFinish();
        _hasUnseenPhrase = false;
    }

    internal void RefreshUnreadIndicator()
    {
        if (Transcript.Scroll.AtFinish)
            _hasUnseenPhrase = false;
        AssignUnreadIndicatorPhase(_hasUnseenPhrase);
    }

    internal void BeginTell(string label)
    {
        Input.AssignPhrase($"@tell {label}, ");
        SeekTrunkOf(Input)?.AssignKeyboardFocus(Input);
    }

    internal void JoinCommsManner(KeyStroke? physicalChord = null)
    {
        WidgetTrunk? trunk = SeekTrunkOf(Input);
        trunk?.AssignKeyboardFocus(Input);
        if (physicalChord is { Device: 0 } chord)
            trunk?.SuppressPhysicalTagUntilFree(chord.Key);
        Input.PickAllPhrase();
    }

    internal void ToggleChatEntry(KeyStroke? physicalChord = null)
    {
        WidgetTrunk? trunk = SeekTrunkOf(Input);
        if (trunk is null)
            return;
        trunk.AssignKeyboardFocus(ReferenceEquals(trunk.KeyboardFocus, Input) ? null : Input);
        if (physicalChord is { Device: 0 } chord)
            trunk.SuppressPhysicalTagUntilFree(chord.Key);
    }

    internal void BeginDirective()
    {
        Input.AssignPhrase("/");
        SeekTrunkOf(Input)?.AssignKeyboardFocus(Input);
    }

    internal void BeginReply(string? label)
    {
        if (!string.IsNullOrEmpty(label))
            BeginTell(label);
    }

    private string S(string tag, string authoredBackup)
        => _commsTexts?.Invoke(tag) ?? authoredBackup;

    private string LaneBtnCaption(CommsChannelKind kdx)
    {
        return kdx switch
        {
            CommsChannelKind.Say => S("ID_Chat_ChatTargetMenu", "Chat"),
            CommsChannelKind.Tell => S("ID_Chat_ChatTargetMenuSelected", "Tell"),
            CommsChannelKind.General => S("ID_Chat_ChatTargetMenuGeneral", "Gen"),
            CommsChannelKind.Trade => S("ID_Chat_ChatTargetMenuTrade", "Trade"),
            CommsChannelKind.Lfg => S("ID_Chat_ChatTargetMenuLFG", "LFG"),
            CommsChannelKind.Fellowship => S("ID_Chat_ChatTargetMenuFellows", "Fell"),
            CommsChannelKind.Allegiance => S("ID_Chat_ChatTargetMenuAllegiance", "Alg"),
            CommsChannelKind.Patron => S("ID_Chat_ChatTargetMenuPatron", "Pat"),
            CommsChannelKind.Vassals => S("ID_Chat_ChatTargetMenuVassals", "Vas"),
            CommsChannelKind.Monarch => S("ID_Chat_ChatTargetMenuMonarch", "Mon"),
            CommsChannelKind.Roleplay => S("ID_Chat_ChatTargetMenuRoleplay", "RP"),
            CommsChannelKind.Society => S("ID_Chat_ChatTargetMenuSociety", "Soc"),
            CommsChannelKind.Olthoi => S("ID_Chat_ChatTargetMenuOlthoi", "Olt"),
            _ => S("ID_Chat_ChatTargetMenu", "Chat"),
        };
    }

    private static bool LaneOnHand(CommsChannelKind kdx)
    {
        return kdx is CommsChannelKind.Say or CommsChannelKind.General or CommsChannelKind.Trade or CommsChannelKind.Lfg;
    }

    private void FlipMaximize()
    {
        if (PaneHnd is not { IsRegistered: true } hnd)
            return;

        WidgetElem cycle = hnd.OuterCycle;
        float ancestorHeight = cycle.Ancestor?.Height ?? 0f;
        if (ancestorHeight <= 0f)
            return;

        if (IsMaximized)
        {
            float restoredHeight = Math.Clamp(_normHeight, cycle.MinHeight, cycle.MaxHeight);
            float restoredTop = Math.Clamp(
                _normTop,
                0f,
                MathF.Max(0f, ancestorHeight - cycle.MinHeight));
            IsMaximized = false;
            _upperLowerBtn?.TrySetCanonPhase(CanonWidgetStateIds.Minimized);
            hnd.RescaleTo(cycle.Width, restoredHeight);
            hnd.ShiftTo(cycle.Left, restoredTop);
            return;
        }

        _normTop = cycle.Top;
        _normHeight = cycle.Height;

        float expansion = ancestorHeight / 2f;
        float markHeight = Math.Clamp(
            MathF.Min(cycle.Height + expansion, ancestorHeight),
            cycle.MinHeight,
            cycle.MaxHeight);
        bool expandUp = cycle.Top + markHeight > ancestorHeight
                      || cycle.Top >= ancestorHeight / 2f;
        float markTop = expandUp
            ? MathF.Max(0f, cycle.Top - (markHeight - cycle.Height))
            : cycle.Top;
        markHeight = MathF.Min(markHeight, ancestorHeight - markTop);

        IsMaximized = true;
        _upperLowerBtn?.TrySetCanonPhase(CanonWidgetStateIds.Maximized);
        hnd.RescaleTo(cycle.Width, markHeight);
        hnd.ShiftTo(cycle.Left, markTop);
    }

    private static ElemDetails? SeekDetails(ElemDetails joint, uint ident)
    {
        if (joint.Id == ident) return joint;
        foreach (ElemDetails descendant in joint.Children)
        {
            var located = SeekDetails(descendant, ident);
            if (located is not null) return located;
        }
        return null;
    }

    private IReadOnlyList<WidgetPhrase.Line> FetchTranscriptStrokes(ChatModel model)
    {
        float upperW = Transcript.Width - 2f * Transcript.Padding;
        var datTypeface = Transcript.DatFont;
        BitmapFont? diagTypeface = Transcript.Font;
        long rev = model.Rev;
        ulong sift = _paneFilters.FetchSift(ChatPaneState.PrimaryPaneIdent);

        if (_stashedTranscriptRev == rev
            && _stashedSift == sift
            && _stashedTranscriptEncloseWidth.Equals(upperW)
            && ReferenceEquals(_stashedTranscriptDatTypeface, datTypeface)
            && ReferenceEquals(_stashedTranscriptDiagTypeface, diagTypeface))

            return _stashedTranscriptStrokes;

        if (_stashedTranscriptRev != rev
            && _stashedTranscriptRev >= 0
            && !Transcript.Scroll.AtFinish)

            _hasUnseenPhrase = true;

        var detailed = model.RecentStrokesDetailed();
        if (detailed.Count is 0)
        {
            return VaultTranscriptArrangement(
                Array.Empty<WidgetPhrase.Line>(), rev, sift, upperW, datTypeface, diagTypeface);
        }

        Func<string, float> gauge =
              datTypeface is { } font ? font.MeasureWidth
            : diagTypeface is { } bf ? bf.MeasureWidth
            : static s => s.Length * 7f;

        bool Admit(uint tracePhraseKind) => _paneFilters.ShouldReadout(
            ChatPaneState.PrimaryPaneIdent, ChatPaneState.AirMarkPane, tracePhraseKind);
        _stashedTranscriptExecutions.Clear();
        _stashedTranscriptTags.Clear();
        List<WidgetPhrase.Line> outcome = CommsTranscriptPainter.AssembleStrokes(
            detailed,
            upperW,
            gauge,
            Admit,
            Transcript.DefaultTint,
            Transcript.TagTint,
            _stashedTranscriptExecutions,
            _stashedTranscriptTags);
        return VaultTranscriptArrangement(outcome, rev, sift, upperW, datTypeface, diagTypeface);
    }

    private void AssignUnreadIndicatorPhase(bool unread)
    {
        if (UnreadIndicatorForTest is null)
            return;

        if (!unread)
        {
            UnreadIndicatorForTest.Visible = false;
            _flashBegun = false;
            return;
        }

        if (!_flashBegun)
        {
            _flashBegun = true;
            UnreadIndicatorForTest.Visible = true;
            if (UnreadIndicatorForTest is IWidgetDatStateful starting)
                starting.TrySetCanonPhase(WidgetButtonStateMachine.Normal);
            return;
        }

        if (UnreadIndicatorForTest is WidgetBtn flashing
            && string.Equals(flashing.EngagedCondition, "Ghosted", StringComparison.Ordinal))

            UnreadIndicatorForTest.Visible = false;

    }

    private static WidgetTrunk? SeekTrunkOf(WidgetElem elem)
    {
        for (WidgetElem? at = elem; at is not null; at = at.Ancestor)
            if (at is WidgetTrunk trunk)
                return trunk;
        return null;
    }

    private IReadOnlyList<WidgetPhrase.Line> VaultTranscriptArrangement(
        IReadOnlyList<WidgetPhrase.Line> strokes,
        long rev,
        ulong sift,
        float encloseWidth,
        WidgetDatFont? datTypeface,
        BitmapFont? diagTypeface)
    {
        _stashedTranscriptRev = rev;
        _stashedSift = sift;
        _stashedTranscriptEncloseWidth = encloseWidth;
        _stashedTranscriptDatTypeface = datTypeface;
        _stashedTranscriptDiagTypeface = diagTypeface;
        _stashedTranscriptStrokes = strokes;
        ++TranscriptArrangementAssembleTally;
        return strokes;
    }

    internal int TranscriptArrangementAssembleTally { get; private set; }
}
