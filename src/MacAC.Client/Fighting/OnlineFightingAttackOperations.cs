using MacAC.Client.Link;
using MacAC.Mechanics.Fighting;
using MacAC.Wire.Messages;

namespace MacAC.Client.Fighting;

internal interface IFightingAttackTargetSource
{
    uint? ChosenObjectIdent { get; }
    uint? FetchChosenOrClosestFightingObjective(bool autoMark);
}

internal interface IFightingGameplayPreferencesSource
{
    bool AutoTarget { get; }
    bool AutoRepeatAttack { get; }
    bool ViewCombatTarget { get; }
}

internal sealed class ToonKnobFightingPreferencesSource(SimToonOptionsLedger options) : IFightingGameplayPreferencesSource
{
    private readonly SimToonOptionsLedger _knobs = options ?? throw new ArgumentNullException(nameof(options));

    public bool AutoTarget =>
        _knobs.GetOptionBit(CharacterOptionId.AutoTarget);

    public bool AutoRepeatAttack =>
        _knobs.GetOptionBit(CharacterOptionId.AutoRepeatAttack);

    public bool ViewCombatTarget =>
        _knobs.GetOptionBit(CharacterOptionId.ViewCombatTarget);
}

internal interface IFightingFeedbackSink
{
    void Reveal(string msg);
}

internal sealed class FightingFeedbackSlot : IFightingFeedbackSink
{
    private Action<string>? _mark;

    public void Bind(Action<string> mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (_mark is not null && !ReferenceEquals(_mark, mark))
            throw new InvalidOperationException(
                "Combat feedback is by now bound to a presentation target");
        _mark = mark;
    }

    public IDisposable BindOwned(Action<string> mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (_mark is not null)
            throw new InvalidOperationException(
                "Combat feedback is by now bound to a presentation target");
        _mark = mark;
        return new BindingUnit(this, mark);
    }

    public void Unbind(Action<string> mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        if (ReferenceEquals(_mark, mark))
            _mark = null;
    }

    public void Reveal(string msg) => _mark?.Invoke(msg);

    private sealed class BindingUnit(FightingFeedbackSlot socket, Action<string> mark)
        : IDisposable
    {
        public void Dispose() => socket.Unbind(mark);
    }
}

internal sealed class FightingAttackOperationsSlot
    : ISimFightingAttackOps
{
    private ISimFightingAttackOps? _holder;

    public void Bind(ISimFightingAttackOps holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_holder is not null && !ReferenceEquals(_holder, holder))
            throw new InvalidOperationException(
                "Combat attack operations are by now bound");
        _holder = holder;
    }

    public IDisposable BindOwned(ISimFightingAttackOps holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        if (_holder is not null)
            throw new InvalidOperationException(
                "Combat attack operations are by now bound");
        _holder = holder;
        return new Binding(this, holder);
    }

    public bool CanBeginAssault() => _holder?.CanBeginAssault() == true;
    public void ReadyAssaultReq() => _holder?.ReadyAssaultReq();
    public bool TransmitAssault(AssaultElevation height, float strength) =>
        _holder?.TransmitAssault(height, strength) == true;
    public void TransmitAbortAssault() => _holder?.TransmitAbortAssault();

    private void Loosen(ISimFightingAttackOps anticipated)
    {
        if (ReferenceEquals(_holder, anticipated))
            _holder = null;
    }
    public bool IsDualWield => _holder?.IsDualWield == true;
    public bool AvatarPrimedForAssault => _holder?.AvatarPrimedForAssault == true;
    public bool AutoRepeatAttack => _holder?.AutoRepeatAttack == true;

    private sealed class Binding(
        FightingAttackOperationsSlot socket,
        ISimFightingAttackOps anticipated) : IDisposable
    {
        private FightingAttackOperationsSlot? _socket = socket;
        private readonly ISimFightingAttackOps _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _socket, null)?.Loosen(_anticipated);
    }
}

internal sealed class OnlineFightingAttackOperations(
    FightingPhase combat,
    IFightingAttackTargetSource targets,
    IFightingGameplayPreferencesSource settings,
    ISimAvatarDriverSource player,
    AvatarOutboundDriver outbound,
    IOnlineInRealmSource inWorld,
    IOnlineRealmSessionSource session,
    IFightingFeedbackSink feedback)
        : ISimFightingAttackOps
{
    private readonly FightingPhase _fighting = combat ?? throw new ArgumentNullException(nameof(combat));
    private readonly IFightingAttackTargetSource _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly IFightingGameplayPreferencesSource _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly AvatarOutboundDriver _outgoing = outbound ?? throw new ArgumentNullException(nameof(outbound));
    private readonly IOnlineInRealmSource _inRealm = inWorld ?? throw new ArgumentNullException(nameof(inWorld));
    private readonly IOnlineRealmSessionSource _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly IFightingFeedbackSink _feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));

    public bool IsDualWield
    {
        get
        {
            return _avatar.Controller?.Locomotion.InterpretedPhase.LatestStyle
        == FightInputPlanner.DualWieldFightingStyling;
        }
    }

    public bool AvatarPrimedForAssault
    {
        get
        {
            if (_avatar.Controller is not { } driver)
                return false;
            var locomotion = driver.Locomotion.InterpretedPhase;
            return FightInputPlanner.AvatarInPrimedLocusForAssault(
                _fighting.LatestMode,
                locomotion.LatestStyle,
                locomotion.ForwardCommand);
        }
    }

    public bool AutoRepeatAttack => _prefs.AutoRepeatAttack;

    public bool CanBeginAssault()
    {
        if (!_inRealm.IsInWorld)
            return false;

        if (!FightInputPlanner.SupportsTargetedAssault(_fighting.LatestMode))
        {
            Console.WriteLine(
                "combat: attack ignored; not in melee/missile combat mode");
            return false;
        }

        if (_targets.FetchChosenOrClosestFightingObjective(_prefs.AutoTarget) is null)
        {
            _feedback.Reveal(MacAC.Mechanics.Comms.TextRefusals.MustPickFightingMark);
            Console.WriteLine("combat: attack ignored; no creature target found");
            return false;
        }

        return true;
    }

    public bool TransmitAssault(AssaultElevation height, float strength)
    {
        if (!CanBeginAssault()
            || _session.LatestSess is not { } sess
            || _targets.ChosenObjectIdent is not { } mark)

            return false;

        strength = Math.Clamp(strength, 0f, 1f);
        if (_fighting.LatestMode == FightingManner.Missile)
        {
            sess.TransmitMissileAssault(mark, height, strength);
            Console.WriteLine(
                $"combat: missile attack target=0x{mark:X8} height={height} accuracy={strength:F2}");
        }
        else
        {
            sess.TransmitMeleeAssault(mark, height, strength);
            Console.WriteLine(
                $"combat: melee attack target=0x{mark:X8} height={height} power={strength:F2}");
        }
        return true;
    }

    public void TransmitAbortAssault() =>
        _session.LatestSess?.DispatchAbortAssault();

    public void ReadyAssaultReq()
    {
        if (_avatar.Controller is not { } driver
            || !driver.ReadyForAssaultReq())

            return;

        _outgoing.TryTransmitTravel(
            _session.LatestSess,
            driver,
            driver.GrabTravelOutcome(pointerGazeSignal: false));
    }
}
