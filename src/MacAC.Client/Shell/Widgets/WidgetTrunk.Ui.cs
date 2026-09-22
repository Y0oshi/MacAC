using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetTrunk
{
    public WidgetTrunk()
    {
        PaneManager = new CanonWindowKeeper(this);
    }

    public void DeclareFixedCanvas(object holder, Vector2 dims)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_fixedCanvasDeclarations.TryGetValue(holder, out Vector2 extant))
        {
            if (extant == dims)
                return; // idempotent re-declare (e.g. a re-ticked activation edge)
            throw new InvalidOperationException(
                $"UiRoot.DeclareFixedCanvas: owner {holder} re-declared a different " +
                $"canvas ({extant} -> {dims}) without revoking first");
        }

        foreach (Vector2 declared in _fixedCanvasDeclarations.Values)
        {
            if (declared != dims)
            {
                throw new InvalidOperationException(
                    $"UiRoot.DeclareFixedCanvas: owner {holder} declared {dims} but " +
                    $"another active owner by now declared {declared} - every " +
                    "concurrently-active fixed-canvas screen must author the SAME " +
                    "canvas size");
            }
        }

        _fixedCanvasDeclarations[holder] = dims;
        FixedCanvasDims = dims;
    }

    public void RevokeFixedCanvas(object holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (!_fixedCanvasDeclarations.Remove(holder))
            return;

        if (_fixedCanvasDeclarations.Count is 0)
        {
            FixedCanvasDims = null;
            return;
        }

        foreach (Vector2 declared in _fixedCanvasDeclarations.Values)
        {
            FixedCanvasDims = declared;
            break;
        }
    }

    public override void AddChild(WidgetElem descendant)
    {
        AssignSignalIdents(descendant);
        base.AddChild(descendant);
    }

    public bool WidgetBolted
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            UiLockChanged?.Invoke(value);
        }
    }

    public void Tick(double dt, long instantMsec)
    {
        InstantMsec = instantMsec;

        if (_hoverWidget is not null && !_hintFired && Captured is null
            && InstantMsec - _hoverBegunMsec >= NetHintDelayMsec(_hoverWidget))
        {
            var e = new WidgetSignal(_hoverWidget.SignalIdent, _hoverWidget, WidgetEventType.Tooltip);
            _hoverWidget.OnSignal(in e);
            _hintFired = true;
            _hintShownMsec = InstantMsec;
            TooltipShow?.Invoke(_hoverWidget);
        }
        else if (_hoverWidget is not null && _hintFired
            && InstantMsec - _hintShownMsec >= HintIntervalMsec)
        {
            var depart = new WidgetSignal(_hoverWidget.SignalIdent, _hoverWidget, WidgetEventType.HoverDepart);
            _hoverWidget.OnSignal(in depart);
            TooltipHide?.Invoke(_hoverWidget);
            _hoverWidget = null;
            _hintFired = false;
        }

        AirGlobalWidgetMoment(this, instantMsec / 1000d);
        PulseSelfAndDescendants(dt);
    }

    public void Draw(WidgetRenderScope cx)
    {
        cx.TextRenderer.CanvasScale = CanvasScale;
        try
        {
            PaintCore(cx);
        }
        finally
        {
            cx.TextRenderer.CanvasScale = Vector2.One;
        }
    }

    public void OnPointerRelocate(int x, int y)
    {
        (x, y) = ChartPaneToCanvas(x, y);
        int dx = x - PointerX;
        int dy = y - PointerY;
        PointerX = x;
        PointerY = y;
        _previousPointerRelocateMsec = InstantMsec;

        // Window resize takes precedence over move / drag-drop / hover
        if (_rescaleMark is not null)
        {
            float upperWidth = _rescaleMark.MaxWidth;
            float upperHeight = _rescaleMark.MaxHeight;
            if (_rescaleMark.ConstrainRescaleToParent
                && _rescaleMark.Ancestor is { } rescaleAncestor)
            {
                upperWidth = MathF.Min(
                    upperWidth,
                    (_rescaleRims & RescaleRims.Left) != 0
                        ? _rescaleBeginX + _rescaleBeginW
                        : rescaleAncestor.Width - _rescaleBeginX);
                upperHeight = MathF.Min(
                    upperHeight,
                    (_rescaleRims & RescaleRims.Top) != 0
                        ? _rescaleBeginY + _rescaleBeginH
                        : rescaleAncestor.Height - _rescaleBeginY);
            }
            var (nx, ny, nw, nh) = RescaleRect(
                _rescaleBeginX, _rescaleBeginY, _rescaleBeginW, _rescaleBeginH,
                _rescaleRims, x - _rescalePointerX, y - _rescalePointerY,
                _rescaleMark.MinWidth, _rescaleMark.MinHeight,
                MathF.Max(_rescaleMark.MinWidth, upperWidth),
                MathF.Max(_rescaleMark.MinHeight, upperHeight));
            _rescaleMark.Left = nx; _rescaleMark.Top = ny;
            _rescaleMark.Width = nw; _rescaleMark.Height = nh;
            _rescaleMark.RestartMooringGrab();
            return;
        }

        if (_panePullMark is not null)
        {
            float left = x - _panePullOffX;
            float top = y - _panePullOffY;
            if (_panePullMark.ConstrainPullToParent
                && _panePullMark.Ancestor is { } ancestor)
            {
                left = Math.Clamp(left, 0f, Math.Max(0f, ancestor.Width - _panePullMark.Width));
                top = Math.Clamp(top, 0f, Math.Max(0f, ancestor.Height - _panePullMark.Height));
            }
            _panePullMark.Left = left;
            _panePullMark.Top = top;
            _panePullMark.RestartMooringGrab();
            return;
        }

        if (Captured is not null)
        {
            RelayPointerRelocate(Captured, x, y);

            if (_pullContender && PullSrc is null)
            {
                if (Math.Abs(x - _pressX) > PullGapThreshold
                    || Math.Abs(y - _pressY) > PullGapThreshold)

                    CommencePull(Captured);
            }
            if (PullSrc is not null)
                RefreshPullHover(x, y);
            return;
        }

        RefreshHover(x, y);
        WorldMouseMoveFallThrough?.Invoke(x, y);
    }

    public void OnPointerDown(WidgetMouseButton btn, int x, int y, uint flagSet = 0)
    {
        (x, y) = ChartPaneToCanvas(x, y);
        PointerX = x; PointerY = y;
        RefreshBtnBit(btn, down: true);
        _pressX = x; _pressY = y;

        if (Modal is not null && !ContainsAbsolute(Modal, x, y))
            return;

        WidgetElem? mark;
        if (_engagedPopup is not null)
        {
            mark = PopupHit(x, y);
            if (mark is null)
            {
                if (_engagedPopup is not null)
                {
                    // Press outside a live popup: dismiss it, swallow the press
                    Action? dismiss = _engagedPopupDismiss;
                    _engagedPopup = null;
                    _engagedPopupDismiss = null;
                    dismiss?.Invoke();
                    return;
                }
                (mark, _, _) = SmackTestTopDown(x, y);
            }
        }
        else
        {
            (mark, _, _) = SmackTestTopDown(x, y);
        }
        if (mark is null)
        {
            if (btn == WidgetMouseButton.Left) AssignKeyboardFocus(null);
            WorldMouseFallThrough?.Invoke(btn, x, y, flagSet);
            return;
        }

        if (btn == WidgetMouseButton.Left)
            AssignKeyboardFocus(mark.AcceptsFocus ? mark : null);

        AssignGrab(mark);

        var pane = SeekPane(mark);
        WidgetElem? hndPane = SeekPullHndPane(mark);
        WidgetElem? emitPane = pane ?? hndPane;
        if (emitPane is not null) BringToFront(emitPane);
        if (btn == WidgetMouseButton.Left && emitPane is not null && !WidgetBolted)
        {
            RescaleRims gripRims = NetGripRims(mark, pane);
            RescaleRims rims = gripRims != RescaleRims.None
                ? gripRims
                : (hndPane is null && pane is { Resizable: true }
                    ? SmackRims(pane, x, y, RescaleGrip)
                    : RescaleRims.None);
            if (rims != RescaleRims.None)
            {
                _rescaleMark = pane;
                _rescaleRims = rims;
                _rescaleBeginX = pane!.Left; _rescaleBeginY = pane.Top;
                _rescaleBeginW = pane.Width; _rescaleBeginH = pane.Height;
                _rescalePointerX = x; _rescalePointerY = y;
                _pullContender = false;
            }
            else if (hndPane is not null)
            {
                _panePullMark = hndPane;
                _panePullOffX = x - (int)hndPane.Left;
                _panePullOffY = y - (int)hndPane.Top;
                _pullContender = false;
            }
            else if (mark.IsPullSrc)
            {
                _pullContender = true;
            }
            else if (mark.CapturesPointerDrag || mark.HndsPress)
            {
                _pullContender = false;
            }
            else if (pane is { Draggable: true })
            {
                _panePullMark = pane;
                _panePullOffX = x - (int)pane.Left;
                _panePullOffY = y - (int)pane.Top;
                _pullContender = false;
            }
            else { _pullContender = true; }
        }
        else _pullContender = mark.CapturesPointerDrag ? false : btn == WidgetMouseButton.Left;

        int rawKind = btn switch
        {
            WidgetMouseButton.Left => WidgetEventType.PointerDown,
            WidgetMouseButton.Right => WidgetEventType.RightDown,
            WidgetMouseButton.Middle => WidgetEventType.MiddleDown,
            _ => WidgetEventType.PointerDown,
        };
        Vector2 sp = mark.MonitorLocus;
        var e = new WidgetSignal(mark.SignalIdent, mark, rawKind,
                            Data0: (int)flagSet, Data1: (int)(x - sp.X), Data2: (int)(y - sp.Y));
        BubbleSignal(mark, in e);
    }

    public void OnPointerUp(WidgetMouseButton btn, int x, int y, uint flagSet = 0)
    {
        (x, y) = ChartPaneToCanvas(x, y);
        PointerX = x; PointerY = y;
        RefreshBtnBit(btn, down: false);

        if (_rescaleMark is not null)
        {
            WidgetElem resizedPane = _rescaleMark;
            _rescaleMark = null;
            FreeGrab();
            AlertPaneResized(resizedPane);
            return;
        }

        if (_panePullMark is not null)
        {
            WidgetElem movedPane = _panePullMark;
            _panePullMark = null;
            FreeGrab();
            AlertPaneMoved(movedPane);
            return;
        }

        if (PullSrc is not null)
        {
            CompletePull(x, y);
            FreeGrab();
            _pullContender = false;
            return;
        }

        if (Captured is { } mark)
        {
            int rawKind = btn switch
            {
                WidgetMouseButton.Left => WidgetEventType.PointerUp,
                WidgetMouseButton.Right => WidgetEventType.RightUp,
                WidgetMouseButton.Middle => WidgetEventType.MiddleUp,
                _ => WidgetEventType.PointerUp,
            };

            Vector2 sp = mark.MonitorLocus;
            var raw = new WidgetSignal(mark.SignalIdent, mark, rawKind,
                                  Data0: (int)flagSet,
                                  Data1: (int)(x - sp.X), Data2: (int)(y - sp.Y));
            BubbleSignal(mark, in raw);

            if (btn == WidgetMouseButton.Left && ContainsAbsolute(mark, x, y))
            {
                long instant = InstantMsec is not 0 ? InstantMsec : Environment.TickCount64;
                bool isDoublePress =
                    ReferenceEquals(mark, _previousPressMark)
                    && instant - _previousPressMsec <= DoublePressDelayMsec
                    && Math.Abs(x - _previousPressX) <= PullGapThreshold
                    && Math.Abs(y - _previousPressY) <= PullGapThreshold;

                var press = new WidgetSignal(mark.SignalIdent, mark, WidgetEventType.Click,
                                        Data0: (int)flagSet,
                                        Data1: (int)(x - sp.X), Data2: (int)(y - sp.Y));
                BubbleSignal(mark, in press);

                if (isDoublePress)
                {
                    var dbl = new WidgetSignal(mark.SignalIdent, mark, WidgetEventType.DoublePress,
                                          Data0: (int)flagSet,
                                          Data1: (int)(x - sp.X), Data2: (int)(y - sp.Y));
                    BubbleSignal(mark, in dbl);
                }
                _previousPressMark = mark;
                _previousPressMsec = instant;
                _previousPressX = x;
                _previousPressY = y;
            }
            else if (btn == WidgetMouseButton.Right
                && ContainsAbsolute(mark, x, y)
                && Math.Abs(x - _pressX) <= PullGapThreshold
                && Math.Abs(y - _pressY) <= PullGapThreshold)
            {
                var press = new WidgetSignal(mark.SignalIdent, mark, WidgetEventType.RightPress,
                                        Data0: (int)flagSet);
                BubbleSignal(mark, in press);
            }

            if (ReferenceEquals(Captured, mark))
                FreeGrab();
            _pullContender = false;
            return;
        }

        WorldMouseFallThrough?.Invoke(btn, x, y, flagSet);
    }

    public void OnRoll(int dy)
    {
        if (PopupHit(PointerX, PointerY) is { } popupMark)
        {
            Vector2 pp = popupMark.MonitorLocus;
            var pe = new WidgetSignal(popupMark.SignalIdent, popupMark, WidgetEventType.Roll,
                                 Data0: dy,
                                 Data1: (int)(PointerX - pp.X), Data2: (int)(PointerY - pp.Y));
            BubbleSignal(popupMark, in pe);
            return;
        }

        var (mark, lx, ly) = SmackTestTopDown(PointerX, PointerY);
        if (mark is null)
        {
            WorldScrollFallThrough?.Invoke(dy);
            return;
        }
        var e = new WidgetSignal(mark.SignalIdent, mark, WidgetEventType.Roll, Data0: dy,
                            Data1: (int)lx, Data2: (int)ly);
        BubbleSignal(mark, in e);
    }

    public void OnTagDown(int vk, uint lparam = 0)
    {
        if (_suppressedPhysicalTag == vk)
            return;

        if (KeyboardFocus is null && DefaultPhraseFeed is not null
            && (vk == (int)Silk.NET.Input.Key.Tab
                || vk == (int)Silk.NET.Input.Key.Enter))
        {
            AssignKeyboardFocus(DefaultPhraseFeed);
            return;
        }

        if (KeyboardFocus is not null)
        {
            var e = new WidgetSignal(KeyboardFocus.SignalIdent, KeyboardFocus, WidgetEventType.TagDown,
                                Data0: vk, Data1: (int)lparam);
            if (BubbleSignal(KeyboardFocus, in e)) return;
        }

        if (KeyboardFocus is null || !KeyboardFocus.IsEditControl)
        {
            WidgetElem trunk = Modal ?? (WidgetElem)this;
            var e = new WidgetSignal(trunk.SignalIdent, trunk, WidgetEventType.TagDown,
                                Data0: vk, Data1: (int)lparam);
            if (BubbleSignal(trunk, in e)) return;
        }

        WorldKeyFallThrough?.Invoke(vk, lparam);
    }

    public void OnTagUp(int vk, uint lparam = 0)
    {
        if (_suppressedPhysicalTag == vk)
        {
            _suppressedPhysicalTag = null;
            return;
        }
        if (KeyboardFocus is not null)
        {
            var e = new WidgetSignal(KeyboardFocus.SignalIdent, KeyboardFocus, WidgetEventType.TagUp,
                                Data0: vk, Data1: (int)lparam);
            if (BubbleSignal(KeyboardFocus, in e)) return;
        }
    }

    public void OnChar(int codepoint)
    {
        if (_suppressedPhysicalTag is not null)
            return;
        if (KeyboardFocus is null || !KeyboardFocus.IsEditControl) return;
        var e = new WidgetSignal(KeyboardFocus.SignalIdent, KeyboardFocus, WidgetEventType.Char,
                            Data0: codepoint);
        BubbleSignal(KeyboardFocus, in e);
    }

    /// <summary>Suppress the raw retained-UI tail of a semantic key action.</summary>
    public void SuppressPhysicalTagUntilFree(Silk.NET.Input.Key tag)
        => _suppressedPhysicalTag = (int)tag;

    public void AssignKeyboardFocus(WidgetElem? element)
    {
        if (KeyboardFocus == element) return;
        WidgetElem? earlier = KeyboardFocus;
        if (earlier is not null)
        {
            var lost = new WidgetSignal(earlier.SignalIdent, earlier, WidgetEventType.FocusLost);
            earlier.OnSignal(in lost);
        }
        KeyboardFocus = element;
        if (element is not null)
        {
            var gained = new WidgetSignal(element.SignalIdent, element, WidgetEventType.FocusGained);
            element.OnSignal(in gained);
        }
        KeyboardFocusChanged?.Invoke(earlier, element);
    }

    public void AssignGrab(WidgetElem element)
    {
        if (ReferenceEquals(Captured, element)) return;
        WidgetElem? earlier = Captured;
        Captured = element;
        AlertGrabLost(earlier);
        PointerCaptureChanged?.Invoke(earlier, element);
    }

    public void FreeGrab()
    {
        WidgetElem? earlier = Captured;
        Captured = null;
        _hoverBegunMsec = InstantMsec;
        _previousPointerRelocateMsec = InstantMsec;
        AlertGrabLost(earlier);
        if (earlier is not null)
            PointerCaptureChanged?.Invoke(earlier, null);
    }

    public CanonWindowHandle ListPane(
        string label,
        WidgetElem pane,
        WidgetElem? substanceTrunk = null,
        IRetainedPaneDriver? driver = null,
        IRetainedWindowStateDriver? phaseDriver = null,
        int authoredGeoRev = 0)
    {
        return PaneManager.Register(
                label,
                pane,
                substanceTrunk,
                driver,
                phaseDriver,
                authoredGeoRev);
    }

    public bool DelistPane(string label) => PaneManager.Unregister(label);

    public bool DisplayPane(string label)
        => PaneManager.Show(label);

    public bool MaskPane(string label)
        => PaneManager.Hide(label);

    public bool ShutWindow(string label) => PaneManager.Close(label);

    public bool IsPaneVisible(string label)
        => PaneManager.IsVisible(label);

    public bool FlipWindow(string label)
        => PaneManager.Toggle(label);

    internal void OnSubtreeRemoving(WidgetElem subtree)
    {
        bool replacingEngagedPullSrc = ReferenceEquals(subtree, PullSrc);
        WipeSubtreeOwnership(subtree, preserveDetachedPull: replacingEngagedPullSrc);
        PaneManager.OnSubtreeRemoving(subtree);
    }

    internal void OnElemVisChanging(WidgetElem elem, bool shown)
    {
        if (shown) return;
        PaneManager.ReadyToConceal(elem);
        WipeSubtreeOwnership(elem);
    }

    internal void OnElemVisAltered(WidgetElem elem, bool shown)
        => ElementVisibilityChanged?.Invoke(elem, shown);

    internal void WipeSubtreeOwnership(WidgetElem subtree, bool preserveDetachedPull = false)
    {
        if (IsWithinSubtree(KeyboardFocus, subtree))
            AssignKeyboardFocus(null);
        if (IsWithinSubtree(Captured, subtree))
        {
            if (preserveDetachedPull && ReferenceEquals(Captured, PullSrc))
                AssignGrab(this);
            else
                FreeGrab();
            _pullContender = false;
        }
        if (IsWithinSubtree(DefaultPhraseFeed, subtree))
            DefaultPhraseFeed = null;
        if (IsWithinSubtree(Modal, subtree))
            Modal = null;
        if (IsWithinSubtree(PullSrc, subtree))
        {
            PullSrc?.AssignPullSrcEngaged(false, PullCargo);
            if (!preserveDetachedPull)
            {
                PullSrc = null;
                PullCargo = null;
                _pullGhost = null;
                _pullContender = false;
            }
        }
        if (IsWithinSubtree(_hoverWidget, subtree))
        {
            var depart = new WidgetSignal(_hoverWidget!.SignalIdent, _hoverWidget, WidgetEventType.HoverDepart);
            _hoverWidget.OnSignal(in depart);
            if (_hintFired)
                TooltipHide?.Invoke(_hoverWidget);
            _hoverWidget = null;
            _hintFired = false;
        }
        if (IsWithinSubtree(_previousPullHoverMark, subtree))
            _previousPullHoverMark = null;
        if (IsWithinSubtree(_previousPressMark, subtree))
            _previousPressMark = null;
        if (IsWithinSubtree(_panePullMark, subtree))
        {
            _panePullMark = null;
            _pullContender = false;
        }
        if (IsWithinSubtree(_rescaleMark, subtree))
        {
            _rescaleMark = null;
            _pullContender = false;
        }
    }

    internal void AssignEngagedPopup(WidgetElem popup, Action dismiss)
    {
        if (!ReferenceEquals(_engagedPopup, popup))
            _engagedPopupDismiss?.Invoke();
        _engagedPopup = popup;
        _engagedPopupDismiss = dismiss;
    }

    internal void WipeEngagedPopup(WidgetElem popup)
    {
        if (!ReferenceEquals(_engagedPopup, popup)) return;
        _engagedPopup = null;
        _engagedPopupDismiss = null;
    }

    private (int x, int y) ChartPaneToCanvas(int x, int y)
    {
        Vector2 scaling = CanvasScale;
        return scaling == Vector2.One
            ? (x, y)
            : ((int)(x / scaling.X), (int)(y / scaling.Y));
    }

    private static RescaleRims NetGripRims(WidgetElem? mark, WidgetElem? pane)
    {
        if (pane is null || mark is not WidgetResizeGrip grip)
            return RescaleRims.None;

        RescaleRims rims = grip.Rims;
        if (!pane.ResizeX) rims &= ~(RescaleRims.Left | RescaleRims.Right);
        if (!pane.ResizeY) rims &= ~(RescaleRims.Top | RescaleRims.Bottom);
        rims &= pane.ResizableEdges;
        return rims;
    }

    private static bool WithinBorderBand(WidgetElem element, int x, int y, int band)
    {
        float l = element.Left, t = element.Top, r = element.Left + element.Width, b = element.Top + element.Height;
        return x < l || x >= r || y < t || y >= b ? false : x - l < band || r - x <= band || y - t < band || b - y <= band;
    }

    private int NetHintDelayMsec(WidgetElem widget)
    {
        return widget.AuthoredHintDelaySecs is { } secs
                ? (int)(secs * 1000f)
                : HintDelayMsec;
    }

    private void AssignSignalIdents(WidgetElem elem)
    {
        if (elem.SignalIdent is 0)
            elem.SignalIdent = _upcomingSignalIdent++;
        foreach (var descendant in elem.Children)
            AssignSignalIdents(descendant);
    }

    private static void AirGlobalWidgetMoment(WidgetElem elem, double instantSecs)
    {
        if (elem is IWidgetGlobalTimeListener listener)
            listener.OnGlobalWidgetMoment(instantSecs);

        foreach (var descendant in elem.DescendantsBackToFrontCapture())
            if (ReferenceEquals(descendant.Ancestor, elem))
                AirGlobalWidgetMoment(descendant, instantSecs);
    }

    private static bool IsWithinSubtree(WidgetElem? elem, WidgetElem subtree)
    {
        while (elem is not null)
        {
            if (ReferenceEquals(elem, subtree)) return true;
            elem = elem.Ancestor;
        }
        return false;
    }

    private void PaintCore(WidgetRenderScope cx)
    {
        PaintSelfAndDescendants(cx);
        cx.CommenceTopLayerStratum();
        PaintTopLayers(cx);
        PaintPullGhost(cx);
        cx.FinishTopLayerStratum();
    }

    private void PaintPullGhost(WidgetRenderScope cx)
    {
        if (_pullGhost is not { } g || g.tex is 0) return;
        cx.SketchSprite(g.tex, PointerX - g.w / 2f, PointerY - g.h / 2f, g.w, g.h,
                       0f, 0f, 1f, 1f, new Vector4(1f, 1f, 1f, GhostAlpha));
    }

    private WidgetElem? PopupHit(int x, int y)
    {
        if (_engagedPopup is not { } popup) return null;
        for (WidgetElem? element = popup; element is not null; element = element.Ancestor)
        {
            if (ReferenceEquals(element, this)) break;
            if (!element.Visible || !element.Enabled || element.Ancestor is null)
            {
                Action? stale = _engagedPopupDismiss;
                _engagedPopup = null;
                _engagedPopupDismiss = null;
                stale?.Invoke();
                return null;
            }
        }
        Vector2 pp = popup.MonitorLocus;
        return popup.HitTest(x - pp.X, y - pp.Y);
    }

    private static void AlertGrabLost(WidgetElem? earlier)
    {
        if (earlier is null) return;
        var lost = new WidgetSignal(
            earlier.SignalIdent, earlier, WidgetEventType.GrabAltered);
        earlier.OnSignal(in lost);
    }
}
