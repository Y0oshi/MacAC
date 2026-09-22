using System.Numerics;

namespace MacAC.Client.Shell;

[System.Flags]
public enum RescaleRims { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }

public sealed partial class WidgetTrunk : WidgetElem
{
    private readonly Dictionary<object, Vector2> _fixedCanvasDeclarations = [];

    private int? _suppressedPhysicalTag;
    private const int RelocateBorderBand = 8;

    private (uint tex, int w, int h)? _pullGhost;

    private WidgetElem? _previousPullHoverMark;

    private int _pressX, _pressY;

    private bool _pullContender;

    private WidgetElem? _panePullMark;

    private int _panePullOffX, _panePullOffY;

    private WidgetElem? _previousPressMark;

    private long _previousPressMsec;

    private int _previousPressX, _previousPressY;

    private WidgetElem? _rescaleMark;

    private RescaleRims _rescaleRims;

    private float _rescaleBeginX, _rescaleBeginY, _rescaleBeginW, _rescaleBeginH;

    private int _rescalePointerX, _rescalePointerY;

    private const int RescaleGrip = 5;   // px proximity to an edge to start a resize

    private const int PullGapThreshold = 3;

    private const int DoublePressDelayMsec = 500;

    // Hover / tooltip tracking
    private WidgetElem? _hoverWidget;

    private long _hoverBegunMsec;

    public int HintDelayMsec { get; set; } = 250;

    public int HintIntervalMsec { get; set; } = 10_000;

    private bool _hintFired;

    private long _hintShownMsec;

    public event Action<WidgetElem>? TooltipShow;

    public event Action<WidgetElem>? TooltipHide;

    private long _previousPointerRelocateMsec;

    public event Action<WidgetMouseButton, int, int, uint>? WorldMouseFallThrough;

    public event Action<int /*vk*/, uint /*lparam*/>? WorldKeyFallThrough;

    public event Action<int, int>? WorldMouseMoveFallThrough;

    public event Action<int /*dy*/>? WorldScrollFallThrough;

    public event Action<object /*payload*/, int /*x*/, int /*y*/>? DragReleasedOutsideUi;

    public event Action<string, WidgetElem>? WindowMoved;

    public event Action<string, WidgetElem>? WindowResized;

    public event Action<WidgetElem, bool>? ElementVisibilityChanged;

    public event Action<WidgetElem?, WidgetElem?>? KeyboardFocusChanged;

    public event Action<WidgetElem?, WidgetElem?>? PointerCaptureChanged;

    public event Action<bool>? UiLockChanged;

    private uint _upcomingSignalIdent = 0x10000001u;

    private const float GhostAlpha = 1.0f;

    private WidgetElem? _engagedPopup;

    private Action? _engagedPopupDismiss;
}
