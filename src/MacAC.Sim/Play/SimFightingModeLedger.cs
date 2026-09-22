using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;

namespace MacAC.Sim.Play;

public enum SimFightingModeRequestStatus
{
    Inactive,
    Rejected,
    Sent,
}

public readonly record struct SimFightingModeRequestResult(SimFightingModeRequestStatus Status, FightingManner Mode, string? Notice = null);

public interface ISimFightingModeOps
{
    bool IsInRealm { get; }

    IReadOnlyList<ClientThing> ObtainSequencedEquipment();

    void InformExplicitFightingMannerReq();

    void DispatchEditFightingManner(FightingManner manner);
}

public sealed class SimFightingModeLedger(FightingPhase combat, ISimFightingModeOps operations)
{
    private readonly FightingPhase _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
    private readonly ISimFightingModeOps _ops = operations ?? throw new ArgumentNullException(nameof(operations));

    private SimFightingModeRequestResult Inactive => new(SimFightingModeRequestStatus.Inactive, _fighting.LatestMode);

    public SimFightingModeRequestResult Toggle()
    {
        if (!_ops.IsInRealm)
            return Inactive;

        _ops.InformExplicitFightingMannerReq();

        FightingManner latest = _fighting.LatestMode;
        if (latest != FightingManner.NonCombat)
            return Seal(FightingManner.NonCombat);

        var decision = FightInputPlanner.FetchDefaultFightingMannerDecision(_ops.ObtainSequencedEquipment());
        if (decision.IncompatibleHeldItem is { } pinned)
        {
            return new SimFightingModeRequestResult(
                SimFightingModeRequestStatus.Rejected,
                latest,
                $"You can't enter combat mode while wielding the {pinned.FetchAppropriateLabel()}");
        }
        return Seal(FightInputPlanner.FlipManner(latest, decision.Mode));
    }

    public SimFightingModeRequestResult Request(FightingManner manner)
    {
        if (!_ops.IsInRealm)
            return Inactive;
        if (manner is not (FightingManner.NonCombat or FightingManner.Melee or FightingManner.Missile or FightingManner.Magic))
            return new SimFightingModeRequestResult(SimFightingModeRequestStatus.Rejected, _fighting.LatestMode, "Invalid combat mode.");
        if (_fighting.LatestMode == manner)
            return new SimFightingModeRequestResult(SimFightingModeRequestStatus.Sent, manner);

        _ops.InformExplicitFightingMannerReq();
        return Seal(manner);
    }

    private SimFightingModeRequestResult Seal(FightingManner manner)
    {
        _ops.DispatchEditFightingManner(manner);
        _fighting.ApplyFightingManner(manner);
        return new SimFightingModeRequestResult(SimFightingModeRequestStatus.Sent, manner);
    }
}
