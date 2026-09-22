namespace MacAC.Client.Paging;

internal readonly record struct PagingRevealWindow(
    int NearRadius,
    int FarRadius);

internal readonly record struct RealmRevealReadinessCapture(
    uint DestinationCell,
    bool IsIndoor,
    bool IsUnhydratable,
    int RequiredRenderRadius,
    int RequiredNearRadius,
    bool IsRenderNeighborhoodReady,
    bool AreCompositeTexturesReady,
    bool IsCollisionReady)
{
    public bool HasDest => DestinationCell is not 0u;

    public bool IsPrimed
    {
        get
        {
            return HasDest
        && (IsUnhydratable
            || (IsRenderNeighborhoodReady
                && AreCompositeTexturesReady
                && IsCollisionReady));
        }
    }
}

internal sealed class RealmRevealReadinessBarrier(
    Func<PagingRevealWindow> revealWindow,
    Func<uint, int, int, bool> isRenderNeighborhoodReady,
    Func<uint, bool> isSpawnCellReady,
    Func<uint, int, bool> isTerrainNeighborhoodReady,
    Func<bool> areCompositeTexturesReady,
    Action<uint, int> prepareCompositeTextures,
    Action invalidateCompositeTextures,
    Func<uint, bool> isSpawnClaimUnhydratable)
{
    private readonly Func<PagingRevealWindow> _unveilPane = revealWindow
            ?? throw new ArgumentNullException(nameof(revealWindow));
    private readonly Func<uint, int, int, bool> _isRasterizeNeighborhoodPrimed = isRenderNeighborhoodReady
            ?? throw new ArgumentNullException(nameof(isRenderNeighborhoodReady));
    private readonly Func<uint, bool> _isSummonChamberPrimed = isSpawnCellReady
            ?? throw new ArgumentNullException(nameof(isSpawnCellReady));
    private readonly Func<uint, int, bool> _isLandNeighborhoodPrimed = isTerrainNeighborhoodReady
            ?? throw new ArgumentNullException(nameof(isTerrainNeighborhoodReady));
    private readonly Func<bool> _areCompoundTexturesPrimed = areCompositeTexturesReady
            ?? throw new ArgumentNullException(nameof(areCompositeTexturesReady));
    private readonly Action<uint, int> _readyCompoundTextures = prepareCompositeTextures
            ?? throw new ArgumentNullException(nameof(prepareCompositeTextures));
    private readonly Action _staleCompoundTextures = invalidateCompositeTextures
            ?? throw new ArgumentNullException(nameof(invalidateCompositeTextures));
    private readonly Func<uint, bool> _isSummonClaimUnhydratable = isSpawnClaimUnhydratable
            ?? throw new ArgumentNullException(nameof(isSpawnClaimUnhydratable));

    public void Begin() => _staleCompoundTextures();

    public void Prepare(uint destChamber)
    {
        if (destChamber is 0 || _isSummonClaimUnhydratable(destChamber))
            return;

        var needed = NeededPane(destChamber);
        if (_isRasterizeNeighborhoodPrimed(
                destChamber,
                needed.NearRadius,
                needed.NearRadius))

            _readyCompoundTextures(destChamber, needed.NearRadius);
    }

    public bool IsReady(uint destChamber)
        => Evaluate(destChamber).IsPrimed;

    public RealmRevealReadinessCapture Evaluate(uint destChamber)
    {
        if (destChamber is 0)
            return default;

        bool isInside = IsInside(destChamber);
        var needed = NeededPane(destChamber);
        if (_isSummonClaimUnhydratable(destChamber))
        {
            return new RealmRevealReadinessCapture(
                destChamber,
                isInside,
                IsUnhydratable: true,
                needed.FarRadius,
                needed.NearRadius,
                IsRenderNeighborhoodReady: false,
                AreCompositeTexturesReady: false,
                IsCollisionReady: false);
        }

        bool rasterizePrimed = _isRasterizeNeighborhoodPrimed(
            destChamber,
            needed.NearRadius,
            needed.FarRadius);
        bool compositesPrimed = rasterizePrimed && _areCompoundTexturesPrimed();
        bool impactPrimed = rasterizePrimed && compositesPrimed
            && (isInside
                ? _isSummonChamberPrimed(destChamber)
                : _isLandNeighborhoodPrimed(
                    destChamber,
                    needed.FarRadius));

        return new RealmRevealReadinessCapture(
            destChamber,
            isInside,
            IsUnhydratable: false,
            needed.FarRadius,
            needed.NearRadius,
            rasterizePrimed,
            compositesPrimed,
            impactPrimed);
    }

    internal PagingRevealWindow NeededPane(uint destChamber)
    {
        if (IsInside(destChamber))
            return new PagingRevealWindow(0, 0);

        var pane = _unveilPane();
        int faraway = Math.Max(0, pane.FarRadius);
        int nearby = Math.Clamp(pane.NearRadius, 0, faraway);
        return new PagingRevealWindow(nearby, faraway);
    }

    internal int NeededRasterizeRadius(uint destChamber) =>
        NeededPane(destChamber).FarRadius;

    private static bool IsInside(uint chamberIdent) => (chamberIdent & 0xFFFFu) >= 0x0100u;
}
