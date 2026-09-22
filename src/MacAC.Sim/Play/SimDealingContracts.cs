using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public enum SimQueuedDealingKind
{
    Activate,
    Use,
    Pickup,
}

public readonly record struct SimDealingIdentity(uint ServerGuid, uint? LocalEntityId, ClientThing? ClientObject);

public readonly record struct SimQueuedDealing(SimQueuedDealingKind Kind, SimDealingIdentity Identity);

public readonly record struct SimDealingApproachTicket(ulong ControllerLifetime, ulong ApproachGeneration)
{
    public bool IsValid => ControllerLifetime is not 0u && ApproachGeneration is not 0u;
}

public readonly record struct SimPendingGrab(
    ulong Token,
    uint ServerGuid,
    uint LocalEntityId,
    uint DestinationContainerId,
    int Placement,
    ulong PendingPlacementToken,
    SimDealingApproachTicket ApproachToken);

public readonly record struct SimPendingUse(
    ulong Token,
    uint ServerGuid,
    bool OwnedByPlayer,
    bool Useable,
    ItemUseHold? Reservation,
    SimDealingApproachTicket ApproachToken);

public readonly record struct SimAssessResponseAcceptance(bool Accepted, bool FirstResponse);

public readonly record struct SimItemUseFinish(long Revision, uint SourceObjectId, uint TargetObjectId, uint WeenieError)
{
    public bool IsSuccess => Revision is not 0 && WeenieError is 0u;
}

public enum SimDealingDispatchResult
{
    Rejected,
    NotInWorld,
    NotUseable,
    Dispatched,
}

public readonly record struct SimDealingTransactionCapture(
    bool IsDisposed,
    long Revision,
    uint LastUseSourceId,
    uint LastUseTargetId,
    uint AwaitingAppraisalId,
    uint CurrentAppraisalId,
    int OutboundCount,
    bool HasPendingPickup,
    ulong PendingPickupToken,
    long DispatchFailureCount,
    bool HasPendingUse = false,
    ulong PendingUseToken = 0u,
    bool AwaitingItemUseCompletion = false,
    SimItemUseFinish LastItemUseCompletion = default)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed
        && LastUseSourceId is 0u && LastUseTargetId is 0u
        && AwaitingAppraisalId is 0u && CurrentAppraisalId is 0u
        && OutboundCount is 0 && !HasPendingPickup && !HasPendingUse
        && !AwaitingItemUseCompletion && LastItemUseCompletion.Revision is 0;
        }
    }
}

public interface ISimDealingTransport
{
    bool IsInRealm { get; }

    bool TryTransmitUse(uint srvOid, out uint series);

    bool TryTransmitLift(uint gearOid, uint destVesselIdent, int stance, out uint series);
}
