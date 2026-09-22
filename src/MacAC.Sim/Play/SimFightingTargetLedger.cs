using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Targeting;

namespace MacAC.Sim.Play;

public interface ISimFightingTargetOps
{
    bool AutoTarget { get; }

    uint? PickClosestMark();
}

public sealed class SimFightingTargetLedger : IDisposable
{
    private readonly FightingPhase _fighting;
    private readonly PickPhase _pick;
    private readonly ISimFightingTargetOps _ops;
    private bool _destroyed;

    public SimFightingTargetLedger(FightingPhase combat, PickPhase selection, ISimFightingTargetOps operations)
    {
        _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _ops = operations ?? throw new ArgumentNullException(nameof(operations));
        _pick.Changed += OnPickShift;
    }

    public bool IsDisposed => _destroyed;

    public void OnLocomotionImposed(uint objectIdent, uint latestLocomotion)
    {
        if (latestLocomotion == LocomotionDirective.Dead && _pick.ChosenObjectTag == objectIdent)
            _pick.Clear(PickChangeSource.System, PickChangeReason.CombatTargetDied);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _pick.Changed -= OnPickShift;
    }

    private void OnPickShift(PickShift shift)
    {
        bool emptied = shift.SelectedObjectId is null && shift.Reason != PickChangeReason.SessionReset;
        if (emptied && _ops.AutoTarget && FightInputPlanner.SupportsTargetedAssault(_fighting.LatestMode))
            _ops.PickClosestMark();
    }
}
