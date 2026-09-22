namespace MacAC.Client.Shell.Panels;

internal static class WindowChromeDriver
{
    public const uint UpperLowerBtnIdent = 0x1000046Fu;
    public const uint SatchelShutBtnIdent = 0x100001D2u;
    public const uint ToonShutBtnIdent = 0x1000022Au;

    public static int WireShutBtn(ImportedArrangement arrangement, Action? onShut) => onShut is null ? 0 : AttachShutBtn(arrangement.Root, onShut);

    private static int AttachShutBtn(WidgetElem joint, Action onShut)
    {
        int tally = TryAttachShutBtn(joint, onShut) ? 1 : 0;
        foreach (var descendant in joint.Children)
            tally += AttachShutBtn(descendant, onShut);
        return tally;
    }

    private static bool TryAttachShutBtn(WidgetElem elem, Action onShut)
    {
        switch (elem)
        {
            case WidgetBtn btn when IsShutBtnIdent(btn.ElementId):
                btn.OnClick = onShut;
                return true;

            case WidgetDatElement datElem when IsShutBtnIdent(datElem.ElementId):
                datElem.ClickThrough = false;
                datElem.CapturesPointerDrag = true;
                datElem.OnClick = onShut;
                return true;

            default:
                return false;
        }
    }

    private static bool IsShutBtnIdent(uint ident) =>
        ident is SatchelShutBtnIdent or ToonShutBtnIdent;
}
