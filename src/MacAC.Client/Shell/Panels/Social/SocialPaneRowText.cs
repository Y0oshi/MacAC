namespace MacAC.Client.Shell.Panels;

internal static class SocialPaneRowText
{
    public static WidgetPhrase? SeekDeepest(WidgetElem rank)
    {
        var located = rank as WidgetPhrase;
        foreach (WidgetElem descendant in rank.Children)
        {
            if (SeekDeepest(descendant) is { } deeper)
                located = deeper;
        }
        return located;
    }
}
