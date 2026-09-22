using System.Numerics;

namespace MacAC.Client.Shell;

public class WidgetBoard : WidgetElem
{
    public Vector4 BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.55f);

    public Vector4 BorderTint { get; set; } = new(0.15f, 0.15f, 0.2f, 0.8f);

    public float BorderThickness { get; set; } = 1f;

    public uint BackgroundSprite { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        if (BackgroundSprite is not 0 && SpriteResolve is { } sr)
        {
            var (bmp, tw, th) = sr(BackgroundSprite);
            if (bmp is not 0 && tw is not 0 && th is not 0)
                cx.SketchSprite(bmp, 0, 0, Width, Height, 0, 0, Width / tw, Height / th, Vector4.One);
        }
        else if (BackgroundColor.W > 0f)
        {
            cx.SketchPopulate(0, 0, Width, Height, BackgroundColor);
        }

        if (BorderTint.W > 0f && BorderThickness > 0f)
            cx.SketchRectOutline(0, 0, Width, Height, BorderTint, BorderThickness);
    }
}

public class WidgetCaption : WidgetElem
{
    public string Text { get; set; } = string.Empty;
    public Vector4 PhraseColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Func<string?>? PhraseSrc { get; set; }

    public WidgetDatFont? DatFont { get; set; }

    public bool Outline { get; set; } = true;

    public WidgetCaption() { ClickThrough = true; }

    protected override bool ClipsDescendants => false;

    protected override void OnPaint(WidgetRenderScope cx)
    {
        string phrase = PhraseSrc?.Invoke() ?? Text;
        float w = DatFont is { } font
            ? font.MeasureWidth(phrase)
            : (cx.DefaultFont?.MeasureWidth(phrase) ?? phrase.Length * 7f);
        float h = DatFont?.LineHeight
            ?? cx.DefaultFont?.LineHeight ?? 14f;
        if (w != Width) Width = w;
        if (h != Height) Height = h;
        if (DatFont is { } dat)
            cx.PaintStringDat(dat, phrase, 0, 0, PhraseColor, Outline);
        else
            cx.SketchString(phrase, 0, 0, PhraseColor);
    }
}

public class WidgetSimpleButton : WidgetBoard
{
    public string Text { get; set; } = string.Empty;
    public Vector4 TextTint { get; set; } = new(1f, 1f, 1f, 1f);

    public Func<string?>? WordingSrc { get; set; }

    public WidgetDatFont? DatFont { get; set; }

    public bool Outline { get; set; } = true;

    public Func<(uint tex, int w, int h)>? GlyphSrc { get; set; }

    public event System.Action? Click;

    public override bool HndsPress => true;

    public WidgetSimpleButton()
    {
        BackgroundColor = new Vector4(0.1f, 0.1f, 0.15f, 0.8f);
        BorderTint = new Vector4(0.45f, 0.45f, 0.55f, 1f);
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.Click && Enabled)
        {
            Click?.Invoke();
            return true;
        }
        return false;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        base.OnPaint(cx);

        float glyphColumn = 0f;
        if (GlyphSrc is { } glyphSrc)
        {
            float reach = MathF.Max(0f, MathF.Min(Width, Height) - 6f);
            glyphColumn = reach + 6f;

            (uint bmp, int w, int h) = glyphSrc();
            if (bmp is not 0u && w > 0 && h > 0)
            {
                float scaling = MathF.Min(reach / w, reach / h);
                float paintWidth = w * scaling;
                float paintHeight = h * scaling;
                cx.SketchSprite(
                    bmp,
                    3f + (reach - paintWidth) * 0.5f,
                    (Height - paintHeight) * 0.5f,
                    paintWidth, paintHeight,
                    0f, 0f, 1f, 1f, Vector4.One);
            }
        }

        string legend = WordingSrc?.Invoke() ?? Text;
        if (legend.Length is 0) return;

        float legendAreaX = glyphColumn;
        float legendAreaWidth = MathF.Max(0f, Width - glyphColumn);
        if (DatFont is { } dat)
        {
            float datW = dat.MeasureWidth(legend);
            cx.PaintStringDat(
                dat, legend,
                legendAreaX + (legendAreaWidth - datW) * 0.5f,
                (Height - dat.LineHeight) * 0.5f,
                TextTint, Outline);
            return;
        }

        if (cx.DefaultFont is null) return;
        float phraseW = cx.DefaultFont.MeasureWidth(legend);
        float tx = legendAreaX + (legendAreaWidth - phraseW) * 0.5f;
        float ty = (Height - cx.DefaultFont.LineHeight) * 0.5f;
        cx.SketchString(legend, tx, ty, TextTint);
    }
}

public class WidgetClickablePane : WidgetBoard
{
    public Action? OnClick { get; set; }

    public string? TooltipText { get; set; }

    public override string? FetchHintPhrase() =>
        string.IsNullOrWhiteSpace(TooltipText) ? null : TooltipText;

    public WidgetClickablePane()
    {
        ClickThrough = false;
    }

    public override bool HndsPress => true;

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.Click && Enabled)
        {
            OnClick?.Invoke();
            return true;
        }
        return false;
    }
}
