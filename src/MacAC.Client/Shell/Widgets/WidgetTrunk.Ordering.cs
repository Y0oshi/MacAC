using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetTrunk
{
    public void BringToFront(WidgetElem pane)
        => PaneManager.BringToFront(pane);

    public void RestartHintTracking()
    {
        _hoverBegunMsec = InstantMsec;
        _previousPointerRelocateMsec = InstantMsec;   // same fresh idle deadline for the world-hover dwell
        _hintFired = false;
    }

    public void TriggerSignal(int kind, WidgetElem mark, object? cargo = null)
    {
        var e = new WidgetSignal(mark.SignalIdent, mark, kind, Payload: cargo);
        mark.OnSignal(in e);
    }

    public WidgetElem? Pick(int x, int y) => SmackTestTopDown(x, y).element;

    public static (float x, float y, float w, float h) RescaleRect(
        float beginX, float beginY, float beginW, float beginH,
        RescaleRims rims, float dx, float dy, float lowerW, float lowerH, float upperW, float upperH)
    {
        float x = beginX, y = beginY, w = beginW, h = beginH;
        if ((rims & RescaleRims.Right) != 0) w = System.Math.Clamp(beginW + dx, lowerW, upperW);
        if ((rims & RescaleRims.Bottom) != 0) h = System.Math.Clamp(beginH + dy, lowerH, upperH);
        if ((rims & RescaleRims.Left) != 0) { float nw = System.Math.Clamp(beginW - dx, lowerW, upperW); x = beginX + (beginW - nw); w = nw; }
        if ((rims & RescaleRims.Top) != 0) { float nh = System.Math.Clamp(beginH - dy, lowerH, upperH); y = beginY + (beginH - nh); h = nh; }
        return (x, y, w, h);
    }

    internal void AlertPaneMoved(WidgetElem pane)
    {
        if (PaneManager.TryGet(pane, out var hnd))
            WindowMoved?.Invoke(hnd.Name, pane);
    }

    internal void AlertPaneResized(WidgetElem pane)
    {
        if (PaneManager.TryGet(pane, out var hnd))
            WindowResized?.Invoke(hnd.Name, pane);
    }

    // Which edges of element's screen rect the point (x,y) is within grip px of
    internal static RescaleRims SmackRims(WidgetElem element, int x, int y, int grip)
    {
        float l = element.Left, t = element.Top, r = element.Left + element.Width, b = element.Top + element.Height;
        if (x < l - grip || x > r + grip || y < t - grip || y > b + grip) return RescaleRims.None;
        var edges = RescaleRims.None;
        if (System.Math.Abs(x - l) <= grip) edges |= RescaleRims.Left;
        if (System.Math.Abs(x - r) <= grip) edges |= RescaleRims.Right;
        if (System.Math.Abs(y - t) <= grip) edges |= RescaleRims.Top;
        if (System.Math.Abs(y - b) <= grip) edges |= RescaleRims.Bottom;
        if (!element.ResizeX) edges &= ~(RescaleRims.Left | RescaleRims.Right);
        if (!element.ResizeY) edges &= ~(RescaleRims.Top | RescaleRims.Bottom);
        edges &= element.ResizableEdges;
        return edges;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
    }

    private void CommencePull(WidgetElem src)
    {
        object? cargo = src.FetchPullCargo();
        if (cargo is null) { _pullContender = false; return; }
        PullSrc = src;
        PullCargo = cargo;
        _pullGhost = src.FetchPullGhost();
        CommencePullRest(cargo, src);
    }

    private void CommencePullRest(object cargo, WidgetElem src)
    {
        var e = new WidgetSignal(src.SignalIdent, src, WidgetEventType.PullCommence, Payload: cargo);
        src.OnSignal(in e);
        src.AssignPullSrcEngaged(true, cargo);
    }

    private void RefreshPullHover(int x, int y)
    {
        var (element, lx, ly) = SmackTestTopDown(x, y);
        if (ReferenceEquals(element, _previousPullHoverMark)) return;

        // Leave old target
        if (_previousPullHoverMark is not null)
        {
            var eDepart = new WidgetSignal(PullSrc!.SignalIdent, _previousPullHoverMark,
                                     WidgetEventType.PullOver, Data1: x, Data2: y,
                                     Payload: PullCargo);
            _previousPullHoverMark.OnSignal(in eDepart);
        }

        // Enter new target
        if (element is not null)
        {
            var eJoin = new WidgetSignal(PullSrc!.SignalIdent, element, WidgetEventType.PullJoin,
                                     Data1: (int)lx, Data2: (int)ly,
                                     Payload: PullCargo);
            element.OnSignal(in eJoin);
        }
        _previousPullHoverMark = element;
    }

    private void CompletePull(int x, int y)
    {
        WidgetElem? src = PullSrc;
        object? cargo = PullCargo;

        src?.AssignPullSrcEngaged(false, cargo);

        var (element, lx, ly) = SmackTestTopDown(x, y);
        if (element is not null)
        {
            var e = new WidgetSignal(src!.SignalIdent, element, WidgetEventType.DiscardReleased,
                                Data1: (int)lx, Data2: (int)ly, Payload: cargo);
            element.OnSignal(in e);
        }
        else if (cargo is not null)
        {
            DragReleasedOutsideUi?.Invoke(cargo, x, y);
        }
        PullSrc = null;
        PullCargo = null;
        _pullGhost = null;
        _previousPullHoverMark = null;
    }

    private void RefreshHover(int x, int y)
    {
        WidgetElem? element = PopupHit(x, y);
        if (element is null)
            (element, _, _) = SmackTestTopDown(x, y);
        if (ReferenceEquals(element, _hoverWidget))
        {
            if (element?.ReceivesHoverPointerRelocate == true)
                RelayPointerRelocate(element, x, y);
            if (!_hintFired)
                _hoverBegunMsec = InstantMsec;
            return;
        }

        if (_hoverWidget is not null)
        {
            var depart = new WidgetSignal(_hoverWidget.SignalIdent, _hoverWidget, WidgetEventType.HoverDepart);
            _hoverWidget.OnSignal(in depart);
            if (_hintFired)
                TooltipHide?.Invoke(_hoverWidget);
        }
        _hoverWidget = element;
        _hoverBegunMsec = InstantMsec;
        _hintFired = false;
        if (element is not null)
        {
            RefreshHoverBranch(element, y, x);
        }
    }

    private void RefreshHoverBranch(WidgetElem element, int y, int x)
    {
        Vector2 monitor = element.MonitorLocus;
        var enter = new WidgetSignal(
                        element.SignalIdent,
                        element,
                        WidgetEventType.HoverJoin,
                        Data1: (int)(x - monitor.X),
                        Data2: (int)(y - monitor.Y));
        element.OnSignal(in enter);
    }

    private void RefreshBtnBit(WidgetMouseButton button, bool down)
    {
        switch (button)
        {
            case WidgetMouseButton.Left: LeftBtnDown = down; break;
            case WidgetMouseButton.Right: RightBtnDown = down; break;
            case WidgetMouseButton.Middle: MiddleBtnDown = down; break;
        }
    }

    private (WidgetElem? element, float localX, float localY) SmackTestTopDown(int x, int y)
    {
        if (Modal is not null)
        {
            Vector2 mp = Modal.MonitorLocus;
            WidgetElem? element = Modal.HitTest(x - mp.X, y - mp.Y);
            if (element is not null) return (element, x - mp.X, y - mp.Y);
            return (null, 0, 0);
        }

        foreach (var c in DescendantsFrontToBackCapture())
        {
            Vector2 cp = c.MonitorLocus;
            WidgetElem? strike = c.HitTest(x - cp.X, y - cp.Y);
            if (strike is not null)
                return (strike, x - cp.X, y - cp.Y);
        }
        return (null, 0, 0);
    }

    private static WidgetElem? SeekPane(WidgetElem? element)
    {
        while (element is not null)
        {
            if (element.Draggable || element.Resizable) return element;
            element = element.Ancestor;
        }
        return null;
    }

    private WidgetElem? SeekPullHndPane(WidgetElem? element)
    {
        while (element is not null && !ReferenceEquals(element, this) && !element.PaneRelocateHnd)
            element = element.Ancestor;
        if (element is null || ReferenceEquals(element, this)) return null;
        while (element.Ancestor is not null && !ReferenceEquals(element.Ancestor, this))
            element = element.Ancestor;
        return element;
    }

    private static bool ContainsAbsolute(WidgetElem element, int x, int y)
    {
        Vector2 sp = element.MonitorLocus;
        return x >= sp.X && x < sp.X + element.Width
            && y >= sp.Y && y < sp.Y + element.Height;
    }

    private void RelayPointerRelocate(WidgetElem mark, int x, int y)
    {
        Vector2 sp = mark.MonitorLocus;
        var e = new WidgetSignal(mark.SignalIdent, mark, WidgetEventType.PointerRelocate,
                            Data1: (int)(x - sp.X), Data2: (int)(y - sp.Y));
        BubbleSignal(mark, in e);
    }

    private bool BubbleSignal(WidgetElem begin, in WidgetSignal e)
    {
        var element = begin;
        while (element is not null)
        {
            if (element.OnSignal(in e)) return true;
            element = element.Ancestor;
        }
        return false;
    }

    public CanonWindowKeeper PaneManager { get; }

    protected override bool ClipsDescendants => false;

    public Vector2? FixedCanvasDims { get; set; }

    public Vector2 NetCanvasDims
    {
        get
        {
            return FixedCanvasDims is { X: > 0f, Y: > 0f } canvas
            ? canvas
            : new Vector2(Width, Height);
        }
    }

    public Vector2 CanvasScale
    {
        get
        {
            return FixedCanvasDims is { X: > 0f, Y: > 0f } canvas && Width > 0f && Height > 0f
            ? new Vector2(Width / canvas.X, Height / canvas.Y)
            : Vector2.One;
        }
    }

    public int PointerX { get; private set; }

    public int PointerY { get; private set; }

    public long PointerIdleMsec => InstantMsec - _previousPointerRelocateMsec;

    public bool LeftBtnDown { get; private set; }

    public bool RightBtnDown { get; private set; }

    public bool MiddleBtnDown { get; private set; }

    public WidgetElem? KeyboardFocus { get; private set; }

    public WidgetElem? DefaultPhraseFeed { get; set; }

    public WidgetBoard? Modal { get; set; }

    public WidgetElem? Captured { get; private set; }

    public bool WantsMouse
    {
        get
        {
            return Captured is not null
        || PopupHit(PointerX, PointerY) is not null
        || SmackTestTopDown(PointerX, PointerY).element is not null;
        }
    }

    public bool WantsKeyboard => KeyboardFocus is not null;

    public WidgetElem? PullSrc { get; private set; }

    public object? PullCargo { get; private set; }

    // Snapshotted drag-ghost (tex,w,h), exposed for tests
    internal (uint tex, int w, int h)? PullGhostForTest => _pullGhost;

    public bool IsPaneRelocateEngaged => _panePullMark is not null;

    public RescaleRims EngagedRescaleRims => _rescaleMark is not null ? _rescaleRims : RescaleRims.None;

    public RescaleRims HoverRescaleRims
    {
        get
        {
            WidgetElem? mark = Pick(PointerX, PointerY);
            WidgetElem? pane = SeekPane(mark);
            if (WidgetBolted || pane is not { Resizable: true })
                return RescaleRims.None;

            RescaleRims gripRims = NetGripRims(mark, pane);
            if (gripRims != RescaleRims.None)
                return gripRims;

            return SeekPullHndPane(mark) is not null ? RescaleRims.None : SmackRims(pane, PointerX, PointerY, RescaleGrip);
        }
    }

    public bool HoverPaneRelocate
    {
        get
        {
            WidgetElem? mark = Pick(PointerX, PointerY);
            if (WidgetBolted || mark is null)
                return false;
            if (HoverRescaleRims != RescaleRims.None)
                return false;
            if (SeekPullHndPane(mark) is not null)
                return true;
            WidgetElem? pane = SeekPane(mark);
            return pane is not { Draggable: true }
                ? false
                : ReferenceEquals(mark, pane)
                && WithinBorderBand(pane, PointerX, PointerY, RelocateBorderBand);
        }
    }

    public long InstantMsec { get; private set; }
}
