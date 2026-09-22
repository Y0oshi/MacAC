using System.Numerics;

namespace MacAC.Client.Shell;

public sealed partial class WidgetMenu : WidgetElem
{
    public readonly record struct MenuGear(string Label, object? Payload);

    public IReadOnlyList<MenuGear> Items { get; set; } = System.Array.Empty<MenuGear>();

    public int RowsPerColumn { get; set; } = 7;

    public float RowHeight { get; set; } = 17f;

    public float ColumnWidth { get; set; } = 191f;  // dat item template W=191

    public WidgetScrollable PopupRoll { get; } = new();

    public float ScrollbarWidth { get; set; } = 16f;

    public float RollButtonExtent { get; set; } = 16f;

    private bool _draggingPopupThumb;

    private float _popupThumbPullShift;
    private const int Border = CanonChromeSprites.Border; // 8-piece bevel thickness (5px)

    public float PhraseIndent { get; set; } = 19f;

    public float BtnPhraseIndent { get; set; } = 20f;

    public float ArrowCapWidth { get; set; } = 17f;

    public float ArrowCapHeight { get; set; } = 19f;

    public Vector4 OutlineColor { get; set; } = WidgetRenderScope.DefaultOutlineTint;

    public Vector4 WordingTint { get; set; } = new(1f, 0.92f, 0.72f, 1f);

    public Vector4 PhraseTintOnHand { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector4 PhraseTintGhosted { get; set; } = new(0.5f, 0.5f, 0.5f, 1f);

    public bool CanonBtnArt { get; set; } = true;

    public Vector4 PlainBackgroundTint { get; set; } = new(0f, 0f, 0f, 0.92f);

    public Vector4 PlainBorderTint { get; set; } = new(0.46f, 0.37f, 0.16f, 1f);

    public Vector4 PlainOpenBorderTint { get; set; } = new(0.70f, 0.58f, 0.24f, 1f);

    public Vector4 PlainPhraseTint { get; set; } = new(0.91f, 0.87f, 0.76f, 1f);

    public Vector4 PlainTriangleTint { get; set; } = new(0.91f, 0.87f, 0.76f, 1f);

    public const float PlainPadding = 3f;

    public Vector4 PlainSelectedColor { get; set; } = new(0.28f, 0.23f, 0.08f, 0.95f);

    public Vector4 PlainHoverColor { get; set; } = new(0.40f, 0.33f, 0.14f, 0.95f);

    private bool _facePressed;

    public bool OpenUpward { get; set; } = true;

    public WidgetMenu() { CapturesPointerDrag = true; }

    private const float FaceCapL = 20f, FaceCapR = 12f;
}
