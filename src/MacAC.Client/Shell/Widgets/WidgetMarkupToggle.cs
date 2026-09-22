using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetMarkupToggle : WidgetElem
{
    public string Text { get; set; } = string.Empty;
    public Func<string?>? TextSrc { get; set; }
    public Func<bool>? CheckedSrc { get; set; }
    public WidgetDatFont? DatFont { get; set; }
    public Vector4 TextColor { get; set; } =
        new(0.86f, 0.84f, 0.74f, 1f);
    public Action? Toggle { get; set; }

    public bool IsChecked => CheckedSrc?.Invoke() ?? false;

    public override bool HndsPress => true;

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type != WidgetEventType.Click || !Enabled)
            return false;
        Toggle?.Invoke();
        return true;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        WidgetCheckLamp.Draw(cx, 1f, MathF.Max(1f, (Height - WidgetCheckLamp.LampDims) * 0.5f), IsChecked);

        string legend = TextSrc?.Invoke() ?? Text;
        Vector4 tint = Enabled
            ? TextColor
            : new Vector4(TextColor.X, TextColor.Y, TextColor.Z, 0.42f);
        float y = DatFont is { } typeface
            ? (Height - typeface.LineHeight) * 0.5f
            : 1f;
        if (DatFont is { } dat)
            cx.PaintStringDat(dat, legend, 17f, y, tint, outline: true);
        else
            cx.SketchString(legend, 17f, y, tint);
    }
}
