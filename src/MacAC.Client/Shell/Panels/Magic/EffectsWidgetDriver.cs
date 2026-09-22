using System.Globalization;
using MacAC.Mechanics.Arcana;

namespace MacAC.Client.Shell.Panels;

public sealed class EffectsWidgetDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x2100001Bu;
    public const uint PositiveTrunkIdent = 0x1000011Fu;
    public const uint NegativeTrunkIdent = 0x10000121u;
    public const uint ShutId = 0x100000FCu;
    public const uint RosterIdent = 0x10000123u;
    public const uint RosterScrollerIdent = 0x10000124u;
    public const uint DetailsPhraseIdent = 0x10000126u;
    public const uint DetailsScrollerIdent = 0x10000127u;
    public const uint RankBlueprintIdent = 0x10000128u;
    public const uint RankGlyphIdent = 0x10000129u;
    public const uint RankCaptionIdent = 0x1000012Au;
    public const uint RankIntervalIdent = 0x1000012Bu;

    private readonly Grimoire _grimoire;
    private readonly bool _positive;
    private readonly Func<double> _srvMoment;
    private readonly Func<uint, uint> _locateArcanumGlyph;
    private readonly EffectRowTemplateMint _blueprints;
    private readonly string _pickPrompt;
    private readonly WidgetGearRoster _roster;
    private readonly WidgetPhrase? _details;
    private readonly WidgetTextArrangementShelf<string>? _detailsArrangement;
    private readonly WidgetBtn? _shut;
    private readonly WidgetScroller? _rosterScroller;
    private readonly WidgetScroller? _detailsScroller;
    private readonly Dictionary<uint, EffectRowTemplateMint.FxRank> _ranks = [];
    private double _previousIntervalRefresh = double.NaN;
    private bool _destroyed;

    internal uint? ChosenArcanumIdent { get; private set; }

    private EffectsWidgetDriver(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        bool positive,
        Func<double> srvMoment,
        Func<uint, uint> locateArcanumGlyph,
        EffectRowTemplateMint blueprints,
        string pickPrompt,
        WidgetGearRoster roster,
        Action? shut)
    {
        _grimoire = grimoire;
        _positive = positive;
        _srvMoment = srvMoment;
        _locateArcanumGlyph = locateArcanumGlyph;
        _blueprints = blueprints;
        _pickPrompt = pickPrompt;
        _roster = roster;
        _details = arrangement.SeekElem(DetailsPhraseIdent) as WidgetPhrase;
        _shut = arrangement.SeekElem(ShutId) as WidgetBtn;
        _rosterScroller = arrangement.SeekElem(RosterScrollerIdent) as WidgetScroller;
        _detailsScroller = arrangement.SeekElem(DetailsScrollerIdent) as WidgetScroller;
        _shut?.OnClick = shut;
        _roster.Columns = 1;
        _roster.ChamberWidth = blueprints.Width;
        _roster.ChamberHeight = blueprints.Height;
        _rosterScroller?.Model = _roster.Scroll;
        _detailsArrangement = ConfigureDetails();
        _grimoire.EnchantmentsChanged += Rebuild;
        Rebuild();
    }

    public static EffectsWidgetDriver? Bind(
        ImportedArrangement arrangement,
        Grimoire grimoire,
        bool positive,
        Func<double> srvMoment,
        Func<uint, (uint Texture, int Width, int Height)> spriteLocate,
        Func<uint, uint> locateArcanumGlyph,
        EffectRowTemplateMint blueprints,
        string pickPrompt,
        Action? shut = null)
    {
        WidgetElem? hub = arrangement.SeekElem(RosterIdent);
        if (hub is null) return null;
        WidgetGearRoster roster;
        if (hub is WidgetGearRoster gearRoster)
            roster = gearRoster;
        else
        {
            roster = new WidgetGearRoster(spriteLocate)
            {
                Width = hub.Width,
                Height = hub.Height,
                Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right | MooringRims.Bottom,
            };
            hub.AddChild(roster);
            roster.GrabLatestMooringBaseline();
        }
        return new EffectsWidgetDriver(
            arrangement,
            grimoire,
            positive,
            srvMoment,
            locateArcanumGlyph,
            blueprints,
            pickPrompt,
            roster,
            shut);
    }

    public void Tick()
    {
        double instant = _srvMoment();
        if (!double.IsFinite(instant)) return;
        if (double.IsFinite(_previousIntervalRefresh)
            && instant >= _previousIntervalRefresh
            && instant - _previousIntervalRefresh < 1.0)
            return;
        _previousIntervalRefresh = instant;
        foreach (LiveEnchantmentRow enchantment in ShownEnchantments())
            if (_ranks.TryGetValue(
                    enchantment.Identity,
                    out EffectRowTemplateMint.FxRank? rank))
                rank.Remaining = ComposeLeftover(enchantment, instant);
    }

    public void OnShown() => Rebuild();

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _grimoire.EnchantmentsChanged -= Rebuild;
        _shut?.OnClick = null;
        _rosterScroller?.Model = null;
        _detailsScroller?.Model = null;
    }

    private void Rebuild()
    {
        _previousIntervalRefresh = double.NaN;
        _ranks.Clear();
        LiveEnchantmentRow[] enchantments = [.. ShownEnchantments()];
        using (_roster.DeferArrangement())
        {
            _roster.Flush();
            foreach (LiveEnchantmentRow enchantment in enchantments)
            {
                _grimoire.TryFetchMetadata(enchantment.SpellId, out SpellMeta? metadata);
                uint persona = enchantment.Identity;
                var rank = _blueprints.Create(
                    enchantment.SpellId,
                    metadata is null ? 0u : _locateArcanumGlyph(enchantment.SpellId),
                    metadata?.Name ?? $"Spell {enchantment.SpellId}",
                    ComposeLeftover(enchantment, _srvMoment()));
                rank.Slot.Clicked = () => Select(enchantment.SpellId);
                _ranks[persona] = rank;
                _roster.AddItem(rank.Slot);
            }
        }
        if (ChosenArcanumIdent is uint chosen
            && !_ranks.Values.Any(row => row.Slot.ListingIdent == chosen))
            ChosenArcanumIdent = null;
        SynchronizePick();
        RefreshDetailsPhrase();
    }

    private IEnumerable<LiveEnchantmentRow> ShownEnchantments()
    {
        return _grimoire.EnchantmentsInFxCapture
                .Where(capture =>
                {
                    return !_grimoire.TryFetchMetadata(capture.SpellId, out SpellMeta metadata) ? false : metadata.IsBeneficial == _positive;
                })
                .OrderBy(capture => _grimoire.TryFetchMetadata(capture.SpellId, out SpellMeta metadata)
                    ? metadata.Name : capture.SpellId.ToString(CultureInfo.InvariantCulture),
                    StringComparer.OrdinalIgnoreCase);
    }

    private static string ComposeLeftover(LiveEnchantmentRow enchantment, double instant)
    {
        if (enchantment.Duration < 0) return string.Empty;
        double leftover = Math.Max(0, enchantment.StartTime + enchantment.Duration - instant);
        if (!double.IsFinite(leftover)) return "--:--";
        leftover = Math.Min(leftover, TimeSpan.MaxValue.TotalSeconds);
        TimeSpan moment = TimeSpan.FromSeconds(leftover);
        return moment.TotalHours >= 1
            ? $"{(int)moment.TotalHours}:{moment.Minutes:00}:{moment.Seconds:00}"
            : $"{moment.Minutes}:{moment.Seconds:00}";
    }

    private void Select(uint arcanumIdent)
    {
        ChosenArcanumIdent = ChosenArcanumIdent == arcanumIdent ? null : arcanumIdent;
        SynchronizePick();
        RefreshDetailsPhrase();
    }

    private void SynchronizePick()
    {
        foreach (EffectRowTemplateMint.FxRank rank in _ranks.Values)
            rank.Slot.AssignChosen(rank.Slot.ListingIdent == ChosenArcanumIdent);
    }

    private WidgetTextArrangementShelf<string>? ConfigureDetails()
    {
        if (_details is null) return null;
        _details.PreserveFinishOnArrangement = false;
        _details.WheelRollTurnedOn = true;
        _details.ClickThrough = false;
        _detailsScroller?.Model = _details.Scroll;
        var stash = new WidgetTextArrangementShelf<string>(
            _details,
            static (mark, val) => IndicatorSpecificsPhrase.Shape(mark, val),
            _pickPrompt,
            StringComparer.Ordinal);
        _details.StrokesSupplier = stash.Provider;
        return stash;
    }

    private void RefreshDetailsPhrase()
    {
        if (_detailsArrangement is null)
            return;

        string val = _pickPrompt;
        if (ChosenArcanumIdent is uint arcanumIdent
            && _grimoire.EngagedEnchantmentCapture.Any(
                capture => capture.SpellId == arcanumIdent)
            && _grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata))

            val = metadata.Name + "\n\n" + metadata.Description;
        _detailsArrangement.SetValue(val);
    }
}
