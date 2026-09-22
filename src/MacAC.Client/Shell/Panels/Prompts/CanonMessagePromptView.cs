namespace MacAC.Client.Shell.Panels;

internal sealed class CanonMessagePromptView : ICanonPromptView
{
    public const uint RootElemId = 0x24u;
    public const uint OkBtnIdent = 0x26u;
    public const uint PopupElementIdent = 0x3Du;
    public const uint MsgElementId = 0x3Eu;

    private readonly WidgetTrunk _hub;
    private readonly uint _ctx;
    private readonly Action<uint> _shutPopup;
    private readonly WidgetElem _popup;
    private readonly WidgetPhrase _msg;
    private readonly WidgetBtn _ok;
    private readonly float _basePopupHeight;
    private readonly float _baseMsgHeight;

    public CanonMessagePromptView(
        WidgetTrunk host,
        ImportedArrangement layout,
        CanonPromptData blob,
        uint ctx,
        Action<uint> closeDialog)
    {
        _hub = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(blob);
        _ctx = ctx;
        _shutPopup = closeDialog ?? throw new ArgumentNullException(nameof(closeDialog));

        Root = layout.Root as WidgetPopupTrunk
            ?? throw new ArgumentException("Message layout root isn't a UiDialogRoot", nameof(layout));
        _popup = layout.SeekElem(PopupElementIdent)
            ?? throw new ArgumentException("Message layout is absent popup element 0x3D", nameof(layout));
        _msg = layout.SeekElem(MsgElementId) as WidgetPhrase
            ?? throw new ArgumentException("Message layout is absent text element 0x3E", nameof(layout));
        _ok = layout.SeekElem(OkBtnIdent) as WidgetBtn
            ?? throw new ArgumentException("Message layout is absent OK button 0x26", nameof(layout));

        _basePopupHeight = _popup.Height;
        _baseMsgHeight = _msg.Height;
        _popup.ArrangementRule = null;
        _popup.Moorings = MooringRims.None;
        _msg.ArrangementRule = null;
        _msg.Moorings = MooringRims.None;
        _msg.Padding = 0f;
        _msg.Selectable = false;

        Root.Cancel = Close;
        _ok.OnClick = Close;
        AssignMsg(blob.FetchString(CanonPromptProperty.Message) ?? string.Empty);
        DimsAndMiddle();
    }

    public WidgetPopupTrunk Root { get; }

    public void Tick() => DimsAndMiddle();

    public void AssignQueuedTally(int tally)
    {
    }

    public void UnfastenHandlers()
    {
        Root.Cancel = null;
        _ok.OnClick = null;
    }

    private void Close() => _shutPopup(_ctx);

    private void AssignMsg(string phrase)
    {
        float ceilingWidth = Math.Max(1f, _msg.Width - 2f * _msg.Padding);
        Func<string, float> gauge = _msg.DatFont is { } typeface
            ? typeface.MeasureWidth
            : static val => val.Length * 8f;
        var wrapped = WidgetPhrase.EncloseWords(phrase, gauge, ceilingWidth);
        WidgetPhrase.Line[] strokes = new WidgetPhrase.Line[wrapped.Count];
        for (int idx = 0; idx < wrapped.Count; ++idx)
            strokes[idx] = new WidgetPhrase.Line(wrapped[idx], _msg.DefaultTint);
        _msg.StrokesSupplier = () => strokes;

        float strokeHeight = _msg.DatFont?.LineHeight ?? 16f;
        _msg.Height = Math.Max(_baseMsgHeight, strokes.Length * strokeHeight);
        _popup.Height = _basePopupHeight + (_msg.Height - _baseMsgHeight);
    }

    private void DimsAndMiddle()
    {
        System.Numerics.Vector2 space = _hub.NetCanvasDims;
        Root.Left = 0f;
        Root.Top = 0f;
        Root.Width = space.X;
        Root.Height = space.Y;
        _popup.Left = MathF.Round((Root.Width - _popup.Width) * 0.5f);
        _popup.Top = MathF.Round((Root.Height - _popup.Height) * 0.5f);
    }
}
