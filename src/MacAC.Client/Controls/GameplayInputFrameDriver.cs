using MacAC.Client.Pulse;
using MacAC.Cockpit.Input;

namespace MacAC.Client.Controls;

internal interface IFightingInputFrameDriver
{
    void Tick();
    void ProcessTravelFeed(FeedAct act, ActivationKind activation);
    void CancelAutomaticAssault();
    bool ProcessFeedAct(FeedAct act, ActivationKind activation);
}

internal sealed class FightingAttackInputFrameBridge(SimFightingAttackLedger owner) : IFightingInputFrameDriver
{
    private readonly SimFightingAttackLedger _holder = owner ?? throw new ArgumentNullException(nameof(owner));

    public void Tick() => _holder.Tick();

    public void ProcessTravelFeed(FeedAct act, ActivationKind activation)
    {
        if (activation != ActivationKind.Press
            || act is not (
                FeedAct.MovementForward
                or FeedAct.MovementBackup
                or FeedAct.MovementRunLock
                or FeedAct.MovementJump))

            return;

        _holder.ServiceDirective(new SimFightingAttackInput(
            SimFightingAttackDirective.AbortForMovement,
            SimInputArming.Press));
    }

    public void CancelAutomaticAssault()
    {
        _holder.ServiceDirective(new SimFightingAttackInput(
            SimFightingAttackDirective.AbortForMovement,
            SimInputArming.Press));
    }

    public bool ProcessFeedAct(FeedAct act, ActivationKind activation)
    {
        SimFightingAttackDirective? directive = act switch
        {
            FeedAct.CombatLowAttack =>
                SimFightingAttackDirective.LowAttack,
            FeedAct.CombatMediumAttack =>
                SimFightingAttackDirective.MediumAttack,
            FeedAct.CombatHighAttack =>
                SimFightingAttackDirective.HighAttack,
            FeedAct.CombatDecreaseAttackPower =>
                SimFightingAttackDirective.DecreasePower,
            FeedAct.CombatIncreaseAttackPower =>
                SimFightingAttackDirective.IncreasePower,
            FeedAct.CombatDecreaseMissileAccuracy =>
                SimFightingAttackDirective.DecreasePower,
            FeedAct.CombatIncreaseMissileAccuracy =>
                SimFightingAttackDirective.IncreasePower,
            FeedAct.CombatAimLow =>
                SimFightingAttackDirective.LowAttack,
            FeedAct.CombatAimMedium =>
                SimFightingAttackDirective.MediumAttack,
            FeedAct.CombatAimHigh =>
                SimFightingAttackDirective.HighAttack,
            _ => null,
        };
        if (directive is null)
            return false;

        return activation == ActivationKind.Hold
            ? true
            : _holder.ServiceDirective(new SimFightingAttackInput(
            directive.Value,
            activation switch
            {
                ActivationKind.Press => SimInputArming.Press,
                ActivationKind.Release => SimInputArming.Release,
                _ => SimInputArming.Press,
            }));
    }
}

internal sealed class GameplayInputFrameDriver(
    InputRouter? router,
    RouterLocomotionInputSource movement,
    IMouseLookInputFrameDriver? pointerGaze,
    IFightingInputFrameDriver combat)
        : IGameplayFeedCycleStage,
      MacAC.Client.Paging.IAvatarWarpInputLifetime
{
    private readonly InputRouter? _router = router;
    private readonly RouterLocomotionInputSource _movement = movement ?? throw new ArgumentNullException(nameof(movement));
    private readonly IMouseLookInputFrameDriver? _pointerGaze = pointerGaze;
    private readonly IFightingInputFrameDriver _fighting = combat ?? throw new ArgumentNullException(nameof(combat));

    public bool PointerGazeEngaged => _pointerGaze?.Active == true;

    public void Tick(PulseFrameTiming timing)
    {
        _ = timing;
        _router?.Tick();
        _pointerGaze?.Tick();
        _fighting.Tick();
    }

    public bool ServicePtrAct(FeedAct act, ActivationKind activation) =>
        _pointerGaze?.ProcessPointerAction(act, activation) == true;

    public bool ServiceFightingAct(FeedAct act, ActivationKind activation)
    {
        _fighting.ProcessTravelFeed(act, activation);
        return _fighting.ProcessFeedAct(act, activation);
    }

    public bool ServicePressedTravelAct(FeedAct act) =>
        _movement.ProcessPressedAct(act);

    public void ScrapAutomaticAssault() => _fighting.CancelAutomaticAssault();

    public void EnqueueRawPointerDiff(float dx, float dy) =>
        _pointerGaze?.EnqueueRawDiff(dx, dy);

    public void EndMouseLook() => _pointerGaze?.FinishForLifecycle();

    public void RewindSess()
    {
        _pointerGaze?.ResetSession();
        _movement.ResetSession();
    }
}
