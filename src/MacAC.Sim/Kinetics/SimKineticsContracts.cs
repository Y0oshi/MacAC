using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

public readonly record struct SimKineticsHoldingCapture(
    int LandblockCount,
    int RetainedShadowRegistrationCount,
    int SpatialRootCount,
    int SpatialRemoteCount,
    int SpatialProjectileCount,
    int SetPositionOperationCount,
    int AwaitingSetPositionPreparationCount,
    int DeferredSetPositionCount,
    int PendingSetPositionHostAcknowledgementCount,
    int LostCellDeadlineCount,
    int LostCellDeadlineNodeCount,
    int LostCellDeadlineIndexCount,
    int ExpiredLostCellCount,
    int ExpiredLostCellIndexCount,
    int DeferredSetPositionBucketCount,
    int DeferredSetPositionBucketOrderCount,
    int UnboundDeferredSetPositionCellCount,
    int UnboundDeferredSetPositionCellOrderCount,
    int PreparedSetPositionMoverCount,
    int CollisionReportOwnerCount,
    int TrackedCollisionObjectCount,
    int CollisionReportReversePeerCount,
    int CollisionReportObserverCount,
    int PendingCollisionReportCount,
    int LeavingCollisionReportOwnerCount,
    int CollisionReportAdmissionBlockedOwnerCount,
    int PendingCollisionSetPositionDispatchCount,
    int PendingShadowSetPositionDispatchCount,
    bool IsCollisionReportDispatching,
    int CollisionPrefixQuiescenceCount,
    int PendingCollisionPrefixProjectionCount,
    int CollisionPrefixMutationCount,
    int CommittedCollisionPrefixMutationCount,
    int CollisionAdmissionCount,
    int CollisionGenerationCount,
    bool OwnsProductionDataCache,
    bool IsDisposed)
{
    public bool IsConverged
    {
        get
        {
            return IsDisposed
        && LandblockCount is 0
        && RetainedShadowRegistrationCount is 0
        && SpatialRootCount is 0
        && SpatialRemoteCount is 0
        && SpatialProjectileCount is 0
        && SetPositionOperationCount is 0
        && AwaitingSetPositionPreparationCount is 0
        && DeferredSetPositionCount is 0
        && PendingSetPositionHostAcknowledgementCount is 0
        && LostCellDeadlineCount is 0
        && LostCellDeadlineNodeCount is 0
        && LostCellDeadlineIndexCount is 0
        && ExpiredLostCellCount is 0
        && ExpiredLostCellIndexCount is 0
        && DeferredSetPositionBucketCount is 0
        && DeferredSetPositionBucketOrderCount is 0
        && UnboundDeferredSetPositionCellCount is 0
        && UnboundDeferredSetPositionCellOrderCount is 0
        && PreparedSetPositionMoverCount is 0
        && CollisionReportOwnerCount is 0
        && TrackedCollisionObjectCount is 0
        && CollisionReportReversePeerCount is 0
        && CollisionReportObserverCount is 0
        && PendingCollisionReportCount is 0
        && LeavingCollisionReportOwnerCount is 0
        && CollisionReportAdmissionBlockedOwnerCount is 0
        && PendingCollisionSetPositionDispatchCount is 0
        && PendingShadowSetPositionDispatchCount is 0
        && !IsCollisionReportDispatching
        && CollisionPrefixQuiescenceCount is 0
        && PendingCollisionPrefixProjectionCount is 0
        && CollisionPrefixMutationCount is 0
        && CommittedCollisionPrefixMutationCount is 0
        && CollisionAdmissionCount is 0
        && CollisionGenerationCount is 0
        && OwnsProductionDataCache;
        }
    }
}

public readonly record struct SimKineticsCellCommit(
    SimActorRecord Record,
    uint PreviousFullCellId,
    uint FullCellId,
    ulong SpatialAuthorityVersion);

public sealed record SimLandblockContactAssets(
    uint LandblockId,
    LandCanvas Terrain,
    IReadOnlyList<CellFacet> CellSurfaces,
    IReadOnlyList<PortalFace> PortalPlanes,
    float WorldOffsetX,
    float WorldOffsetY,
    uint CurrentCellId);

public sealed class SimContactIntake
{
    internal SimContactIntake(
        SimKineticsLedger holder,
        uint lbIdent,
        ulong gen,
        ulong earlierGen)
    {
        Owner = holder;
        LandblockId = lbIdent;
        Generation = gen;
        EarlierGen = earlierGen;
    }

    internal SimKineticsLedger Owner { get; }
    internal bool HoldingsReadied { get; set; }
    internal bool Completed { get; set; }
    public uint LandblockId { get; }
    public ulong Generation { get; }
    internal ulong EarlierGen { get; }
}

internal enum SimContactPrefixEditKind : byte
{
    Activation,
    Demotion,
    Withdrawal,
}

internal sealed class SimContactPrefixEdit
{
    internal required SimContactPrefixEditKind Kind { get; init; }
    internal required uint LbIdent { get; init; }
    internal required ulong EarlierGen { get; init; }
    internal required ulong MarkGen { get; init; }
    internal ulong InvalidatedGen { get; init; }
    internal required SimContactPrefixLullTicket Stillness
    { get; init; }
    internal SimContactIntake? Admission { get; init; }
    internal StagedLandblockContactEpoch? Prepared { get; init; }
    internal SimContactPrefixEditLeave Permission { get; set; }
    internal bool EngineAlterationSealed { get; set; }
    internal bool AbortAsked { get; set; }
    internal bool WasHoused { get; set; }
    internal bool Ready { get; set; }
}

public readonly record struct SimContactAck(
    uint LandblockId,
    ulong Generation,
    bool WasResident,
    bool Ready);

public readonly record struct SimContactEditResult(
    SimContactAck Acknowledgement,
    bool Completed)
{
    public uint LandblockId => Acknowledgement.LandblockId;
    public ulong Generation => Acknowledgement.Generation;
    public bool WasHoused => Acknowledgement.WasResident;
    public bool Ready => Acknowledgement.Ready;
}

public readonly record struct SimContactEpochCommit(
    SimContactAck Acknowledgement,
    uint[] DirtyRetainedOwnerIds,
    bool EngineCommitted,
    bool Completed)
{
    public bool Committed => Completed;
}

public readonly record struct SimContactEpochCommitted(
    uint LandblockId,
    ulong Generation,
    bool Ready);

internal readonly record struct SimContactHolderCaptureStep(
    bool Completed,
    bool Restarted,
    bool HasOwner,
    uint OwnerId);

internal readonly record struct SimContactStagingStep(
    bool Completed,
    int WorkUnits);

internal readonly record struct SimContactSealStep(
    bool Completed,
    bool Restarted,
    int WorkUnits);
