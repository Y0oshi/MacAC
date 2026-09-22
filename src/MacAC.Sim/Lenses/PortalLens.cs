using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Realm;

namespace MacAC.Sim;

public enum SimPortalKind
{
    None,
    Login,
    Portal,
}

public readonly record struct SimWarpDestination(
    uint EntityGuid,
    ushort InstanceSequence,
    ushort PositionSequence,
    ushort TeleportSequence,
    ushort ForcePositionSequence,
    Locus Position)
{
    public uint CellId => Position.ObjCellId;
}

/// <summary>Whether the destination cell has enough streamed in to drop the player there.</summary>
public readonly record struct SimDestinationFitness(
    long Generation,
    uint DestinationCell,
    bool IsIndoor,
    bool IsUnhydratable,
    int RequiredRenderRadius,
    bool IsRenderNeighborhoodReady,
    bool AreCompositeTexturesReady,
    bool IsCollisionReady)
{
    public bool HasDestination => DestinationCell is not 0u;

    public bool IsReady
    {
        get
        {
            return HasDestination && (IsUnhydratable || (IsRenderNeighborhoodReady && AreCompositeTexturesReady && IsCollisionReady));
        }
    }
}

[Flags]
public enum SimRealmHarborAckStage
{
    None = 0,
    ProjectionRegistered = 1 << 0,
    SimulationReleaseProjected = 1 << 1,
    DestinationReservationReleased = 1 << 2,
    TerminalProjected = 1 << 3,
}

public readonly record struct SimRealmHarborMirrorTicket(long Generation, uint DestinationCell)
{
    public bool IsValid => Generation is not 0 && DestinationCell is not 0u;
}

public readonly record struct SimRealmHarborAck(SimRealmHarborMirrorTicket Projection, SimRealmHarborAckStage Stage);

public readonly record struct SimRealmHarborMirrorCapture(
    SimRealmHarborMirrorTicket Token,
    SimRealmHarborAckStage PendingAcknowledgements,
    bool ProjectionRegistered,
    bool SimulationReleaseProjected,
    bool DestinationReservationReleased,
    bool IsSuperseding)
{
    public int QueuedAcknowledgementTally => BitOperations.PopCount((uint)PendingAcknowledgements);
}

public readonly record struct SimPortalCapture(
    long Generation,
    SimPortalKind Kind,
    SimDestinationFitness Readiness,
    bool Materialized,
    bool Completed,
    bool Cancelled,
    bool WorldViewportObserved,
    bool WorldSimulationAvailable,
    int InvariantFailureCount,
    bool WaitCueShown,
    int PortalMaterializationCount)
{
    public static SimPortalCapture Idle { get; } = new(
        Generation: 0,
        SimPortalKind.None,
        Readiness: default,
        Materialized: false,
        Completed: false,
        Cancelled: false,
        WorldViewportObserved: false,
        WorldSimulationAvailable: true,
        InvariantFailureCount: 0,
        WaitCueShown: false,
        PortalMaterializationCount: 0);

    public uint DestinationCell => Readiness.DestinationCell;
    public bool IsReady => Readiness.IsReady;
    public bool IsMaterialized => Materialized;
    public bool IsCompleted => Completed;
    public bool IsCancelled => Cancelled;
    public bool IsWorldVisible => WorldViewportObserved;
    public bool IsActive => Generation is not 0 && !Cancelled;
}

public interface ISimPortalLens
{
    SimPortalCapture Snapshot { get; }

    SimRealmCrossingHoldingCapture Ownership { get; }
}
