namespace MacAC.Client.Paging;

public enum LandblockFlowTier
{
    Far,
    Near,
}

public enum LandblockFlowJobFlavor
{
    LoadFar,
    LoadNear,
    /// <summary>Read LandBlockInfo + scenery only - terrain already loaded for this LB.</summary>
    PromoteToNear,
}
