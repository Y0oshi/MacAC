namespace MacAC.Client.Shell.Panels;

internal sealed class CanonWaitPromptView : ICanonPromptView
{
    public const uint RootElementId = 0x31u;
    public const uint PopupElementId = 0x3Du;
    public const uint MessageElemId = 0x3Eu;

    private readonly WidgetTrunk _hub;
    private readonly WidgetElem _popup;
    private readonly WidgetPhrase _msg;
    private readonly float _basePopupHeight;
    private readonly float _baseMsgHeight;

    public CanonWaitPromptView(
        WidgetTrunk host,
        ImportedArrangement layout,
        CanonPromptData blob)
    {
        _hub = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(blob);

        Root = layout.Root as WidgetPopupTrunk
            ?? throw new ArgumentException("Wait layout root isn't a UiDialogRoot", nameof(layout));
        _popup = layout.SeekElem(PopupElementId)
            ?? throw new ArgumentException("Wait layout is absent popup element 0x3D", nameof(layout));
        _msg = layout.SeekElem(MessageElemId) as WidgetPhrase
            ?? throw new ArgumentException("Wait layout is absent text element 0x3E", nameof(layout));

        _basePopupHeight = _popup.Height;
        _baseMsgHeight = _msg.Height;
        _popup.ArrangementRule = null;
        _popup.Moorings = MooringRims.None;
        _msg.ArrangementRule = null;
        _msg.Moorings = MooringRims.None;
        _msg.Padding = 0f;
        _msg.Selectable = false;

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
    }

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
