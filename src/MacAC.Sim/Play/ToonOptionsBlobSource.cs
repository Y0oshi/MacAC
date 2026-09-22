using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public readonly record struct ToonOptionsBlobEcho(
    uint Options1,
    uint Options2,
    IReadOnlyList<HotbarSlot> Shortcuts,
    IReadOnlyList<IReadOnlyList<uint>> FavoriteSpells,
    IReadOnlyDictionary<uint, uint> DesiredComponents,
    uint SpellbookFilters);

public static class ToonOptionsBlobSource
{
    private const int FavoriteTabs = 8;

    public static ToonOptionsBlobEcho Capture(SimToonLedger toon, HotbarStore shortcuts)
    {
        ArgumentNullException.ThrowIfNull(toon);
        ArgumentNullException.ThrowIfNull(shortcuts);

        var favorites = new IReadOnlyList<uint>[FavoriteTabs];
        for (int tab = 0; tab < favorites.Length; ++tab)
            favorites[tab] = toon.Spellbook.FetchFavorites(tab);

        return new ToonOptionsBlobEcho(
            toon.Options.Options1,
            toon.Options.Options2,
            shortcuts.Items,
            favorites,
            toon.Spellbook.DesiredComponents,
            toon.Spellbook.GrimoireFilters);
    }
}
