using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell.Panels;

public sealed class GearCooldownWidgetDriver
{
    private readonly Grimoire _grimoire;
    private readonly ClientThingChart _objects;
    private readonly Func<double> _latestMoment;
    private readonly List<WidgetGearRoster> _rosters = [];
    private readonly Dictionary<uint, int> _hopByGearIdent = [];

    private GearCooldownWidgetDriver(
        Grimoire grimoire,
        ClientThingChart objects,
        Func<double> latestMoment)
    {
        _grimoire = grimoire;
        _objects = objects;
        _latestMoment = latestMoment;
    }

    public static GearCooldownWidgetDriver Bind(
        WidgetElem trunk,
        Grimoire grimoire,
        ClientThingChart objects,
        Func<double> latestMoment,
        GearCooldownAssets holdings)
    {
        ArgumentNullException.ThrowIfNull(trunk);
        ArgumentNullException.ThrowIfNull(grimoire);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(latestMoment);

        GearCooldownWidgetDriver driver = new GearCooldownWidgetDriver(
            grimoire,
            objects,
            latestMoment);
        driver.Configure(trunk, holdings.Sprites);
        driver.Tick();
        return driver;
    }

    public void Tick()
    {
        _hopByGearIdent.Clear();
        if (!_grimoire.HasCooldownEnchantments)
            return;

        double instant = _latestMoment();

        foreach (WidgetGearRoster roster in _rosters)
        {
            if (!IsEffectivelyShown(roster))
                continue;

            for (int ordinal = 0; ordinal < roster.FetchCountWIDGETGearList(); ++ordinal)
            {
                var chamber = roster.GetItem(ordinal);
                if (chamber is null)
                    continue;
                uint gearIdent = chamber.GearIdent;
                if (gearIdent is 0u || _hopByGearIdent.ContainsKey(gearIdent))
                    continue;

                int hop = 0;
                var gear = _objects.Get(gearIdent);
                if (gear?.CooldownId is { } cooldownIdent
                    && gear.CooldownDuration is { } interval
                    && _grimoire.OnCooldown(
                        cooldownIdent,
                        instant,
                        out double leftover))
                    hop = CooldownReadout.FetchTopLayerHop(
                        interval,
                        leftover);

                _hopByGearIdent.Add(gearIdent, hop);
            }
        }
    }

    internal static bool IsEffectivelyShown(WidgetElem elem)
    {
        ArgumentNullException.ThrowIfNull(elem);
        for (WidgetElem? latest = elem; latest is not null; latest = latest.Ancestor)
        {
            if (!latest.Visible)
                return false;
        }
        return true;
    }

    internal int FetchTopLayerHop(uint gearIdent)
        => _hopByGearIdent.TryGetValue(gearIdent, out int hop) ? hop : 0;

    private void Configure(WidgetElem elem, IReadOnlyList<uint> sprites)
    {
        if (elem is WidgetGearRoster roster)
        {
            _rosters.Add(roster);
            roster.CooldownSprites = sprites;
            roster.CooldownHopSupplier = FetchTopLayerHop;
        }

        foreach (WidgetElem descendant in elem.Children)
            Configure(descendant, sprites);
    }
}
