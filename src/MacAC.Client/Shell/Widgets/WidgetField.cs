using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetField : WidgetElem
{
    private readonly record struct WrappedStroke(int Start, int Length, string Text);

    public Vector4 PhraseTint { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public Vector4 BackgroundTint { get; set; } = new(0f, 0f, 0f, 0f);

    public Vector4 PickTint { get; set; } = new(0.25f, 0.45f, 0.85f, 0.5f);

    public float Padding { get; set; } = 4f;

    public int UpperToons { get; set; } = 0xFFFF;

    public bool OneStroke { get; set; } = true;

    public bool WipeOnSubmit { get; set; } = true;

    public bool CaptureHistory { get; set; } = true;

    public WidgetScrollable Scroll { get; } = new();

    public float FocusRailLeftWidth { get; set; } = 1f;

    public float FocusRailRightWidth { get; set; } = 1f;

    private int? _selMooring;   // selection fixed end (null = no selection); span = [min,max] with _caret

    private readonly List<string> _history = [];

    private int _historyOrdinal = -1;
    private bool _selecting;   // mouse drag in progress

    private bool _preserveFocusPickOnPointerDown;

    private float _rollX;

    private IReadOnlyList<WrappedStroke> _wrappedStrokes = Array.Empty<WrappedStroke>();

    private float _wrappedStrokeHeight = 14f;

    private int _phraseVer;

    private int _wrappedVer = -1;

    private float _wrappedWidth;

    private bool _suppressUpcomingNewlineChar;

    private Silk.NET.Input.Key? _repeatTag;

    private double _repeatTicker;

    private const double RepeatDelay = 0.40;   // s before the first repeat

    private const double RepeatRate = 0.04;   // s between repeats (~25/s)
}
