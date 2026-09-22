using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetMarkupTabButton : WidgetSimpleButton
{
    private static readonly Vector4 EngagedPhrase =
        new(0.94f, 0.76f, 0.18f, 1f);
    private static readonly Vector4 NormPhrase =
        new(0.78f, 0.76f, 0.67f, 1f);
    private static readonly Vector4 DisabledPhrase =
        new(0.34f, 0.33f, 0.29f, 1f);
    private static readonly Vector4 Underline =
        new(0.77f, 0.59f, 0.12f, 1f);

    public Func<bool>? ChosenSrc { get; set; }

    public bool IsChosen => ChosenSrc?.Invoke() ?? false;

    public WidgetMarkupTabButton()
    {
        BackgroundColor = Vector4.Zero;
        BorderTint = Vector4.Zero;
        BorderThickness = 0f;
        Outline = true;
    }

    protected override void OnBeat(double diffSecs)
    {
        base.OnBeat(diffSecs);
        TextTint = !Enabled
            ? DisabledPhrase
            : IsChosen ? EngagedPhrase : NormPhrase;
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        base.OnPaint(cx);
        if (IsChosen)
            cx.SketchPopulate(2f, Height - 2f, MathF.Max(0f, Width - 4f), 1f, Underline);
    }
}
