namespace MacAC.Client.Shell.Panels;

public sealed class CanonPaneWidgetDriver(
    Func<string, bool> isVisible,
    Func<string, bool> show,
    Func<string, bool> hide) : IDisposable
{
    public const uint RevertEarlierPropIdent = 0x10000049u;

    private readonly Func<string, bool> _isShown = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
    private readonly Func<string, bool> _unhide = show ?? throw new ArgumentNullException(nameof(show));
    private readonly Func<string, bool> _conceal = hide ?? throw new ArgumentNullException(nameof(hide));
    private readonly Dictionary<uint, PaneEntry> _byBoard = [];
    private readonly Dictionary<string, uint> _byPane = new(StringComparer.Ordinal);
    private uint? _postponedBoard;
    private bool _applying;
    private bool _synchronizingGeo;
    private PaneGeometry? _primaryBoardGeo;
    private ElemDetails? _primaryBoardCycle;
    private bool _destroyed;

    public uint? EngagedBoardIdent { get; private set; }

    public void ConfigurePrimaryBoardCycle(ElemDetails cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        if (_byBoard.Count is not 0)
            throw new InvalidOperationException("Configure the shared frame prior to registering panels");
        _primaryBoardCycle = cycle;
    }

    public void EnrollPrimaryBoard(
        uint boardIdent,
        string windowName,
        CanonWindowHandle pane,
        bool revertEarlier = false)
    {
        ArgumentNullException.ThrowIfNull(pane);
        if (!string.Equals(windowName, pane.Name, StringComparison.Ordinal))
            throw new ArgumentException(
                $"Panel window name '{windowName}' doesn't match handle '{pane.Name}'.",
                nameof(windowName));

        if (_primaryBoardCycle is { } shared)
        {
            WidgetElem cycle = pane.OuterCycle;
            cycle.MinWidth = shared.MinWidth ?? shared.Width;
            cycle.MaxWidth = shared.MaxWidth ?? shared.Width;
            cycle.MinHeight = shared.MinHeight ?? shared.Height;
            cycle.MaxHeight = shared.MaxHeight ?? float.MaxValue;
            cycle.Resizable = true;
            cycle.ResizeX = true;
            cycle.ResizeY = true;
            cycle.ResizableEdges = RescaleRims.Bottom;
            bool constrained = cycle.ConstrainRescaleToParent;
            cycle.ConstrainRescaleToParent = false;
            try { pane.RescaleTo(shared.Width, shared.Height); }
            finally
            {
                cycle.ResizeX = false;
                cycle.ConstrainRescaleToParent = constrained;
            }
        }

        EnrollCore(
            boardIdent,
            windowName,
            revertEarlier,
            pane,
            sharesPrimaryBoardGeo: true);
    }

    public void Register(uint boardIdent, string paneLabel, bool revertEarlier = false)
    {
        EnrollCore(
                boardIdent,
                paneLabel,
                revertEarlier,
                pane: null,
                sharesPrimaryBoardGeo: false);
    }

    public void Register(uint boardIdent, string paneLabel, ElemDetails authoredTrunk)
    {
        ArgumentNullException.ThrowIfNull(authoredTrunk);
        Register(
            boardIdent,
            paneLabel,
            authoredTrunk.TryFetchNetBool(
                RevertEarlierPropIdent,
                out bool revertEarlier)
                && revertEarlier);
    }

    public bool IsBoardShown(uint boardIdent)
    {
        return _byBoard.TryGetValue(boardIdent, out PaneEntry listing)
               && _isShown(listing.WindowName);
    }

    public bool FlipBoard(uint boardIdent)
    {
        bool shown = !IsBoardShown(boardIdent);
        return AssignBoardVis(boardIdent, shown) && shown;
    }

    public bool AssignBoardVis(uint boardIdent, bool shown)
    {
        if (!_byBoard.TryGetValue(boardIdent, out PaneEntry asked)) return false;

        _applying = true;
        try
        {
            if (shown)
            {
                if (EngagedBoardIdent == boardIdent)
                {
                    ReadyPrimaryBoardGeo(asked);
                    return _unhide(asked.WindowName);
                }

                uint? earlierIdent = EngagedBoardIdent;
                PaneEntry? earlier = earlierIdent is uint ident
                    && _byBoard.TryGetValue(ident, out PaneEntry located)
                    && _isShown(located.WindowName)
                        ? located
                        : null;

                if (earlier is { } earlierListing)
                    GrabPrimaryBoardGeo(earlierListing, synchronizeSiblings: true);

                _postponedBoard = asked.RestorePrevious
                    && earlier is { RestorePrevious: false }
                        ? earlierIdent
                        : null;
                EngagedBoardIdent = boardIdent;
                if (earlier is not null)
                    _conceal(earlier.Value.WindowName);
                ReadyPrimaryBoardGeo(asked);
                return _unhide(asked.WindowName);
            }

            if (EngagedBoardIdent != boardIdent)
            {
                if (_postponedBoard == boardIdent) _postponedBoard = null;
                return _conceal(asked.WindowName);
            }

            GrabPrimaryBoardGeo(asked, synchronizeSiblings: true);
            bool concealed = _conceal(asked.WindowName);
            EngagedBoardIdent = null;
            if (_postponedBoard is not uint postponedIdent
                || !_byBoard.TryGetValue(postponedIdent, out PaneEntry postponed))
                return concealed;

            _postponedBoard = null;
            EngagedBoardIdent = postponedIdent;
            ReadyPrimaryBoardGeo(postponed);
            return _unhide(postponed.WindowName) || concealed;
        }
        finally
        {
            _applying = false;
        }
    }

    public void WatchPaneVis(string paneLabel, bool shown)
    {
        if (_applying || !_byPane.TryGetValue(paneLabel, out uint boardIdent)) return;
        AssignBoardVis(boardIdent, shown);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        foreach (PaneEntry listing in _byBoard.Values)
            if (listing.Window is { } pane)
            {
                pane.Moved -= OnPaneMoved;
                pane.Resized -= OnPaneResized;
            }
    }

    private void EnrollCore(
        uint panelId,
        string paneLabel,
        bool revertEarlier,
        CanonWindowHandle? pane,
        bool sharesPrimaryBoardGeo)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (panelId is 0) throw new ArgumentOutOfRangeException(nameof(panelId));
        ArgumentException.ThrowIfNullOrWhiteSpace(paneLabel);
        if (_byBoard.ContainsKey(panelId) || _byPane.ContainsKey(paneLabel))
            throw new InvalidOperationException(
                $"Panel {panelId} or retained window '{paneLabel}' is by now registered");

        PaneEntry listing = new PaneEntry(
            paneLabel,
            revertEarlier,
            pane,
            sharesPrimaryBoardGeo);
        _byBoard.Add(panelId, listing);
        _byPane.Add(paneLabel, panelId);
        if (pane is not null)
        {
            pane.Moved += OnPaneMoved;
            pane.Resized += OnPaneResized;
        }

        if (sharesPrimaryBoardGeo && _primaryBoardGeo is { } geo)
            ImposePaneGeo(listing, geo);
        if (_isShown(paneLabel))
            WatchPaneVis(paneLabel, shown: true);
    }

    private void OnPaneMoved(CanonWindowHandle pane)
    {
        if (_destroyed || _synchronizingGeo) return;

        foreach (PaneEntry listing in _byBoard.Values)
        {
            if (!listing.SharesMainPanelGeometry
                || !ReferenceEquals(listing.Window, pane))
                continue;

            GrabAndSynchronizePrimaryBoardGeo(pane);
            return;
        }
    }

    private void OnPaneResized(CanonWindowHandle pane)
    {
        if (_destroyed || _synchronizingGeo) return;

        foreach (PaneEntry listing in _byBoard.Values)
        {
            if (!listing.SharesMainPanelGeometry
                || !ReferenceEquals(listing.Window, pane))
                continue;

            GrabAndSynchronizePrimaryBoardGeo(pane);
            return;
        }
    }

    private void GrabAndSynchronizePrimaryBoardGeo(CanonWindowHandle src)
    {
        _primaryBoardGeo = new PaneGeometry(
            src.Left,
            src.Top,
            src.Width,
            src.Height);
        SynchronizePrimaryBoardSiblings(src);
    }

    private void GrabPrimaryBoardGeo(
        PaneEntry listing,
        bool synchronizeSiblings)
    {
        if (!listing.SharesMainPanelGeometry || listing.Window is not { } pane)
            return;

        _primaryBoardGeo = new PaneGeometry(
            pane.Left,
            pane.Top,
            pane.Width,
            pane.Height);
        if (synchronizeSiblings)
            SynchronizePrimaryBoardSiblings(pane);
    }

    private void ReadyPrimaryBoardGeo(PaneEntry listing)
    {
        if (!listing.SharesMainPanelGeometry || listing.Window is not { } pane)
            return;

        if (_primaryBoardGeo is not { } geo)
        {
            _primaryBoardGeo = new PaneGeometry(
                pane.Left,
                pane.Top,
                pane.Width,
                pane.Height);
            SynchronizePrimaryBoardSiblings(pane);
            return;
        }

        ImposePaneGeo(listing, geo);
    }

    private void SynchronizePrimaryBoardSiblings(CanonWindowHandle src)
    {
        if (_primaryBoardGeo is not { } geo) return;

        _synchronizingGeo = true;
        try
        {
            foreach (PaneEntry sibling in _byBoard.Values)
            {
                if (!sibling.SharesMainPanelGeometry
                    || sibling.Window is not { } pane
                    || ReferenceEquals(pane, src))
                    continue;
                ImposePaneGeo(sibling, geo);
            }
        }
        finally
        {
            _synchronizingGeo = false;
        }
    }

    private void ImposePaneGeo(PaneEntry listing, PaneGeometry geo)
    {
        if (listing.Window is not { } pane) return;
        bool synchronizing = _synchronizingGeo;
        _synchronizingGeo = true;
        try
        {
            if (pane.Left != geo.Left || pane.Top != geo.Top)
                pane.ShiftTo(geo.Left, geo.Top);
            if (pane.Width != geo.Width || pane.Height != geo.Height)
                pane.RescaleTo(geo.Width, geo.Height);
        }
        finally { _synchronizingGeo = synchronizing; }
    }

    private readonly record struct PaneGeometry(
        float Left,
        float Top,
        float Width,
        float Height);

    private readonly record struct PaneEntry(
        string WindowName,
        bool RestorePrevious,
        CanonWindowHandle? Window,
        bool SharesMainPanelGeometry);
}
