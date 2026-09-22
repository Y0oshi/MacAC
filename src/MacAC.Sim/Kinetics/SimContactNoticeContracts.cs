using System.Collections.Immutable;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal enum SimContactNoticeKind
{
    ObjectCollision,
    ObjectCollisionEnd,
    EnvironmentCollision,
}

internal enum SetPositionContactBatchDispatchStatus : byte
{
    RejectedReceipt,
    Displaced,
    Completed,
}

internal readonly record struct SetPositionContactBatchDispatchResult(
    SetPositionContactBatchDispatchStatus Status,
    bool Reported);

internal readonly record struct SimContactNotice(
    ulong Sequence,
    SimContactNoticeKind Kind,
    SimActorKey Recipient,
    uint RecipientServerGuid,
    SimActorKey? Other,
    uint? OtherServerGuid,
    bool RecipientWasInContact,
    bool OtherWasInContact);

internal interface ISimContactNoticeWatcher
{
    void OnImpactDossier(in SimContactNotice dossier);
}

internal readonly record struct SimContactNoticesHoldingCapture(
    int OwnerCount,
    int TrackedObjectCount,
    int ReversePeerCount,
    int ObserverCount,
    int PendingReportCount,
    int LeavingOwnerCount,
    int AdmissionBlockedOwnerCount,
    int PendingSetPositionDispatchCount,
    bool IsDispatching,
    long DispatchFailureCount,
    bool IsDisposed)
{
    internal bool IsConverged
    {
        get
        {
            return IsDisposed
        && OwnerCount is 0
        && TrackedObjectCount is 0
        && ReversePeerCount is 0
        && ObserverCount is 0
        && PendingReportCount is 0
        && LeavingOwnerCount is 0
        && AdmissionBlockedOwnerCount is 0
        && PendingSetPositionDispatchCount is 0
        && !IsDispatching;
        }
    }
}
