using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed class WidgetTabBoard(
    ElemDetails details,
    Func<uint, (uint tex, int w, int h)> locate,
    IReadOnlyList<WidgetTabChartEntry> tabs) : WidgetDatElement(details, locate), IWidgetChildrenAttachedListener
{
    public const uint CanonKindIdent = 8u;
    private readonly List<WidgetTabChartEntry> _unresolved = [];

    public IReadOnlyList<WidgetTabChartEntry> Tabs { get; } = tabs;

    public uint EngagedSheetElemIdent { get; private set; }

    public event Action<uint, uint>? ActivePageChanged;

    public bool BehaviorEngaged { get; private set; }

    public IReadOnlyList<WidgetTabChartEntry> UnresolvedListings => _unresolved;

    public void ActivateTabBehavior()
    {
        if (BehaviorEngaged) return;
        BehaviorEngaged = true;

        _unresolved.Clear();
        WidgetTabChartEntry? defaultListing = null;
        foreach (WidgetTabChartEntry listing in Tabs)
        {
            WidgetElem? btn = SeekDescendant(this, listing.ButtonElementId);
            WidgetElem? sheet = SeekDescendant(this, listing.PageElementId);
            if (btn is null || sheet is null)
            {
                _unresolved.Add(listing);
                Console.WriteLine(
                    $"[UI] WidgetTabBoard 0x{Info.Id:X8}: tab entry button=0x{listing.ButtonElementId:X8} "
                    + $"page=0x{listing.PageElementId:X8} didn't resolve against the built subtree "
                    + $"(button {(btn is null ? "MISSING" : "ok")}, page {(sheet is null ? "MISSING" : "ok")}).");
            }

            uint sheetIdent = listing.PageElementId;
            CanonTabWiring.AssignPress(btn, () => SwitchTo(sheetIdent));

            if (listing.IsDefault)
                defaultListing = listing;
        }

        if (defaultListing is { } def)
            SwitchTo(def.PageElementId);
    }

    public void SwitchTo(uint sheetElemIdent)
    {
        if (EngagedSheetElemIdent == sheetElemIdent) return;
        uint earlierSheetElemIdent = EngagedSheetElemIdent;

        foreach (WidgetTabChartEntry listing in Tabs)
        {
            bool engaged = listing.PageElementId == sheetElemIdent;
            WidgetElem? sheet = SeekDescendant(this, listing.PageElementId);
            sheet?.Visible = engaged;

            WidgetElem? btn = SeekDescendant(this, listing.ButtonElementId);
            CanonTabWiring.ApplyOpen(btn, engaged);
        }

        EngagedSheetElemIdent = sheetElemIdent;
        ActivePageChanged?.Invoke(earlierSheetElemIdent, sheetElemIdent);
    }

    void IWidgetChildrenAttachedListener.OnDescendantsAffixed()
    {
    }
}
