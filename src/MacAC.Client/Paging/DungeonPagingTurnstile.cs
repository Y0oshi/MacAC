namespace MacAC.Client.Paging;

public readonly record struct DungeonTurnstileResult(bool InsideDungeon, uint? ObserverLandblockKey);

public static class DungeonPagingTurnstile
{
    public static DungeonTurnstileResult Compute(
        bool isWarpGrip, bool currChamberIsSealedDungeon, uint currChamberIdent)
    {
        if (isWarpGrip)
            return new DungeonTurnstileResult(false, null);

        return currChamberIsSealedDungeon ? new DungeonTurnstileResult(true, currChamberIdent >> 16) : new DungeonTurnstileResult(false, null);
    }
}
