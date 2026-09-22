using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetSolidSpriteFill : WidgetElem
{
    public uint SpriteIdent { get; init; }
    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    public WidgetSolidSpriteFill()
    {
        // A backing field must never intercept pointer events meant for whatever's stacked in front
        // of/behind it (the buttons draw ON TOP via their own later ReadOrder; nothing should route to
        // this leaf at all).
        ClickThrough = true;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        var locate = SpriteResolve;
        if (locate is null || SpriteIdent is 0u) return;
        var (bmp, tw, th) = locate(SpriteIdent);
        if (bmp is 0 || tw is 0 || th is 0) return;
        cx.PushAlphaAbsolute(1f);
        try
        {
            cx.SketchSprite(bmp, 0f, 0f, Width, Height, 0f, 0f, Width / tw, Height / th, Vector4.One);
        }
        finally { cx.TakeAlpha(); }
    }
}
