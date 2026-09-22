namespace MacAC.Mechanics.Gear;

public readonly record struct StackMergeCandidate(
    uint ObjectId,
    uint WeenieClassId,
    int StackSize,
    int MaxStackSize,
    int TradeState);

public readonly record struct StackMergeRoute(uint SourceObjectId, uint TargetObjectId, uint Amount);

/// <summary>Decides whether and how much of one stack drops onto another.</summary>
public static class StackMergeRouter
{
    private const int InBarter = 1;

    public static StackMergeRoute? Plan(in StackMergeCandidate src, in StackMergeCandidate mark, bool primedForSatchelReq, int askedQuantity)
    {
        bool viable = primedForSatchelReq
            && src.ObjectId is not 0 && mark.ObjectId is not 0 && src.ObjectId != mark.ObjectId
            && src.MaxStackSize > 1 && mark.MaxStackSize > 1
            && src.TradeState != InBarter && mark.TradeState != InBarter
            && src.WeenieClassId == mark.WeenieClassId;
        if (!viable)
            return null;

        int hall = mark.MaxStackSize - Math.Max(1, mark.StackSize);
        if (hall <= 0)
            return null;

        int have = Math.Max(1, src.StackSize);
        int want = askedQuantity > 0 ? Math.Min(askedQuantity, have) : have;
        uint quantity = (uint)Math.Min(want, hall);
        return quantity is 0 ? null : new StackMergeRoute(src.ObjectId, mark.ObjectId, quantity);
    }
}
