namespace MacAC.Client.Shell.Panels;

public sealed class WidgetVitalsRoot(ElemDetails details, Func<uint, (uint tex, int w, int h)> locate) : WidgetDatElement(details, locate)
{
    public const uint GmVitalsClassIdent = 0x10000009u;
    public const uint GmFloatyVitalsClassIdent = 0x1000004Du;
    public const uint GmFloatyFlankVitalsClassIdent = 0x10000056u;

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type is WidgetEventType.PointerDown or WidgetEventType.RightDown
            && !PressConsumedByChrome(e.Target))
        {
            TrySetCanonPhase(
                EngagedCanonPhaseIdent == CanonWidgetStateIds.HideDetail
                    ? CanonWidgetStateIds.ShowDetail
                    : CanonWidgetStateIds.HideDetail);
        }
        return base.OnSignal(in e);
    }

    private bool PressConsumedByChrome(WidgetElem? mark)
    {
        for (WidgetElem? element = mark; element is not null && element != this; element = element.Ancestor)
            if (element.PaneRelocateHnd || element is WidgetResizeGrip)
                return true;
        return false;
    }
}
