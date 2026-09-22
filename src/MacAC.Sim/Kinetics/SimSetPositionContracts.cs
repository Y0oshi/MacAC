using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MacAC.Assets;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Play;
using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal enum SimSetPositionOperationKind
{
    InitialLogin,
    LocalAuthoritative,
    RemoteAuthoritative,
    ProjectileAuthoritative,
}

internal enum SimSetPositionStatus
{
    Rejected,
    DeferredCell,
    CommittedHostAcknowledgementPending,
    Committed,
    Cancelled,
}

internal enum SimActorPlacementStage
{
    AwaitingPreparation,
    AwaitingWithdrawalAcknowledgement,
    AwaitingCell,
    QuiescenceHeld,
    AwaitingFinalShadowPreparation,
    AwaitingCommitAcknowledgement,
    CancelledAwaitingAcknowledgement,
}

internal enum SimActorPlacementStagingKind : byte
{
    LegacyDirect,
    AuthoredMover,
}

internal readonly record struct SimActorPlacementTicket(
    ulong SessionLifetimeVersion,
    SimActorKey Entity,
    ulong PositionAuthorityVersion,
    ulong OperationId,
    SimActorPlacementStagingKind PreparationKind)
{
    internal bool IsValid
    {
        get
        {
            return OperationId is not 0UL
        && Entity.LocalEntityId is not 0u
        && PositionAuthorityVersion is not 0UL;
        }
    }
}

public enum SimPlacementMirrorKind
{
    Withdraw,
    Place,
    Discard,
    ExecutorCompleted,
    WithdrawalRestored,
}

public readonly record struct SimPortalPlacementAuthority(
    bool Present,
    long RevealGeneration,
    ushort TeleportSequence,
    SimRealmHarborMirrorTicket Projection)
{
    internal bool IsVacant
    {
        get
        {
            return !Present
        && RevealGeneration is 0
        && TeleportSequence is 0
        && Projection == default;
        }
    }

    internal bool IsValid
    {
        get
        {
            return Present
        && RevealGeneration is not 0
        && Projection.IsValid
        && Projection.Generation == RevealGeneration;
        }
    }
}

internal readonly record struct SimSetPositionDirective(
    KineticSetPositionRequest Physics,
    SimSetPositionOperationKind Kind,
    double GameTime,
    ulong ExpectedVelocityAuthorityVersion,
    float ShadowWorldOffsetX = 0f,
    float ShadowWorldOffsetY = 0f,
    SimPortalPlacementAuthority Portal = default);

internal readonly record struct SimContactEpochAuthority(
    uint LandblockId,
    ulong Generation);

internal readonly record struct SimContactPrefixLullTicket(
    ulong SessionLifetimeVersion,
    uint LandblockPrefix,
    ulong CollisionGeneration,
    ulong OperationId)
{
    internal bool IsValid
    {
        get
        {
            return (LandblockPrefix & 0xFFFFu) is 0u
        && CollisionGeneration is not 0UL
        && OperationId is not 0UL;
        }
    }
}

internal readonly record struct SimContactPrefixEditLeave(
    SimContactPrefixLullTicket Quiescence,
    ImmutableArray<SimPlacementMirrorTicket> Withdrawals)
{
    internal bool IsValid => Quiescence.IsValid;
}

internal readonly record struct SimContactEvaluationAuthority(
    ulong CollisionWorldAuthority,
    ulong ShadowWorldAuthority,
    ClientThingChart? ObjectTable,
    ulong ObjectTableBindingAuthority,
    ulong ObjectTableAuthority,
    ImmutableArray<SimContactEpochAuthority> Generations)
{
    internal bool IsValid
    {
        get
        {
            return CollisionWorldAuthority is not 0UL
        && !Generations.IsDefault;
        }
    }
}

internal readonly record struct SimIdleSetPositionEvaluation(
    SimActorPlacementTicket Placement,
    SimSetPositionDirective Command,
    PlaceOutcome Result,
    SimContactEvaluationAuthority CollisionAuthority)
{
    internal bool IsValid
    {
        get
        {
            return Placement.IsValid
        && CollisionAuthority.IsValid;
        }
    }
}

internal enum SimIdleSetPositionCommitStatus : byte
{
    None,
    AwaitingFinalShadowPreparation,
    Committed,
    DeferredCell,
    RejectedPlacement,
    RejectedAuthority,
}

internal sealed class StagedIdleSetPositionCommit
{
    internal required SimIdleSetPositionEvaluation Evaluation
    { get; init; }
    internal required SimActorKey Entity { get; init; }
    internal required ulong OpIdent { get; init; }
    internal required ulong AnticipatedProjSeries { get; init; }
    internal required ProxyRegistry.BakedPlaceProxyCommit?
        Shadow
    { get; init; }
    internal required SimContactNoticesLedger
        .StagedSetPositionContactBatch? Collision
    { get; init; }
    internal required SimPlacementMirrorCapture Projection
    { get; init; }
    internal required SortedDictionary<ulong, SimPlacementMirrorCapture>?
        QueuedProj
    { get; init; }
    internal required ulong PostponedImpactGen { get; init; }
    internal required bool PostponedImpactGenPrimed { get; init; }
    internal required List<SimActorKey>? PostponedBin { get; init; }
    internal required bool PostponedBinIsNew { get; init; }
}

