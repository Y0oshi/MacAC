namespace MacAC.Client.Shell.Panels;

internal sealed class CanonConfirmationMenuPromptView : ICanonPromptView
{
    public const uint TrunkElemIdent = 0x1Fu;
    public const uint MenuElemIdent = 0x21u;
    public const uint AdmitBtnIdent = 0x22u;
    public const uint RejectBtnIdent = 0x23u;
    public const uint PopupElemIdent = 0x3Du;

    private readonly WidgetTrunk _hub;
    private readonly CanonPromptData _blob;
    private readonly uint _ctx;
    private readonly Action<uint> _shutPopup;
    private readonly WidgetElem? _popup;
    private readonly WidgetMenu _menu;
    private readonly WidgetBtn _admit;
    private readonly WidgetBtn _reject;

    public CanonConfirmationMenuPromptView(
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
                "Confirmation-menu layout root isn't a UiDialogRoot", nameof(layout));
        _popup = layout.SeekElem(PopupElemIdent);
        _menu = layout.SeekElem(MenuElemIdent) as WidgetMenu
            ?? throw new ArgumentException(
                "Confirmation-menu layout is absent menu element 0x21", nameof(layout));
        _admit = layout.SeekElem(AdmitBtnIdent) as WidgetBtn
            ?? throw new ArgumentException(
                "Confirmation-menu layout is absent accept button 0x22", nameof(layout));
        _reject = layout.SeekElem(RejectBtnIdent) as WidgetBtn
            ?? throw new ArgumentException(
                "Confirmation-menu layout is absent reject button 0x23", nameof(layout));

        IReadOnlyList<string> gearList = _blob.TryGet<string[]>(
            CanonPromptProperty.MenuGearList, out string[] vals)
            ? vals
            : [];
        _menu.Items = gearList.Select(
            static (caption, ordinal) => new WidgetMenu.MenuGear(caption, ordinal)).ToArray();
        int chosen = Math.Clamp(
            _blob.FetchInt32(CanonPromptProperty.MenuPick),
            0,
            Math.Max(0, gearList.Count - 1));
        _menu.Selected = gearList.Count is 0 ? null : chosen;
        _menu.OnSelect = cargo => _menu.Selected = cargo;
        _menu.BtnCaptionSupplier = () =>
            _menu.Selected is int idx && idx >= 0 && idx < gearList.Count
                ? gearList[idx]
                : string.Empty;

        if (_blob.FetchString(CanonPromptProperty.MenuAdmitCaption) is { } admitCaption)
            _admit.Label = admitCaption;
        if (_blob.FetchString(CanonPromptProperty.MenuRejectCaption) is { } rejectCaption)
            _reject.Label = rejectCaption;

        Root.Cancel = Reject;
        _admit.OnClick = Admit;
        _reject.OnClick = Reject;
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
        _admit.OnClick = null;
        _reject.OnClick = null;
        _menu.OnSelect = null;
    }

    private void Admit()
    {
        _blob.Set(
            CanonPromptProperty.MenuPick,
            _menu.Selected is int chosen ? chosen : -1);
        _shutPopup(_ctx);
    }

    private void Reject()
    {
        _blob.Set(CanonPromptProperty.MenuPick, -1);
        _shutPopup(_ctx);
    }

    private void DimsAndMiddle()
    {
        System.Numerics.Vector2 space = _hub.NetCanvasDims;
        Root.Left = 0f;
        Root.Top = 0f;
        Root.Width = space.X;
        Root.Height = space.Y;
        if (_popup is null) return;
        _popup.ArrangementRule = null;
        _popup.Moorings = MooringRims.None;
        _popup.Left = MathF.Round((Root.Width - _popup.Width) * 0.5f);
        _popup.Top = MathF.Round((Root.Height - _popup.Height) * 0.5f);
    }
}
