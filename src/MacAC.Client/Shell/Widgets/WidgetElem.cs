namespace MacAC.Client.Shell;

[System.Flags]
public enum MooringRims { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

public readonly record struct WidgetCursorMedia(uint File, int HotspotX, int HotspotY)
{
    public bool IsValid => File is not 0;
}

public abstract partial class WidgetElem
{
    private readonly Dictionary<string, WidgetCursorMedia> _phaseCursors = [];

    public float Opacity { get; set; } = 1f;

    public float MinWidth { get; set; } = 40f;

    public float MinHeight { get; set; } = 40f;

    public float MaxWidth { get; set; } = float.MaxValue;

    public float MaxHeight { get; set; } = float.MaxValue;

    public bool ResizeX { get; set; } = true;

    public bool ResizeY { get; set; } = true;

    public RescaleRims ResizableEdges { get; set; } =
        RescaleRims.Left | RescaleRims.Right | RescaleRims.Top | RescaleRims.Bottom;

    private readonly List<WidgetElem> _descendants = [];

    private WidgetElem[]? _descendantsBackToFront;

    private WidgetElem[]? _descendantsFrontToBack;

    private bool _mooringGrabbed;

    private float _amL, _amT, _amR, _amB, _aw0, _ah0;
}
