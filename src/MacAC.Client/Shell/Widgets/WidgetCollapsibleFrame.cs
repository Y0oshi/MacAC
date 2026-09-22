namespace MacAC.Client.Shell;

public sealed class WidgetCollapsibleFrame(Func<uint, (uint, int, int)> locate) : WidgetNineSlicePane(locate), IRetainedWindowStateDriver
{
    public float CollapsedHeight { get; set; }
    public float ExpandedHeight { get; set; }
    public IReadOnlyList<WidgetElem> SecondRank { get; set; } = Array.Empty<WidgetElem>();

    public bool IsExpanded => Height >= (CollapsedHeight + ExpandedHeight) * 0.5f;

    public RetainedWindowLedger GrabPanePhase()
        => new(Collapsed: !IsExpanded);

    public void ReinstatePanePhase(RetainedWindowLedger phase)
    {
        if (ExpandedHeight <= CollapsedHeight) return;
        Height = phase.Collapsed ? CollapsedHeight : ExpandedHeight;
        for (int idx = 0; idx < SecondRank.Count; ++idx)
            SecondRank[idx].Visible = !phase.Collapsed;
    }

    protected override void OnBeat(double diffSecs)
    {
        base.OnBeat(diffSecs);
        if (ExpandedHeight <= CollapsedHeight) return;
        bool expanded = IsExpanded;
        Height = expanded ? ExpandedHeight : CollapsedHeight;   // snap the dragged height to a stop
        for (int idx = 0; idx < SecondRank.Count; ++idx) SecondRank[idx].Visible = expanded;
    }

    // Test hook - OnTick is protected
    internal void PulseForTest(double dt) => OnBeat(dt);
}
