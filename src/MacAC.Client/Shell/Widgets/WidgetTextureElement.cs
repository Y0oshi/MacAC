using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetTextureElement : WidgetElem
{
    public WidgetTextureElement() => ClickThrough = true;

    public uint Texture { get; set; }
    public Vector4 Tint { get; set; } = Vector4.One;

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (Texture is not 0u)
            cx.SketchSprite(Texture, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Tint);
    }
}
