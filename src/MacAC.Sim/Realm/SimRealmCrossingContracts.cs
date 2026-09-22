namespace MacAC.Sim.Realm;

public readonly record struct SimRealmCrossingHoldingCapture(
    int BufferedTeleportDestinationCount,
    int PendingTeleportStartCount,
    int ActiveTeleportCount,
    int AcceptedTeleportDestinationCount,
    int ActiveRevealCount,
    int PendingDestinationReadinessCount,
    int HostProjectionCount,
    int PendingHostAcknowledgementCount,
    int ActiveLogoutCount = 0)
{
    public bool IsSessionIdle
    {
        get
        {
            return (BufferedTeleportDestinationCount
            | PendingTeleportStartCount
            | ActiveTeleportCount
            | AcceptedTeleportDestinationCount
            | ActiveRevealCount
            | PendingDestinationReadinessCount
            | HostProjectionCount
            | PendingHostAcknowledgementCount
            | ActiveLogoutCount) is 0;
        }
    }
}

public enum SimLogoutStage
{
    None,
    Requested,
    PresentationActive,
    Confirmed,
}
