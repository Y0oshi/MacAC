namespace MacAC.Client.Shell.Panels;

internal static class CanonTabWiring
{
    public static void AssignPress(WidgetElem? elem, Action? act)
    {
        if (elem is null) return;

        elem.ClickThrough = act is null;
        switch (elem)
        {
            case WidgetBtn btn: btn.OnClick = act; break;
            case WidgetPhrase phrase: phrase.OnClick = act; break;
            case WidgetDatElement dat: dat.OnClick = act; break;
        }
    }

    public static bool ApplyOpen(WidgetElem? elem, bool open)
    {
        if (elem is IWidgetDatStateful stateful
            && stateful.TrySetCanonPhase(open ? CanonWidgetStateIds.Open : CanonWidgetStateIds.Closed))
            return true;

        if (elem is WidgetBtn btn)
        {
            btn.Selected = open;
            return true;
        }

        return false;
    }
}
