using System.Numerics;
using MacAC.Client.Shell.Panels;

namespace MacAC.Client.Shell;

public sealed class WidgetTickboxBitfield64 : WidgetBoard
{
    public const uint BlueprintTickboxElemIdent = 0x10000219u;

    public readonly record struct ClientRow(
        ulong LowMask, ulong HighMask, string Label, string? Tooltip,
        WidgetElem RowRoot, WidgetBtn Toggle);

    private readonly List<ClientRow> _ranks = [];
    private float _substanceHeight;

    public IReadOnlyList<ClientRow> Rows => _ranks;

    public ulong LatestLo { get; private set; }

    public ulong LatestHi { get; private set; }

    private ulong _defaultLo, _defaultHi;

    public IReadOnlyList<WidgetTemplateListEntry> Blueprints { get; }

    public uint CheckedLedSprite { get; }

    public uint UncheckedLedSprite { get; }

    public Func<uint, uint, WidgetElem?>? TemplateResolver { get; set; }

    public WidgetDatFont? LabelFont { get; set; }

    public Action<ulong, ulong>? ValAltered { get; set; }

    public WidgetTickboxBitfield64(
        IReadOnlyList<WidgetTemplateListEntry> blueprints,
        uint checkedLedSprite = 0u,
        uint uncheckedLedSprite = 0u)
    {
        Blueprints = blueprints;
        CheckedLedSprite = checkedLedSprite;
        UncheckedLedSprite = uncheckedLedSprite;
        BackgroundColor = Vector4.Zero;
        BorderTint = Vector4.Zero;
    }

    public void SetDefaultValue(ulong lo, ulong hi)
    {
        _defaultLo = lo;
        _defaultHi = hi;
        if (_ranks.Count is 0)
        {
            LatestLo = lo;
            LatestHi = hi;
        }
    }

    public void RecoverDefaultVal()
    {
        LatestLo = _defaultLo;
        LatestHi = _defaultHi;
        RenewRankVisuals();
        ValAltered?.Invoke(LatestLo, LatestHi);
    }

    public void SetCurrentValue(ulong lo, ulong hi)
    {
        LatestLo = lo;
        LatestHi = hi;
        RenewRankVisuals();
    }

    public WidgetBtn? AddChild(ulong loBitmask, ulong hiBitmask, string caption, string? hint = null)
    {
        if (Blueprints.Count is 0)
        {
            Console.WriteLine("[UI] UiCheckboxBitfield64.AddChild: no authored row template (property 0x64 empty) - can't build a row");
            return null;
        }
        var locator = TemplateResolver;
        if (locator is null)
        {
            Console.WriteLine("[UI] UiCheckboxBitfield64.AddChild: TemplateResolver not wired yet - can't build a row");
            return null;
        }

        var listing = Blueprints[0];
        WidgetElem? rank = locator(listing.TemplateLayoutId, listing.TemplateElementId);
        if (rank is null)
        {
            Console.WriteLine($"[UI] UiCheckboxBitfield64.AddChild: resolver returned null for template 0x{listing.TemplateLayoutId:X8}/0x{listing.TemplateElementId:X8}.");
            return null;
        }

        WidgetBtn? tickbox = SeekTickboxRecursive(rank);
        if (tickbox is null)
        {
            Console.WriteLine($"[UI] UiCheckboxBitfield64.AddChild: resolved row template didn't contain checkbox 0x{BlueprintTickboxElemIdent:X8} - row will not respond to clicks");
            return null;
        }

        tickbox.Label = caption;
        tickbox.TooltipText = hint;
        tickbox.OnClick = () => FlipRank(loBitmask, hiBitmask, tickbox);

        rank.Left = 0f;
        rank.Top = _substanceHeight;
        _substanceHeight += rank.Height;
        base.AddChild(rank);

        Height = _substanceHeight;

        ClientRow newRank = new ClientRow(loBitmask, hiBitmask, caption, hint, rank, tickbox);
        _ranks.Add(newRank);
        ImposeRankVisuals(newRank);
        return tickbox;
    }

    private static WidgetBtn? SeekTickboxRecursive(WidgetElem joint)
    {
        if (joint.DatElemIdent == BlueprintTickboxElemIdent && joint is WidgetBtn btn)
            return btn;
        foreach (WidgetElem descendant in joint.Children)
        {
            WidgetBtn? located = SeekTickboxRecursive(descendant);
            if (located is not null) return located;
        }
        return null;
    }

    private bool IsAnySet(ulong loBitmask, ulong hiBitmask)
        => (LatestLo & loBitmask) is not 0 || (LatestHi & hiBitmask) is not 0;

    private bool IsAllSet(ulong loBitmask, ulong hiBitmask)
    {
        return (LatestLo & loBitmask) == loBitmask && (LatestHi & hiBitmask) == hiBitmask;
    }

    private void FlipRank(ulong loBitmask, ulong hiBitmask, WidgetBtn flip)
    {
        bool pivotOn = !IsAnySet(loBitmask, hiBitmask);
        if (pivotOn)
        {
            LatestLo |= loBitmask;
            LatestHi |= hiBitmask;
        }
        else
        {
            LatestLo &= ~loBitmask;
            LatestHi &= ~hiBitmask;
        }
        ImposeRankVisualsForBitmask(loBitmask, hiBitmask, flip);
        ValAltered?.Invoke(LatestLo, LatestHi);
    }

    private void RenewRankVisuals()
    {
        foreach (ClientRow rank in _ranks)
            ImposeRankVisuals(rank);
    }

    private void ImposeRankVisuals(ClientRow rank) => ImposeRankVisualsForBitmask(rank.LowMask, rank.HighMask, rank.Toggle);

    private void ImposeRankVisualsForBitmask(ulong loBitmask, ulong hiBitmask, WidgetBtn flip)
    {
        bool anySet = IsAnySet(loBitmask, hiBitmask);
        flip.Selected = anySet;
        if (!anySet)
        {
            flip.FaceFileOverride = null;
            return;
        }
        uint overrideSprite = IsAllSet(loBitmask, hiBitmask) ? CheckedLedSprite : UncheckedLedSprite;
        flip.FaceFileOverride = overrideSprite is not 0u ? overrideSprite : null;
    }
}
