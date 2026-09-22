namespace MacAC.Client.Shell.Panels;

public sealed class MiniGameWidgetDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x2100001Eu;
    public const uint RootId = 0x1000016Au;
    public const uint CloseId = 0x1000016Bu;

    private readonly WidgetBtn _shut;

    private MiniGameWidgetDriver(WidgetBtn shut, Action? shutPane)
    {
        _shut = shut;
        _shut.OnClick = shutPane;
    }

    public static MiniGameWidgetDriver? Bind(
        ImportedArrangement arrangement,
        Action? shut = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        return arrangement.SeekElem(CloseId) is WidgetBtn btn
            ? new MiniGameWidgetDriver(btn, shut)
            : null;
    }

    public void Dispose() => _shut.OnClick = null;
}
