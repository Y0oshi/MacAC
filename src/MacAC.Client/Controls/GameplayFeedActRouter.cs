using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Shell;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Fighting;
using MacAC.Sim;

namespace MacAC.Client.Controls;

internal interface IGameplayFeedActCanvas
{
    void AppendFired(Action<FeedAct, ActivationKind> hook);

    void DropFired(Action<FeedAct, ActivationKind> hook);

    void AssignFightingAmbit(InputLayer? ambit);

    void AssignCamAlternateAmbit(bool engaged);
}

internal sealed class RouterGameplayInputActionSurface(InputRouter dispatcher)
    : IGameplayFeedActCanvas
{
    private readonly InputRouter _router = dispatcher
        ?? throw new ArgumentNullException(nameof(dispatcher));

    public void AppendFired(Action<FeedAct, ActivationKind> hook) =>
        _router.Fired += hook;

    public void DropFired(Action<FeedAct, ActivationKind> hook) =>
        _router.Fired -= hook;

    public void AssignFightingAmbit(InputLayer? ambit) =>
        _router.ApplyFightingAmbit(ambit);

    public void AssignCamAlternateAmbit(bool engaged) =>
        _router.ApplyCamAlternateAmbit(engaged);
}

internal interface IFightingModeEventSurface
{
    FightingManner LatestManner { get; }

    void AppendAltered(Action<FightingManner> hook);

    void DropAltered(Action<FightingManner> hook);
}

internal sealed class FightingStateModeEventSurface(FightingPhase combat)
    : IFightingModeEventSurface
{
    private readonly FightingPhase _fighting = combat
        ?? throw new ArgumentNullException(nameof(combat));

    public FightingManner LatestManner => _fighting.LatestMode;

    public void AppendAltered(Action<FightingManner> hook) =>
        _fighting.CombatModeChanged += hook;

    public void DropAltered(Action<FightingManner> hook) =>
        _fighting.CombatModeChanged -= hook;
}

internal interface IGameplayFeedPrecedenceMarks
{
    bool ProcessPtrAct(FeedAct act, ActivationKind activation);

    void ServiceRoll(FeedAct act);

    bool ProcessFightingAct(FeedAct act, ActivationKind activation);

    bool ProcessKeptWidgetAct(FeedAct act);

    bool ProcessToonKnobAct(FeedAct act);

    bool ProcessPickAct(FeedAct act);

    bool ProcessPressedTravelAct(FeedAct act);

    void ProcessDirective(FeedAct act);
}

