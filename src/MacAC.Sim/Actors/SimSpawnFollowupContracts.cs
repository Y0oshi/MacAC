using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal enum SimSpawnExecutionStatus : byte
{
    Completed,
    // Initial authored placement not yet acknowledged; retry later
    PendingPlacement,
    AwaitingContinuationPlacement,
    RejectedToken,
    RejectedAuthority,
}

internal readonly record struct SimSpawnExecutionInputs(
    bool UsePositionFromServer,
    float PlayerDistance);

internal enum SimSpawnExecutedActionKind : byte
{
    InitialAdoption,
    TeleportHookRequest,
    DeferredChildReplay,
    ParentRelationReplay,
    PreTailDescriptionAdaptation,
    ObjDesc,
    CreateParent,
    Parent,
    Pickup,
    Position,
    Movement,
    State,
    Vector,
    WeenieDescription,
    ResidentCellCleanup,
}

internal enum SimHeldChildRerunUpshot : byte
{
    Registered,
    ReDeferred,
    Rejected,
}

internal enum SimAnchorRelationUpshot : byte
{
    Applied,
    DiscardedStaleParent,
    DeferredAwaitingParent,
    Rejected,
}

internal enum SimResidentCellCleanupVerdict : byte
{
    ResidentUnmarked,
    DeferredUnderLostCellOwnership,
    CelllessNoWeenieMarkUnreachable,
}

internal readonly record struct SimSpawnExecutedAction(
    SimSpawnExecutedActionKind Kind,
    ulong Sequence,
    int Stage,
    SimSovereignPositionVerdict? PositionDisposition,
    SimWarpHookPhase HookPhase,
    SimHeldChildRerunUpshot? DeferredChildOutcome = null,
    SimResidentCellCleanupVerdict? ResidentCellCleanupDisposition = null,
    SimPositionConstrainPhase ConstrainPhase = SimPositionConstrainPhase.None,
    bool StopInterpolating = false,
    bool ZeroVelocity = false,
    bool PreserveHeading = false,
    bool SendPositionImmediately = false,
    bool UnparentBeforeRouting = false,
    SimAnchorRelationUpshot? ParentRelationOutcome = null);

internal readonly record struct SimSpawnExecutionStub(
    SimActorKey Entity,
    uint FullCellId,
    SimWarpHookPhase TeleportHookPhase,
    ImmutableArray<SimSpawnExecutedAction> Trace,
    int ReplayedDeferredChildCount);

public enum SimSpawnWarpHookPhase : byte
{
    None,
    BeforePositionOperation,
    AfterPositionOperation,
    AfterEnterWorld,
}

public enum SimSpawnPositionVerdict : byte
{
    RejectedAuthority,
    RejectedData,
    AwaitFreshPosition,
    NoPositionOperation,
    Interpolate,
    SetPosition,
    SetPositionSimple,
}

public enum SimSpawnPositionConstrainPhase : byte
{
    None,
    BeforePositionOperation,
    AfterPositionOperation,
}

public readonly record struct SimSpawnPositionRouteFact(
    ulong Sequence,
    SimSpawnPositionVerdict Disposition,
    SimSpawnWarpHookPhase HookPhase,
    SimSpawnPositionConstrainPhase ConstrainPhase,
    bool StopInterpolating,
    bool ZeroVelocity,
    bool PreserveHeading,
    bool SendPositionImmediately);

public readonly record struct SimSpawnPlacementFinish(
    SimActorKey Entity,
    uint FullCellId,
    SimSpawnWarpHookPhase TeleportHookPhase,
    ImmutableArray<SimSpawnPositionRouteFact> PositionRouteFacts,
    int ReplayedDeferredChildCount);
