using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetMarkupIcon : WidgetElem
{
    public Func<(uint tex, int w, int h)> GlyphSource { get; set; } =
        static () => (0u, 0, 0);

    public WidgetMarkupIcon() => ClickThrough = true;

    protected override void OnPaint(WidgetRenderScope cx)
    {
        (uint bmp, int w, int h) = GlyphSource();
        if (bmp is 0u || w <= 0 || h <= 0 || Width <= 0f || Height <= 0f)
            return;

        float scaling = MathF.Min(Width / w, Height / h);
        float paintWidth = w * scaling;
        float paintHeight = h * scaling;
        float x = (Width - paintWidth) * 0.5f;
        float y = (Height - paintHeight) * 0.5f;
        cx.SketchSprite(bmp, x, y, paintWidth, paintHeight, 0f, 0f, 1f, 1f, Vector4.One);
    }
}
