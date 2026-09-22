namespace MacAC.Client.Shell.Panels;

internal sealed class CanonConfirmationTextInputPromptView : ICanonPromptView
{
    public const uint TrunkElementId = 0x2Cu;
    public const uint FeedElemIdent = 0x2Cu;
    public const uint AdmitButtonId = 0x2Eu;
    public const uint RejectBtnId = 0x2Fu;
    public const uint PopupElemId = 0x3Du;
    public const uint MsgElemTag = 0x3Eu;

    private readonly WidgetTrunk _hub;
    private readonly CanonPromptData _blob;
    private readonly uint _ctx;
    private readonly Action<uint> _shutPopup;
    private readonly WidgetElem _popup;
    private readonly WidgetPhrase _msg;
    private readonly WidgetField _feed;
    private readonly WidgetBtn _admit;
    private readonly WidgetBtn _reject;
    private readonly float _basePopupHeight;
    private readonly float _baseMsgHeight;
    private bool _focusQueued = true;

    public CanonConfirmationTextInputPromptView(
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
            ?? throw new ArgumentException(
                "Confirmation-text-input layout root isn't a UiDialogRoot",
                nameof(layout));
        _popup = layout.SeekElem(PopupElemId)
            ?? throw new ArgumentException(
                "Confirmation-text-input layout is absent popup element 0x3D",
                nameof(layout));
        _msg = layout.SeekElem(MsgElemTag) as WidgetPhrase
            ?? throw new ArgumentException(
                "Confirmation-text-input layout is absent text element 0x3E",
                nameof(layout));
        _feed = layout.SeekElem(FeedElemIdent) as WidgetField
            ?? throw new ArgumentException(
                "Confirmation-text-input layout is absent input field 0x2C",
                nameof(layout));
        _admit = layout.SeekElem(AdmitButtonId) as WidgetBtn
            ?? throw new ArgumentException(
                "Confirmation-text-input layout is absent accept button 0x2E",
                nameof(layout));
        _reject = layout.SeekElem(RejectBtnId) as WidgetBtn
            ?? throw new ArgumentException(
                "Confirmation-text-input layout is absent reject button 0x2F",
                nameof(layout));

        _basePopupHeight = _popup.Height;
        _baseMsgHeight = _msg.Height;
        _popup.ArrangementRule = null;
        _popup.Moorings = MooringRims.None;
        _msg.ArrangementRule = null;
        _msg.Moorings = MooringRims.None;
        _msg.Padding = 0f;
        _msg.Selectable = false;
        _feed.WipeOnSubmit = false;
        _feed.CaptureHistory = false;

        if (_blob.FetchString(CanonPromptProperty.PhraseFeedAdmitCaption) is { } admitCaption)
            _admit.Label = admitCaption;
        if (_blob.FetchString(CanonPromptProperty.PhraseFeedRejectCaption) is { } rejectCaption)
            _reject.Label = rejectCaption;

        Root.Cancel = Reject;
        _admit.OnClick = Admit;
        _reject.OnClick = Reject;
        _feed.OnSubmit = _ => Admit();
        AssignMsg(_blob.FetchString(CanonPromptProperty.Message) ?? string.Empty);
        DimsAndMiddle();
    }

    public WidgetPopupTrunk Root { get; }

    public void Tick()
    {
        DimsAndMiddle();
        if (_focusQueued && Root.Ancestor is not null)
        {
            _hub.AssignKeyboardFocus(_feed);
            _focusQueued = false;
        }
    }

    public void AssignQueuedTally(int tally)
    {
    }

    public void UnfastenHandlers()
    {
        Root.Cancel = null;
        _admit.OnClick = null;
        _reject.OnClick = null;
        _feed.OnSubmit = null;
    }

    private void Admit()
    {
        _blob.Set(CanonPromptProperty.PhraseFeedOutcome, _feed.Text);
        _shutPopup(_ctx);
    }

    private void Reject()
    {
        _blob.Set(CanonPromptProperty.PhraseFeedOutcome, string.Empty);
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
