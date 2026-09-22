namespace MacAC.Client.Shell.Panels;

internal sealed class CanonConfirmationPromptView : ICanonPromptView
{
    public const uint TrunkElemTag = 0x15u;
    public const uint AdmitBtnTag = 0x17u;
    public const uint RejectBtnTag = 0x19u;
    public const uint QueuedReadoutIdent = 0x33u;
    public const uint QueuedReadoutPhraseIdent = 0x34u;
    public const uint PopupElemTag = 0x3Du;
    public const uint MsgElemIdent = 0x3Eu;

    private readonly WidgetTrunk _hub;
    private readonly uint _ctx;
    private readonly Action<uint> _shutPopup;
    private readonly WidgetElem _popup;
    private readonly WidgetPhrase _msg;
    private readonly WidgetBtn _admit;
    private readonly WidgetBtn _reject;
    private readonly WidgetElem? _queuedReadout;
    private readonly float _basePopupHeight;
    private readonly float _baseMsgHeight;

    public CanonConfirmationPromptView(
        WidgetTrunk host,
        ImportedArrangement layout,
        CanonPromptData data,
        uint ctx,
        Action<uint> closeDialog)
    {
        _hub = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(layout);
        _blob = data ?? throw new ArgumentNullException(nameof(data));
        _ctx = ctx;
        _shutPopup = closeDialog ?? throw new ArgumentNullException(nameof(closeDialog));

        Root = layout.Root as WidgetPopupTrunk
            ?? throw new ArgumentException("Confirmation layout root isn't a UiDialogRoot", nameof(layout));
        _popup = layout.SeekElem(PopupElemTag)
            ?? throw new ArgumentException("Confirmation layout is absent popup element 0x3D", nameof(layout));
        _msg = layout.SeekElem(MsgElemIdent) as WidgetPhrase
            ?? throw new ArgumentException("Confirmation layout is absent text element 0x3E", nameof(layout));
        _admit = layout.SeekElem(AdmitBtnTag) as WidgetBtn
            ?? throw new ArgumentException("Confirmation layout is absent accept button 0x17", nameof(layout));
        _reject = layout.SeekElem(RejectBtnTag) as WidgetBtn
            ?? throw new ArgumentException("Confirmation layout is absent reject button 0x19", nameof(layout));
        _queuedReadout = layout.SeekElem(QueuedReadoutIdent);

        _basePopupHeight = _popup.Height;
        _baseMsgHeight = _msg.Height;
        _popup.ArrangementRule = null;
        _popup.Moorings = MooringRims.None;
        _msg.ArrangementRule = null;
        _msg.Moorings = MooringRims.None;
        _msg.Padding = 0f;
        _msg.Selectable = false;

        if (_blob.FetchString(CanonPromptProperty.AdmitCaption) is { } admitCaption)
            _admit.Label = admitCaption;
        if (_blob.FetchString(CanonPromptProperty.RejectCaption) is { } rejectCaption)
            _reject.Label = rejectCaption;

        Root.Cancel = Reject;
        _admit.OnClick = Admit;
        _reject.OnClick = Reject;
        AssignMsg(_blob.FetchString(CanonPromptProperty.Message) ?? string.Empty);
        DimsAndMiddle();
    }

    public WidgetPopupTrunk Root { get; }

    private readonly CanonPromptData _blob;

    public CanonPromptData Data => _blob;
    public void Tick() => DimsAndMiddle();

    public void AssignQueuedTally(int tally)
    {
        if (_queuedReadout is IWidgetDatStateful stateful)
            stateful.TrySetCanonPhase(tally is 0 ? 0x19u : 0x18u);
    }

    public void UnfastenHandlers()
    {
        Root.Cancel = null;
        _admit.OnClick = null;
        _reject.OnClick = null;
    }

    private void Admit()
    {
        _blob.Set(CanonPromptProperty.AckOutcome, true);
        _shutPopup(_ctx);
    }

    private void Reject()
    {
        _blob.Set(CanonPromptProperty.AckOutcome, false);
        _shutPopup(_ctx);
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
