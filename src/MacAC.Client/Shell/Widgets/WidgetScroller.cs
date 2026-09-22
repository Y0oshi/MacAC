using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetScroller : WidgetElem
{
    public bool CanonArt { get; set; } = true;

    public Vector4 PlainFollowTint { get; set; } = new(0f, 0f, 0f, 0.6f);

    public Vector4 PlainBorderColor { get; set; } = new(0.46f, 0.37f, 0.16f, 1f);

    public Vector4 PlainNubTint { get; set; } = new(0.72f, 0.62f, 0.34f, 1f);

    public Func<float?> ScalarPopulate { get; set; } = () => null;

    public float ScalarSpanWidth { get; set; } = float.PositiveInfinity;

    public float DecrementBtnReach { get; set; } = 16f;

    public float IncrementBtnReach { get; set; } = 16f;

    private const float LowerThumb = 8f;

    private const float CapH = 3f;
    private bool _hoveredThumb;

    private float _pullShiftY;

    private float _pullShiftX;

    private FinishBtn _hoveredBtn;

    private FinishBtn _pressedBtn;

    private enum FinishBtn
    {
        None,
        Decrement,
        Increment,
    }

    public WidgetScroller() { CapturesPointerDrag = true; }
}
