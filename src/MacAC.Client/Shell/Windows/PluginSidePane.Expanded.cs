using MacAC.Extensibility.Panels;

namespace MacAC.Client.Shell;

public sealed partial class PluginSidePane
{
    internal float ExpandedGripBandHeight
    {
        get
        {
            return _typeface is { } font ? MathF.Max(GripHeight, font.LineHeight + 2f) : GripHeight;
        }
    }

    public int EntryTally => _listings.Count;

    public void Add(
        PanelOwner holder,
        PanelBlueprint descriptor,
        CanonWindowHandle hnd)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(holder.Id);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(hnd);
        if (_listings.ContainsKey(hnd))
            return;

        hnd.OuterCycle.ConstrainPullToParent = true;
        hnd.OuterCycle.ConstrainRescaleToParent = true;
        RetainPaneReachable(hnd);

        ExtensionShelfBtn btn = new ExtensionShelfBtn(
            descriptor,
            holder.DisplayName,
            hnd,
            _locate,
            _typeface)
        {
            Width = BtnReach,
            Height = BtnReach,
        };
        btn.Click += () =>
        {
            if (hnd.IsVisible)
                hnd.Hide();
            else
                hnd.Display();
        };

        ExtensionMinimizeBtn minimize = new ExtensionMinimizeBtn(hnd, _typeface)
        {
            Left = MathF.Max(8f, hnd.OuterCycle.Width - 23f),
            Top = 3f,
            Width = 18f,
            Height = 17f,
            Moorings = MooringRims.Top | MooringRims.Right,
        };
        hnd.OuterCycle.AddChild(minimize);

