using System.Numerics;

namespace MacAC.Client.Shell;

public sealed class WidgetScrollablePane : WidgetBoard
{
    protected override bool ClipsDescendants => true;

    private readonly Dictionary<WidgetElem, float> _baseTops = new(ReferenceEqualityComparer.Instance);

    public WidgetScrollable Scroll { get; } = new();

    public int LineHeight { get; set; } = 16;

    public int ContentHeight { get; private set; }

    public WidgetScrollablePane()
    {
        BackgroundColor = Vector4.Zero;
        BorderTint = Vector4.Zero;
    }

    public override void AddChild(WidgetElem descendant)
    {
        base.AddChild(descendant);
        Follow(descendant);
    }

    public override bool DropDescendant(WidgetElem descendant)
    {
        bool removed = base.DropDescendant(descendant);
        if (removed)
        {
            _baseTops.Remove(descendant);
            RecomputeSubstanceHeight();
        }
        return removed;
    }

    public void WipeSubstance()
    {
        foreach (var descendant in DescendantsBackToFrontCapture())
            DropDescendant(descendant);
        _baseTops.Clear();
        ContentHeight = 0;
        Scroll.AssignRollY(0);
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.Roll)
        {
            Scroll.RollByStrokes(-e.Data0);
            ArrangementScrollableDescendants();
            return true;
        }
        return base.OnSignal(e);
    }

    internal void ArrangementScrollableDescendants()
    {
        Scroll.LineHeight = Math.Max(1, LineHeight);
        Scroll.ContentHeight = ContentHeight;
        Scroll.LensHeight = Math.Max(0, (int)MathF.Floor(Height));
        Scroll.AssignRollY(Scroll.RollY);

        foreach (var descendant in Children)
        {
            if (!_baseTops.TryGetValue(descendant, out float baseTop))
                continue;

            float top = baseTop - Scroll.RollY;
            descendant.Top = top;
            descendant.Visible = top + descendant.Height > 0.5f && top < Height - 0.5f;
        }
    }

    protected override void OnPaint(WidgetRenderScope cx)
    {
        ArrangementScrollableDescendants();
        base.OnPaint(cx);
    }

    private void Follow(WidgetElem descendant)
    {
        descendant.Moorings = MooringRims.None;
        _baseTops[descendant] = descendant.Top;
        RecomputeSubstanceHeight();
    }

    private void RecomputeSubstanceHeight()
    {
        float bottom = 0f;
        foreach (var descendant in Children)
        {
            if (!_baseTops.TryGetValue(descendant, out float top))
                continue;
            bottom = MathF.Max(bottom, top + descendant.Height);
        }
        ContentHeight = (int)MathF.Ceiling(bottom);
        Scroll.AssignRollY(Scroll.RollY);
    }
}
