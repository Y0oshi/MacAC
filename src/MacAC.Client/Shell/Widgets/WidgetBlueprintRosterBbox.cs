using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed class WidgetBlueprintRosterBbox(
    ElemDetails details,
    Func<uint, (uint tex, int w, int h)> locate,
    IReadOnlyList<WidgetTemplateListEntry> blueprints,
    uint scrollerElemIdent) : WidgetDatElement(details, locate)
{
    public const uint CanonKindTag = 5u;

    private const int DefaultStrokeHeight = 16;
    private int _queuedStrokeHeight = DefaultStrokeHeight;

    public IReadOnlyList<WidgetTemplateListEntry> Templates { get; } = blueprints;

    public uint ScrollbarElementId { get; } = scrollerElemIdent;

    public WidgetScrollable Scroll => Viewport.Scroll;

    public int ContentHeight => ViewRectForTest?.ContentHeight ?? 0;

    public int LineHeight
    {
        get => ViewRectForTest?.LineHeight ?? _queuedStrokeHeight;
        set
        {
            _queuedStrokeHeight = value;
            ViewRectForTest?.LineHeight = value;
        }
    }

    public Func<uint, uint, WidgetElem?>? TemplateResolver { get; set; }

    public override bool ConsumesDatChildren => true;

    internal WidgetScrollablePane? ViewRectForTest { get; private set; }

    private WidgetScrollablePane Viewport
    {
        get
        {
            if (ViewRectForTest is null)
            {
                ViewRectForTest = new WidgetScrollablePane
                {
                    Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right | MooringRims.Bottom,
                    LineHeight = _queuedStrokeHeight,
                    Width = Width,
                    Height = Height,
                };
                base.AddChild(ViewRectForTest);

                ViewRectForTest.GrabLatestMooringBaseline();
            }
            return ViewRectForTest;
        }
    }

    public WidgetElem? AppendGearFromBlueprintRoster(int ordinal)
    {
        if (ordinal < 0 || ordinal >= Templates.Count) return null;
        var locator = TemplateResolver;
        if (locator is null) return null;

        var listing = Templates[ordinal];
        WidgetElem? rank = locator(listing.TemplateLayoutId, listing.TemplateElementId);
        return rank is null ? null : AppendPrebuiltRank(rank);
    }

    public WidgetElem AppendPrebuiltRank(WidgetElem rank)
    {
        ArgumentNullException.ThrowIfNull(rank);
        var viewRect = Viewport;
        rank.Left = 0f;
        rank.Top = viewRect.ContentHeight;
        viewRect.AddChild(rank);
        return rank;
    }

    public void DeleteRear(int retainedItemCount)
    {
        int tally = ViewRectForTest?.Children.Count ?? 0;
        if (retainedItemCount < 0 || retainedItemCount > tally)
            throw new ArgumentOutOfRangeException(nameof(retainedItemCount));
        if (ViewRectForTest is null || retainedItemCount == tally)
            return;

        for (int idx = tally - 1; idx >= retainedItemCount; --idx)
            ViewRectForTest.DropDescendant(ViewRectForTest.Children[idx]);
    }

    public int GearCount => ViewRectForTest?.Children.Count ?? 0;

    public void Flush() => ViewRectForTest?.WipeSubstance();

    public void DrainPreservingRoll()
    {
        if (ViewRectForTest is null) return;
        int storedRollY = ViewRectForTest.Scroll.RollY;
        ViewRectForTest.WipeSubstance();
        ViewRectForTest.Scroll.AssignRollY(storedRollY);
    }
}
