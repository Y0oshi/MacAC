using System.Numerics;

namespace MacAC.Client.Shell;

public class WidgetNineSlicePane : WidgetBoard
{
    public readonly record struct ClientRect(float X, float Y, float W, float H);

    public readonly record struct CycleRects(
        ClientRect Center, ClientRect Top, ClientRect Bottom, ClientRect Left, ClientRect Right,
        ClientRect TL, ClientRect TR, ClientRect BL, ClientRect BR);

    private readonly System.Func<uint, (uint tex, int w, int h)> _locate;

    public bool PaintMiddlePopulate { get; set; } = true;

    public bool PaintRescaleAffordances { get; set; } = true;

    public WidgetNineSlicePane(System.Func<uint, (uint, int, int)> locate)
    {
        _locate = locate;
        BackgroundColor = Vector4.Zero; // suppress the base flat-rect fill
        BorderTint = Vector4.Zero;
        Draggable = true;
        Resizable = true;
        Moorings = MooringRims.None;
    }

    public static CycleRects CalculateCycleRects(float w, float h, int b)
    {
        float interiorW = w - 2 * b;
        float interiorH = h - 2 * b;
        return new CycleRects(
            Center: new ClientRect(b, b, interiorW, interiorH),
            Top: new ClientRect(b, 0, interiorW, b),
            Bottom: new ClientRect(b, h - b, interiorW, b),
            Left: new ClientRect(0, b, b, interiorH),
            Right: new ClientRect(w - b, b, b, interiorH),
            TL: new ClientRect(0, 0, b, b),
            TR: new ClientRect(w - b, 0, b, b),
            BL: new ClientRect(0, h - b, b, b),
            BR: new ClientRect(w - b, h - b, b, b));
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        CycleRects rects = CalculateCycleRects(Width, Height, CanonChromeSprites.Border);
        if (PaintMiddlePopulate)
            PaintTiled(cx, CanonChromeSprites.MiddlePopulate, rects.Center);
    }

    protected override void OnPaintFollowingDescendants(WidgetRenderScope cx)
    {
        CycleRects rects = CalculateCycleRects(Width, Height, CanonChromeSprites.Border);
        PaintTiled(cx, CanonChromeSprites.TopRim, rects.Top);
        PaintTiled(cx, CanonChromeSprites.BottomRim, rects.Bottom);
        PaintTiled(cx, CanonChromeSprites.LeftRim, rects.Left);
        PaintTiled(cx, CanonChromeSprites.RightRim, rects.Right);
        PaintStretched(cx, CanonChromeSprites.CornerTL, rects.TL);
        PaintStretched(cx, CanonChromeSprites.CornerTR, rects.TR);
        PaintStretched(cx, CanonChromeSprites.CornerBL, rects.BL);
        PaintStretched(cx, CanonChromeSprites.CornerBR, rects.BR);

        if (!PaintRescaleAffordances)
            return;

        PaintTiled(cx, CanonChromeSprites.GripTop, rects.Top);
        PaintTiled(cx, CanonChromeSprites.GripBottom, rects.Bottom);
        PaintTiled(cx, CanonChromeSprites.GripLeft, rects.Left);
        PaintTiled(cx, CanonChromeSprites.GripRight, rects.Right);
        PaintStretched(cx, CanonChromeSprites.GripCorner, rects.TL);
        PaintStretched(cx, CanonChromeSprites.GripCorner, rects.TR);
        PaintStretched(cx, CanonChromeSprites.GripCorner, rects.BL);
        PaintStretched(cx, CanonChromeSprites.GripCorner, rects.BR);
    }

    private void PaintTiled(WidgetRenderScope cx, uint ident, ClientRect rect)
    {
        if (rect.W <= 0 || rect.H <= 0) return;
        var (bmp, tw, th) = _locate(ident);
        if (bmp is 0 || tw is 0 || th is 0) return;
        cx.SketchSprite(bmp, rect.X, rect.Y, rect.W, rect.H, 0, 0, rect.W / tw, rect.H / th, Vector4.One);
    }

    private void PaintStretched(WidgetRenderScope cx, uint ident, ClientRect rect)
    {
        if (rect.W <= 0 || rect.H <= 0) return;
        var (bmp, _, _) = _locate(ident);
        if (bmp is 0) return;
        cx.SketchSprite(bmp, rect.X, rect.Y, rect.W, rect.H, 0, 0, 1, 1, Vector4.One);
    }
}