internal sealed class StagedIdleArmingFinalCommit
{
    internal required SimActorKey Entity { get; init; }
    internal required ulong OpIdent { get; init; }
    internal required ulong AnticipatedProjSeries { get; init; }
    internal required ProxyRegistry.BakedPlaceProxyCommit
        Shadow
    { get; init; }
    internal required SimPlacementMirrorCapture Projection
    { get; init; }
    internal required SortedDictionary<ulong, SimPlacementMirrorCapture>
        QueuedProj
    { get; init; }
}

internal readonly record struct SimIdleSetPositionCommitStub(
    SimIdleSetPositionCommitStatus Status,
    SimActorKey Entity,
    ulong OperationId,
    SimPlacementMirrorCapture Projection,
    SimContactNoticesLedger.SetPositionContactBatchStub Collision,
    ProxyRegistry.PlaceProxyCommitReceipt Shadow,
    SimContactEvaluationAuthority CollisionAuthority,
    ulong SourceVectorAuthorityVersion,
    bool HitGround,
    bool LeaveGround)
{
    internal bool IsSealed
    {
        get
        {
            return Status
        is SimIdleSetPositionCommitStatus.Committed;
        }
    }
}

public readonly record struct SimPlacementMirrorTicket(
    ulong Sequence,
    ulong Revision,
    SimActorKey Entity,
    ulong PositionAuthorityVersion,
    ulong SpatialAuthorityVersion,
    ulong PlacementCommitVersion,
    ulong SessionLifetimeVersion,
    uint ExactCellId,
    ulong CollisionGeneration,
    SimPortalPlacementAuthority Portal)
{
    internal bool IsValid
    {
        get
        {
            return Sequence is not 0
        && Entity.LocalEntityId is not 0u
        && PositionAuthorityVersion is not 0UL;
        }
    }
}

public readonly record struct SimPlacementMirrorCapture(
    SimPlacementMirrorTicket Token,
    SimPlacementMirrorKind Kind,
    Vector3 WorldPosition,
    Quaternion Orientation,
    Vector3 CellLocalPosition,
    bool InContact,
    bool OnWalkable);

internal readonly record struct SimSetPositionUpshot(
    SimSetPositionStatus Status,
    PlaceError Error,
    KineticResidenceVerdict Residence,
    uint ExactCellId,
    SimPlacementMirrorTicket Projection)
{
    internal bool Accepted => Error == PlaceError.Ok;
}

internal readonly record struct SimPlacementAbortStub(
    SimPlacementMirrorCapture Projection)
{
    internal bool IsValid
    {
        get
        {
            return Projection.Kind
            is SimPlacementMirrorKind.Discard
        && Projection.Token.IsValid;
        }
    }
}

internal readonly record struct SimSetPositionHoldingCapture(
    int ActiveOperationCount,
    int AwaitingPreparationCount,
    int DeferredCellCount,
    int PendingProjectionAcknowledgementCount,
    int LostDeadlineCount,
    int LostDeadlineNodeCount,
    int LostDeadlineIndexCount,
    int ExpiredLostCellCount,
    int ExpiredLostCellIndexCount,
    int DeferredBucketCount,
    int DeferredBucketOrderCount,
    int UnboundDeferredCellCount,
    int UnboundDeferredCellOrderCount,
    int PreparedMoverCount,
    int MoverPreparationAuthorityCount,
    int PlacementCompletionWatchCount,
    int AcknowledgedPlacementCompletionCount,
    int CollisionPrefixQuiescenceCount,
    int PendingQuiescenceProjectionCount,
    int PooledOperationCount,
    int ParkedAwaitingSetupCollisionCount = 0,
    int ParkedAwaitingWorldFrameCount = 0)
{
    internal int ShelvedStanceTally =>
        ParkedAwaitingSetupCollisionCount + ParkedAwaitingWorldFrameCount;

    internal bool IndexesConsistent
    {
        get
        {
            return LostDeadlineCount == LostDeadlineNodeCount
        && LostDeadlineCount == LostDeadlineIndexCount
        && ExpiredLostCellCount == ExpiredLostCellIndexCount
        && DeferredBucketCount == DeferredBucketOrderCount
        && UnboundDeferredCellCount == UnboundDeferredCellOrderCount
        && MoverPreparationAuthorityCount <= ActiveOperationCount;
        }
    }

    internal bool IsConverged
    {
        get
        {
            return ActiveOperationCount is 0
        && AwaitingPreparationCount is 0
        && DeferredCellCount is 0
        && PendingProjectionAcknowledgementCount is 0
        && LostDeadlineCount is 0
        && LostDeadlineNodeCount is 0
        && LostDeadlineIndexCount is 0
        && ExpiredLostCellCount is 0
        && ExpiredLostCellIndexCount is 0
        && DeferredBucketCount is 0
        && DeferredBucketOrderCount is 0
        && UnboundDeferredCellCount is 0
        && UnboundDeferredCellOrderCount is 0
        && PreparedMoverCount is 0
        && MoverPreparationAuthorityCount is 0
        && PlacementCompletionWatchCount is 0
        && AcknowledgedPlacementCompletionCount is 0
        && CollisionPrefixQuiescenceCount is 0
        && PendingQuiescenceProjectionCount is 0;
        }
    }
}