internal sealed class EngineGameplayInputPriorityTargets(
    GameplayInputFrameDriver frame,
    CameraPointerInputDriver pointer,
    CanonWidgetEngine? keptWidget,
    PickingDealingDriver? pick,
    ISimCoreLens runtimeView,
    ISimSelectionDirectives runtimeSelection,
    ISimLocomotionDirectives runtimeMovement,
    ISimToonDirectives runtimeCharacter,
    IGameplayInputDirectiveTarget commands)
        : IGameplayFeedPrecedenceMarks
{
    private readonly GameplayInputFrameDriver _cycle = frame ?? throw new ArgumentNullException(nameof(frame));
    private readonly CameraPointerInputDriver _ptr = pointer ?? throw new ArgumentNullException(nameof(pointer));
    private readonly CanonWidgetEngine? _keptWidget = keptWidget;
    private readonly PickingDealingDriver? _pick = pick;
    private readonly ISimCoreLens _coreLens = runtimeView
            ?? throw new ArgumentNullException(nameof(runtimeView));
    private readonly ISimSelectionDirectives _corePick = runtimeSelection
            ?? throw new ArgumentNullException(nameof(runtimeSelection));
    private readonly ISimLocomotionDirectives _coreTravel = runtimeMovement
            ?? throw new ArgumentNullException(nameof(runtimeMovement));
    private readonly ISimToonDirectives _runtimeCharacter = runtimeCharacter
            ?? throw new ArgumentNullException(nameof(runtimeCharacter));
    private readonly IGameplayInputDirectiveTarget _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    public bool ProcessPtrAct(FeedAct act, ActivationKind activation)
    {
        return _cycle.ServicePtrAct(act, activation)
        || _ptr.ProcessCamAct(act, activation);
    }

    public void ServiceRoll(FeedAct act) =>
        _ptr.ProcessRoll(act);

    public bool ProcessFightingAct(FeedAct act, ActivationKind activation) =>
        _cycle.ServiceFightingAct(act, activation);

    public bool ProcessKeptWidgetAct(FeedAct act)
    {
        return CompleteLeapPriorWidget(act)
        || _keptWidget?.ProcessInputAction(act) == true;
    }

    public bool ProcessToonKnobAct(FeedAct act)
    {
        if (!CanonActionIdentityTable.TryFetchToonKnobIdent(
                act,
                out uint knobIdent)
            || !ToonOptionChart.TryGet(
                knobIdent,
                out ToonOptionChartEntry listing))

            return false;

        var knobs =
            _coreLens.Character.Snapshot.Options;
        uint word = listing.IsOptions1 ? knobs.Options1 : knobs.Options2;
        bool latest = (word & listing.Mask) is not 0u;
        _runtimeCharacter.AssignSingleKnob(
            _coreLens.Generation,
            knobIdent,
            !latest);
        return true;
    }

    public bool ProcessPickAct(FeedAct act)
    {
        if (act == FeedAct.EscapeKey)
        {
            var travel = _coreLens.Movement;
            var assault = _coreLens.Actions.Snapshot
                .CombatAttack;
            var escapeDirective =
                LocateEscapeTravelDirective(
                    act,
                    travel.IsStandingStill,
                    travel.LeapCharge,
                    assault);
            if (escapeDirective == SimLocomotionDirective.StopCompletely)
            {
                _coreTravel.Execute(
                    _coreLens.Generation,
                    escapeDirective.Value);
                if (assault.RepeatAttackInProgress)
                    _cycle.ScrapAutomaticAssault();
                return true;
            }
        }

        SimSelectionDirective? directive = act switch
        {
            FeedAct.SelectionClosestMonster =>
                SimSelectionDirective.SelectClosestHostile,
            FeedAct.SelectionPreviousSelection =>
                SimSelectionDirective.SelectPrevious,
            FeedAct.SelectionExamine =>
                SimSelectionDirective.ExamineSelected,
            FeedAct.UseSelected =>
                SimSelectionDirective.UseSelected,
            FeedAct.SelectionPickUp =>
                SimSelectionDirective.PickUpSelected,
            _ => null,
        };
        if (directive is { } typed)
        {
            _corePick.Execute(_coreLens.Generation, typed);
            return true;
        }

        return _pick?.ServiceFeedAct(act) == true;
    }

    public bool ProcessPressedTravelAct(FeedAct act)
    {
        if (CanonGestureMotionChart.TryFetchLocomotion(act, out uint locomotion))
        {
            _coreTravel.PerformLocomotion(
                _coreLens.Generation,
                locomotion);
            return true;
        }

        var directive = LocatePressedTravelDirective(act);
        if (directive is { } typed)
        {
            _coreTravel.Execute(_coreLens.Generation, typed);
            return true;
        }

        return _cycle.ServicePressedTravelAct(act);
    }

    public void ProcessDirective(FeedAct act) =>
        _commands.Handle(act);

    internal static SimLocomotionDirective? LocateEscapeTravelDirective(
        FeedAct act,
        bool isStandingStill,
        in MacAC.Sim.Play.JumpChargeCapture leapCharge,
        in SimFightingAttackCapture assault)
    {
        if (act != FeedAct.EscapeKey)
            return null;
        if (leapCharge.IsCharging)
            return SimLocomotionDirective.FinishJump;
        return !isStandingStill || assault.RepeatAttackInProgress ? SimLocomotionDirective.StopCompletely : null;
    }

    internal static SimLocomotionDirective? LocatePressedTravelDirective(
        FeedAct act)
    {
        return act switch
        {
            FeedAct.MovementRunLock => SimLocomotionDirective.ToggleRunLock,
            FeedAct.MovementStop => SimLocomotionDirective.Stop,
            FeedAct.Ready => SimLocomotionDirective.Ready,
            FeedAct.Sitting => SimLocomotionDirective.Sit,
            FeedAct.Crouch => SimLocomotionDirective.Crouch,
            FeedAct.Sleeping => SimLocomotionDirective.Sleep,
            _ => null,
        };
    }

    private bool CompleteLeapPriorWidget(FeedAct act)
    {
        var directive = LocateEscapeTravelDirective(
            act,
            _coreLens.Movement.IsStandingStill,
            _coreLens.Movement.LeapCharge,
            _coreLens.Actions.Snapshot.CombatAttack);
        if (directive != SimLocomotionDirective.FinishJump)

            return false;

        _coreTravel.Execute(
            _coreLens.Generation,
            directive.Value);
        return true;
    }
}