        _listings.Add(hnd, new ShelfListing(btn, minimize));
        AddChild(btn);
        Reflow();
    }

    public void Show()
    {
        _askedShown = true;
        if (_collapsed)
            _collapsed = false;
        Reflow();
        _hnd?.AlertPhaseAltered();
    }

    public void Hide()
    {
        _askedShown = false;
        Reflow();
        _hnd?.AlertPhaseAltered();
    }

    public RetainedWindowLedger GrabPanePhase() =>
        new(Collapsed: _collapsed, RequestedVisible: _askedShown);

    public void ReinstatePanePhase(RetainedWindowLedger phase)
    {
        _collapsed = phase.Collapsed;
        if (phase.RequestedVisible is { } askedShown)
            _askedShown = askedShown;
        Reflow();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _panes.WindowUnregistered -= OnPaneUnregistered;
        _panes.WindowRegistered -= OnPaneRegistered;
        if (_hnd is { } ownHnd)
            ownHnd.Moved -= OnHndMoved;

        foreach ((CanonWindowHandle hnd, ShelfListing listing) in _listings)
        {
            listing.Button.TeardownSubscriptions();
            if (ReferenceEquals(listing.Minimize.Ancestor, hnd.OuterCycle))
                hnd.OuterCycle.DropDescendant(listing.Minimize);
        }
        _listings.Clear();
        Visible = false;
    }

    protected override void OnBeat(double diffSecs)
    {
        base.OnBeat(diffSecs);

        _grip.Opacity = _panes.IsLocked ? 0.5f : 1f;

        if (Ancestor is { } ancestor)
        {
            float onHandHeight = MathF.Max(
                BtnReach + OuterPadding * 2f,
                ancestor.Height - Top - ExpandedGripBandHeight - OuterPadding);
            if (MathF.Abs(onHandHeight - _previousArrangementHeight) > 0.5f)
            {
                _previousArrangementHeight = onHandHeight;
                Reflow(onHandHeight);
            }

            if (!_startingDockImposed && !_userPositioned && ancestor.Width > 0f)
            {
                float dockLeft = MathF.Min(DefaultLeft, MathF.Max(0f, ancestor.Width - Width));
                float dockTop = Top;
                _dockLeft = dockLeft;
                _dockTop = dockTop;
                _startingDockImposed = true;
                if (_hnd is { } hnd)
                    hnd.ShiftTo(dockLeft, dockTop);
                else
                    Left = dockLeft;
            }
        }

        foreach (CanonWindowHandle hnd in _listings.Keys)
            RetainPaneReachable(hnd);

    }

    private void OnPaneRegistered(CanonWindowHandle hnd)
    {
        if (!ReferenceEquals(hnd.OuterCycle, this)) return;
        _hnd = hnd;
        hnd.Moved += OnHndMoved;
        _panes.WindowRegistered -= OnPaneRegistered;
    }

    private void OnHndMoved(CanonWindowHandle _)
    {
        if (!_startingDockImposed)
        {
            _userPositioned = true;
            return;
        }
        if (Ancestor is { } ancestor)
        {
            float latestDockLeft = MathF.Min(DefaultLeft, MathF.Max(0f, ancestor.Width - Width));
            float reachabilityClampOfPrecedingDock = Math.Clamp(
                _dockLeft, 0f, MathF.Max(0f, ancestor.Width - Width));
            bool stillDocked = Top == _dockTop
                && (Left == latestDockLeft || Left == reachabilityClampOfPrecedingDock);
            if (stillDocked)
            {
                _dockLeft = Left;
                _dockTop = Top;
                return;
            }
        }

        if (Left != _dockLeft || Top != _dockTop)
            _userPositioned = true;
    }

    void IRetainedPaneDriver.OnShown() => _askedShown = true;

    void IRetainedPaneDriver.OnConcealed()
    {
        if (_listings.Count > 0)
            _askedShown = false;
    }

    private void OnPaneUnregistered(CanonWindowHandle hnd)
    {
        if (!_listings.Remove(hnd, out ShelfListing listing))
            return;

        DropDescendant(listing.Button);
        if (ReferenceEquals(listing.Minimize.Ancestor, hnd.OuterCycle))
            hnd.OuterCycle.DropDescendant(listing.Minimize);
        listing.Button.TeardownSubscriptions();
        Reflow();
    }

    private void FlipCollapsed()
    {
        _collapsed = !_collapsed;
        Reflow();
        _hnd?.AlertPhaseAltered();
    }

    private static void RetainPaneReachable(CanonWindowHandle hnd)
    {
        if (hnd.OuterCycle.Ancestor is not { } ancestor
            || ancestor.Width <= 0f
            || ancestor.Height <= 0f)

            return;

        float left = Math.Clamp(
            hnd.Left,
            0f,
            MathF.Max(0f, ancestor.Width - hnd.Width));
        float top = Math.Clamp(
            hnd.Top,
            0f,
            MathF.Max(0f, ancestor.Height - hnd.Height));
        if (left != hnd.Left || top != hnd.Top)
            hnd.ShiftTo(left, top);
    }

    private void ArrangementChrome()
    {
        float bandHeight = _collapsed ? BtnReach : ExpandedGripBandHeight;
        _grip.Left = 0f;
        _grip.Top = 0f;
        _grip.Width = MathF.Max(0f, Width - FlipWidth);
        _grip.Height = bandHeight;

        _flip.Left = Width - FlipWidth;
        _flip.Top = 0f;
        _flip.Width = FlipWidth;
        _flip.Height = bandHeight;
    }

    private void Reflow(float? ceilingHeight = null)
    {
        float netHeight = ceilingHeight
            ?? (_previousArrangementHeight >= 0f ? _previousArrangementHeight : float.PositiveInfinity);

        int ceilingRanks = float.IsPositiveInfinity(netHeight)
            ? Math.Max(1, _listings.Count)
            : Math.Max(
                1,
                (int)MathF.Floor(
                    (netHeight - OuterPadding * 2f + BtnGap)
                    / (BtnReach + BtnGap)));
        int ordinal = 0;
        foreach (ShelfListing listing in _listings.Values)
        {
            int column = ordinal / ceilingRanks;
            int rank = ordinal % ceilingRanks;
            listing.Button.Left = OuterPadding
                + column * (BtnReach + BtnGap);
            listing.Button.Top = ExpandedGripBandHeight + OuterPadding
                + rank * (BtnReach + BtnGap);
            listing.Button.Visible = !_collapsed;
            ++ordinal;
        }

        int ranks = Math.Min(ordinal, ceilingRanks);
        int columns = ordinal is 0 ? 1 : (ordinal + ceilingRanks - 1) / ceilingRanks;

        if (_collapsed)
        {
            Width = CollapsedWidth;
            Height = BtnReach;
        }
        else
        {
            Width = OuterPadding * 2f
                + columns * BtnReach
                + Math.Max(0, columns - 1) * BtnGap;
            Height = ExpandedGripBandHeight
                + OuterPadding * 2f
                + ranks * BtnReach
                + Math.Max(0, ranks - 1) * BtnGap;
        }

        ArrangementChrome();
        ImposeVis();

        if (_startingDockImposed && !_userPositioned)
        {
            _dockLeft = Left;
            _dockTop = Top;
        }
    }

    private void ImposeVis() => Visible = _askedShown && _listings.Count > 0;
}
