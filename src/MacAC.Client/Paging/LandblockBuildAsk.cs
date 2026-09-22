namespace MacAC.Client.Paging;

public readonly record struct LandblockAssembleOrigin
{
    public LandblockAssembleOrigin(int middleX, int middleY)
    {
        CenterX = middleX;
        CenterY = middleY;
        IsSpecified = true;
    }

    public int CenterX { get; }
    public int CenterY { get; }

    public bool IsSpecified { get; }
}

public readonly record struct LandblockBuildAsk(
    uint LandblockId,
    LandblockFlowJobFlavor Kind,
    ulong Generation,
    LandblockAssembleOrigin Origin);
