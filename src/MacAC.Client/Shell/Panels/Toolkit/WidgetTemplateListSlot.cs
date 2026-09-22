namespace MacAC.Client.Shell.Panels;

public sealed class WidgetTemplateListSlot : WidgetGearSlot
{
    private readonly IWidgetDatStateful? _statefulTrunk;
    private readonly uint _normPhase;
    private readonly uint? _chosenPhase;
    private bool? _phaseWasChosen;

    public WidgetTemplateListSlot(
        ImportedArrangement substance,
        uint listingIdent,
        uint normPhase,
        uint? chosenPhase)
    {
        Content = substance;
        ListingIdent = listingIdent;
        _normPhase = normPhase;
        _chosenPhase = chosenPhase;
        _statefulTrunk = substance.Root as IWidgetDatStateful;

        Width = substance.Root.Width;
        Height = substance.Root.Height;
        substance.Root.Left = 0f;
        substance.Root.Top = 0f;
        substance.Root.Moorings = MooringRims.Left | MooringRims.Top
            | MooringRims.Right | MooringRims.Bottom;
        AddChild(substance.Root);
    }

    public ImportedArrangement Content { get; }
    public uint ListingIdent { get; }
    public new Action? Clicked { get; set; }

    public override bool HndsPress => Clicked is not null;

    public void AssignChosen(bool chosen)
    {
        Selected = chosen;
        ImposePickPhase();
    }

    public override bool OnSignal(in WidgetSignal e)
    {
        if (e.Type == WidgetEventType.PointerDown)
            return true;
        if (e.Type == WidgetEventType.Click && Clicked is not null)
        {
            Clicked();
            return true;
        }
        return false;
    }

    protected override void OnPaint(WidgetRenderScope cx)
        => ImposePickPhase();

    private void ImposePickPhase()
    {
        if (_phaseWasChosen == Selected)
            return;

        _phaseWasChosen = Selected;
        _statefulTrunk?.TrySetCanonPhase(
            Selected && _chosenPhase.HasValue ? _chosenPhase.Value : _normPhase);
    }
}
