namespace MacAC.Client.Shell;

public sealed class WidgetKnobFlipDial : WidgetElem, IWidgetChildrenAttachedListener
{
    public WidgetBtn? Toggle { get; private set; }

    public WidgetScroller? Slider { get; private set; }

    public void OnDescendantsAffixed()
    {
        foreach (WidgetElem descendant in Children)
        {
            if (Toggle is null && descendant is WidgetBtn btn)
                Toggle = btn;
            else if (Slider is null && descendant is WidgetScroller scroller)
                Slider = scroller;
        }
    }
}
