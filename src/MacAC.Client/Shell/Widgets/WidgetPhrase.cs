using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed partial class WidgetPhrase : WidgetElem, IWidgetDatStateful
{
    public readonly record struct Line(string Text, Vector4 Color);

    public readonly record struct PhraseExec(string Text, Vector4 Color);

    public readonly record struct Spot(int Line, int Col);

    public Func<IReadOnlyList<Line>> StrokesSupplier { get; set; } = static () => Array.Empty<Line>();

    internal static List<(string Text, float X, Vector4 Color)> ArrangementExecutions(
        IReadOnlyList<PhraseExec> executions,
        float beginX,
        Func<string, float> gauge)
    {
        var placed = new List<(string Text, float X, Vector4 Color)>(executions.Count);
        float penX = beginX;
        for (int idx = 0; idx < executions.Count; ++idx)
        {
            PhraseExec exec = executions[idx];
            if (exec.Text.Length is not 0)
                placed.Add((exec.Text, penX, exec.Color));
            penX += gauge(exec.Text);
        }
        return placed;
    }

    public Vector4 DefaultTint { get; set; } = Vector4.One;

    public IReadOnlyList<Vector4> TypefaceTintSwatch { get; set; }
        = Array.Empty<Vector4>();

    public Vector4 BackgroundColor { get; set; } = new(0f, 0f, 0f, 0f);

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public Vector4 PickColor { get; set; } = new(0.25f, 0.45f, 0.85f, 0.5f);

    public ClientVJustify VerticalJustify { get; set; } = ClientVJustify.Center;

    /// <summary>The scroll model - also read by the linked UiScrollbar.</summary>
    public WidgetScrollable Scroll { get; } = new();

    public bool PreserveFinishOnArrangement { get; set; } = true;

    private const float WheelStrokes = 1f;

    private IReadOnlyList<Line> _previousStrokes = Array.Empty<Line>();

    private BitmapFont? _previousTypeface;

    private WidgetDatFont? _previousDatTypeface;

    private float _previousStrokeHeight = 16f;

    private float _previousBaseY;          // top Y of line 0 in local space

    private float _previousPadding;

    private ElemDetails? _datDetails;
    private string _engagedDatPhaseLabel = "";
    private bool _honorDatVerticalJustification;

    private Spot? _selMooring;   // where the drag started

    private Spot? _selCaret;

    private bool _selecting;

    public WidgetPhrase()
    {
        ClickThrough = true;
        AcceptsFocus = false;
        IsEditControl = false;
        CapturesPointerDrag = false;
    }

    private Dictionary<uint, string>? _authoredPhaseTexts;
}
