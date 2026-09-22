using System.Numerics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed class WidgetResizeGrip : WidgetElem
{
    private readonly ElemDetails? _details;
    private readonly Func<uint, (uint tex, int w, int h)>? _locate;

    public WidgetResizeGrip()
    {
    }

    public WidgetResizeGrip(ElemDetails details, Func<uint, (uint tex, int w, int h)> locate)
    {
        _details = details;
        _locate = locate;
    }

    public uint SpriteFile
    {
        get
        {
            return _details is not null && _details.StateMedia.TryGetValue("", out var media)
        ? media.File
        : 0u;
        }
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (_locate is null) return;
        uint file = SpriteFile;
        if (file is 0u) return;
        var (bmp, tw, th) = _locate(file);
        if (bmp is 0 || tw is 0 || th is 0) return;
        cx.SketchSprite(bmp, 0, 0, Width, Height, 0, 0, Width / tw, Height / th, Vector4.One);
    }

    public enum Outline
    {
        None = 0,
        UpperLeft = 1,
        Top = 2,
        UpperRight = 3,
        Right = 4,
        LowerRight = 5,
        Bottom = 6,
        LowerLeft = 7,
        Left = 8,
    }

    public Outline BorderLocale { get; set; }

    public static Outline UnpackBorderLocale(bool bottom, bool left, bool right, bool top)
    {
        if (right) return top ? Outline.UpperRight : bottom ? Outline.LowerRight : Outline.Right;
        if (left) return top ? Outline.UpperLeft : bottom ? Outline.LowerLeft : Outline.Left;
        return top ? Outline.Top : bottom ? Outline.Bottom : Outline.None;
    }

    public RescaleRims Rims
    {
        get
        {
            return BorderLocale switch
            {
                Outline.UpperLeft => RescaleRims.Left | RescaleRims.Top,
                Outline.Top => RescaleRims.Top,
                Outline.UpperRight => RescaleRims.Right | RescaleRims.Top,
                Outline.Right => RescaleRims.Right,
                Outline.LowerRight => RescaleRims.Right | RescaleRims.Bottom,
                Outline.Bottom => RescaleRims.Bottom,
                Outline.LowerLeft => RescaleRims.Left | RescaleRims.Bottom,
                Outline.Left => RescaleRims.Left,
                _ => RescaleRims.None,
            };
        }
    }
}
