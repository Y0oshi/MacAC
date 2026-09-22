using MacAC.Mechanics.Fighting;
using MacAC.Sim.Play;

namespace MacAC.Sim;

public readonly record struct SimFightingAttackCapture(
    long Revision,
    AssaultElevation RequestedHeight,
    float DesiredPower,
    float PowerBarLevel,
    bool BuildInProgress,
    bool RequestInProgress,
    float RequestedPower,
    bool RepeatAttackInProgress = false,
    bool ServerResponsePending = false)
{
    public long WrapUpRevision { get; init; }
    public uint WrapUpSequence { get; init; }
    public uint WrapUpWeenieError { get; init; }
}

public readonly record struct SimArcanaCastCapture(long Revision, uint LastRequestedSpellId, uint LastRequestedTargetId);

/// <summary>Selection, combat, interaction and casting state in one record.</summary>
public readonly record struct SimActionCapture(
    long SelectionRevision,
    uint SelectedObjectId,
    uint PreviousObjectId,
    uint PreviousValidObjectId,
    long CombatRevision,
    FightingManner CombatMode,
    int TrackedTargetHealthCount,
    long InteractionRevision,
    DealingModeKind DealingMode,
    uint InteractionSourceObjectId,
    SimDealingTransactionCapture InteractionTransactions,
    SimFightingAttackCapture CombatAttack = default,
    SimArcanaCastCapture Magic = default);

public interface ISimActionLens
{
    SimActionCapture Snapshot { get; }

    bool TryFetchHealth(uint objectIdent, out float healthPct);
}
