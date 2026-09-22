namespace MacAC.Client.Shell.Panels;

internal static class CanonFightingArrangement
{
    internal const int ShownFavoriteSockets = 18;
    internal const float FavoriteChamberWidth = 32f;

    internal static CanonPaneCycle.Options WithHorizontalRescale(
        ImportedArrangement arrangement, CanonPaneCycle.Options knobs)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(knobs);
        return arrangement.SeekElem(ArcanacastingWidgetDriver.FavoriteRosterIdent) is not WidgetGearRoster roster
            ? throw new InvalidOperationException("Combat layout has no favorite spell list")
            : (knobs with
            {
                Resizable = true,
                RescaleX = true,
                RescaleY = false,
                ResizableRims = RescaleRims.Left | RescaleRims.Right,
                MinWidth = arrangement.Root.Width - roster.Width + 9 * FavoriteChamberWidth,
                MaxWidth = float.MaxValue,
                ConstrainRescaleToAncestor = true,
            });
    }

    internal static float FitFavoriteSockets(
        ImportedArrangement arrangement,
        int visibleSlots = ShownFavoriteSockets,
        float cellWidth = FavoriteChamberWidth)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        if (visibleSlots < 1)
            throw new ArgumentOutOfRangeException(nameof(visibleSlots));
        if (!(cellWidth > 0f) || !float.IsFinite(cellWidth))
            throw new ArgumentOutOfRangeException(nameof(cellWidth));

        WidgetElem trunk = arrangement.Root;
        ImposeDescendantArrangement(trunk);
        if (arrangement.SeekElem(ArcanacastingWidgetDriver.FavoriteRosterIdent) is not WidgetGearRoster roster)
            throw new InvalidOperationException("Retail combat layout has no favorite spell list");

        float markViewRect = visibleSlots * cellWidth;
        trunk.Width = MathF.Max(0f, trunk.Width + markViewRect - roster.Width);
        ImposeDescendantArrangement(trunk);
        return trunk.Width;
    }

    private static void ImposeDescendantArrangement(WidgetElem ancestor)
    {
        foreach (WidgetElem descendant in ancestor.Children)
        {
            descendant.ImposeMooring(ancestor.Width, ancestor.Height);
            ImposeDescendantArrangement(descendant);
        }
    }
}