internal sealed class GameplayFeedActRouter : IDisposable
{
    private readonly IGameplayFeedActCanvas _actions;
    private readonly IFightingModeEventSurface _fighting;
    private readonly IGameplayFeedPrecedenceMarks _targets;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<string> _trace;
    private readonly Action<FeedAct, ActivationKind> _fired;
    private readonly Action<FightingManner> _fightingMannerAltered;
    private readonly bool[] _affixed = new bool[2];
    private AssetShutdownTransaction? _unfasten;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;

    public GameplayFeedActRouter(
        IGameplayFeedActCanvas actions,
        IFightingModeEventSurface combat,
        IGameplayFeedPrecedenceMarks targets,
        HostQuiescenceTurnstile quiescence,
        Action<string>? trace = null)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _trace = trace ?? (_ => { });
        _fired = OnFired;
        _fightingMannerAltered = OnFightingMannerAltered;
    }

    public static GameplayFeedActRouter Create(
        InputRouter router,
        FightingPhase fighting,
        IGameplayFeedPrecedenceMarks marks,
        HostQuiescenceTurnstile stillness,
        Action<string>? trace = null)
    {
        return new(
            new RouterGameplayInputActionSurface(router),
            new FightingStateModeEventSurface(fighting),
            marks,
            stillness,
            trace);
    }

    public bool IsDisposalComplete =>
        _affixed.All(static affixed => !affixed);

    public void Fasten()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
        {
            throw new InvalidOperationException(
                "Gameplay action attachment has by now started");
        }

        _fastenBegun = true;
        try
        {
            _affixed[0] = true;
            _actions.AppendFired(_fired);
            _affixed[1] = true;
            _fighting.AppendAltered(_fightingMannerAltered);

            Volatile.Write(ref _engaged, 1);
            AssignFightingAmbit(_fighting.LatestManner);
        }
        catch (Exception fastenProblem)
        {
            Disarm();
            try
            {
                SecureUnfastenTransaction().CompleteOrThrow();
            }
            catch (Exception undoProblem)
            {
                throw new AggregateException(
                    "Gameplay action registration and rollback both failed",
                    new InvalidOperationException(
                        "Gameplay action registration failed", fastenProblem),
                    undoProblem);
            }

            throw new InvalidOperationException(
                "Gameplay action registration failed and was rolled back",
                fastenProblem);
        }
    }

    public void Disarm() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Disarm();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private void OnFired(FeedAct act, ActivationKind activation)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                Course(act, activation);
        });
    }

    private void OnFightingMannerAltered(FightingManner manner)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                AssignFightingAmbit(manner);
        });
    }

    private void Course(FeedAct act, ActivationKind activation)
    {
        _trace($"[input] {act} {activation}");

        if (act == FeedAct.CameraActivateAlternateMode)
        {
            if (activation == ActivationKind.Press)
                _actions.AssignCamAlternateAmbit(true);
            else if (activation == ActivationKind.Release)
                _actions.AssignCamAlternateAmbit(false);
        }

        if (_targets.ProcessPtrAct(act, activation))
            return;

        if (act is FeedAct.ScrollUp or FeedAct.ScrollDown)
        {
            if (activation == ActivationKind.Press)
                _targets.ServiceRoll(act);
            return;
        }

        if (_targets.ProcessFightingAct(act, activation))
            return;

        if (activation is not ActivationKind.Press
            and not ActivationKind.DoubleClick
            and not ActivationKind.Click)

            return;

        if (_targets.ProcessKeptWidgetAct(act))
            return;
        if (_targets.ProcessToonKnobAct(act))
            return;
        if (_targets.ProcessPickAct(act))
            return;
        if (_targets.ProcessPressedTravelAct(act))
            return;

        _targets.ProcessDirective(act);
    }

    private void AssignFightingAmbit(FightingManner manner)
    {
        _actions.AssignFightingAmbit(manner switch
        {
            FightingManner.Melee => InputLayer.MeleeCombat,
            FightingManner.Missile => InputLayer.MissileCombat,
            FightingManner.Magic => InputLayer.MagicCombat,
            _ => null,
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("gameplay action callbacks",
            [
                new("combat mode", () => Drop(1)),
                new("dispatcher fired", () => Drop(0)),
            ]));
    }

    private void Drop(int index)
    {
        if (!_affixed[index])
            return;

        switch (index)
        {
            case 0:
                _actions.DropFired(_fired);
                break;
            case 1:
                _fighting.DropAltered(_fightingMannerAltered);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        _affixed[index] = false;
    }
}
