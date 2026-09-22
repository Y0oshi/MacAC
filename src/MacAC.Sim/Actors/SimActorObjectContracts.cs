using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Play;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

public readonly record struct SimActorRegistrationResult(
    IncomingBuildOutcome Inbound,
    SimActorRecord? Canonical,
    bool LogicalRegistrationCreated,
    bool ReplacedExistingGeneration,
    Exception? PriorGenerationCleanupFailure = null,
    bool DeferredForParent = false);

public readonly record struct SimActorObjectHoldingCapture(
    int ActiveEntityCount,
    int TeardownEntityCount,
    int ClaimedLocalIdCount,
    int AcceptedSnapshotCount,
    int UnresolvedParentRelationCount,
    int DeferredParentCreateCount,
    int StagedParentRelationCount,
    int RecoveryParentRelationCount,
    int CommittedParentRelationCount,
    int ObjectCount,
    int ContainerCount,
    int ContainerProjectionCount,
    int EquipmentOwnerCount,
    int PendingMoveCount,
    int InitialCreateResidenceLeaseCount,
    int InitialCreateExecutorProgressCount,
    int StreamSubscriberCount,
    int PlacementStreamSubscriberCount,
    long StreamDispatchFailureCount,
    bool HasLastStreamDispatchFailure,
    int PendingDispatchCount,
    bool IsDispatching,
    bool IsSessionClearInProgress,
    bool IsDisposed,
    int DeferredAcceptedRelationCount = 0,
    long ReplayFailureCount = 0,
    bool HasLastReplayFailure = false,
    int PendingCompletionReceiptCount = 0,
    int LocalPlayerFirstEntryActiveCount = 0,
    int RemoteFirstEntryActiveCount = 0,
    int FirstEntryDrivePendingCount = 0,
    int AcceptedPositionDrivePendingCount = 0,
    int RemotePlacementDrivePendingCount = 0)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed
        && ActiveEntityCount is 0
        && TeardownEntityCount is 0
        && ClaimedLocalIdCount is 0
        && AcceptedSnapshotCount is 0
        && UnresolvedParentRelationCount is 0
        && DeferredParentCreateCount is 0
        && DeferredAcceptedRelationCount is 0
        && StagedParentRelationCount is 0
        && RecoveryParentRelationCount is 0
        && CommittedParentRelationCount is 0
        && ObjectCount is 0
        && ContainerCount is 0
        && ContainerProjectionCount is 0
        && EquipmentOwnerCount is 0
        && PendingMoveCount is 0
        && InitialCreateResidenceLeaseCount is 0
        && InitialCreateExecutorProgressCount is 0
        && PendingCompletionReceiptCount is 0
        && LocalPlayerFirstEntryActiveCount is 0
        && RemoteFirstEntryActiveCount is 0
        && FirstEntryDrivePendingCount is 0
        && AcceptedPositionDrivePendingCount is 0
        && RemotePlacementDrivePendingCount is 0
        && StreamSubscriberCount is 0
        && PlacementStreamSubscriberCount is 0
        && PendingDispatchCount is 0
        && !IsDispatching
        && !IsSessionClearInProgress;
        }
    }
}

public sealed class SimActorDeleteAcceptance
{
    internal SimActorDeleteAcceptance(
        SimActorObjectLifetime holder,
        ObjectDeletion.Parsed erase,
        SimActorRecord? retiredCanon,
        bool dropKeptObject)
    {
        Owner = holder;
        Delete = erase;
        RetiredCanon = retiredCanon;
        DropKeptObject = dropKeptObject;
    }

    internal SimActorObjectLifetime Owner { get; }
    internal bool Completed { get; set; }
    public ObjectDeletion.Parsed Delete { get; }
    public SimActorRecord? RetiredCanon { get; }
    public bool DropKeptObject { get; }
}
