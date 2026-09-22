namespace MacAC.Client.Paging;

internal readonly record struct WarpLandblockTransition(
    uint SourceLandblockId,
    uint DestinationLandblockId,
    uint StreamingCenterLandblockId)
{
    public bool CrossesLb => SourceLandblockId != DestinationLandblockId;
    public bool EditsPagingMiddle =>
        StreamingCenterLandblockId != DestinationLandblockId;

    public static WarpLandblockTransition Classify(
        uint srcChamberIdent,
        uint destChamberIdent,
        uint latestPagingMiddleLbIdent)
    {
        uint srcLbIdent = srcChamberIdent is not 0
            ? StandardizeLbIdent(srcChamberIdent)
            : StandardizeLbIdent(latestPagingMiddleLbIdent);

        return new WarpLandblockTransition(
            srcLbIdent,
            StandardizeLbIdent(destChamberIdent),
            StandardizeLbIdent(latestPagingMiddleLbIdent));
    }

    private static uint StandardizeLbIdent(uint chamberOrLbIdent)
        => (chamberOrLbIdent & 0xFFFF0000u) | 0xFFFFu;
}
