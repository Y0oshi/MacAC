using MacAC.Client.SimBridge;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{

    public ItemVerdict Lift(uint objectIdent, bool primaryBundle = false)
    {
        SimCore? core;
        Func<uint, bool, bool>? lift;
        lock (_latch)
        {
            core = _runtime;
            lift = _liftGear;
        }
        if (core is null || lift is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        uint trunk = core.SatchelHolder.ExternalVessels.LatestVesselIdent;
        var objects = core.SatchelHolder.Objects;
        if (objectIdent is 0u
            || trunk is 0u
            || objects.Get(objectIdent) is null
            || !GrabVesselIdents(objects, trunk).Contains(objectIdent))

            return new(ItemOutcome.InvalidItem);
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : lift(objectIdent, primaryBundle)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public FellowshipVerdict Create(
        string label,
        bool portionExperience)
    {
        return CallFellowship(directives =>
            directives.FellowshipDirectives.Create(
                directives.Generation,
                label,
                portionExperience));
    }

    uint ILootControls.RequestedContainerId
    {
        get
        {
            lock (_latch)
                return _runtime?.SatchelHolder.ExternalVessels
                    .AskedVesselIdent ?? 0u;
        }
    }

    AppraisalFrame ILootControls.Appraisal
    {
        get
        {
            lock (_latch)
            {
                var transactions =
                    _runtime?.ActHolder.Transactions;
                return transactions is null
                    ? default
                    : new AppraisalFrame(
                        transactions.Revision,
                        transactions.ExpectingAppraisalIdent,
                        transactions.LatestAppraisalTag);
            }
        }
    }

    public FellowshipVerdict Recruit(uint markObjectIdent)
    {
        return CallFellowship(directives => directives.FellowshipDirectives.Recruit(
            directives.Generation,
            markObjectIdent));
    }

    public uint LeaderObjectIdent
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.LeaderGuid ?? 0u;
        }
    }

    public int MemberCount
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            return core?.Fellowship.Snapshot.MemberCount ?? 0;
        }
    }

    public FellowshipVerdict Dismiss(uint markObjectIdent)
    {
        return CallFellowship(directives => directives.FellowshipDirectives.Dismiss(
            directives.Generation,
            markObjectIdent));
    }

    public CombatVerdict DismissGhostMark(uint markObjectIdent)
    {
        Func<uint, bool>? dismiss;
        lock (_latch)
            dismiss = _dismissGhost;
        return dismiss is null || !IsAvailable
            ? new(CombatOutcome.Unavailable)
            : dismiss(markObjectIdent)
            ? new(CombatOutcome.Stopped)
            : new(CombatOutcome.InvalidTarget);
    }

    public FellowshipVerdict Quit(bool disband)
    {
        return CallFellowship(directives => directives.FellowshipDirectives.Quit(
            directives.Generation,
            disband));
    }

    public FellowshipVerdict AssignLeader(uint markObjectIdent)
    {
        return CallFellowship(directives => directives.FellowshipDirectives.AssignLeader(
            directives.Generation,
            markObjectIdent));
    }

    public bool AnnounceCasting(
        uint markObjectIdent,
        uint arcanumIdent,
        double intervalSecs)
    {
        if (markObjectIdent is 0u
            || arcanumIdent is 0u
            || !double.IsFinite(intervalSecs)
            || intervalSecs <= 0d)

            return false;

        lock (_latch)
        {
            if (_destroyed
                || _grimoire is null
                || !_grimoire.TryFetchMetadata(arcanumIdent, out SpellMeta metadata))

                return false;
            FollowEnchantment(
                markObjectIdent,
                metadata,
                intervalSecs,
                DateTimeOffset.UtcNow);
            return true;
        }
    }

    public CombatVerdict JoinDefaultManner()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return new(CombatOutcome.Unavailable);
        if (core.ActHolder.Combat.LatestMode != FightingManner.NonCombat)
            return new(CombatOutcome.AlreadyReady);

        var outcome = core.ActHolder.CombatMode.Toggle();
        return outcome.Status switch
        {
            SimFightingModeRequestStatus.Sent => new(
                CombatOutcome.ModeChangeSent),
            SimFightingModeRequestStatus.Rejected => new(
                CombatOutcome.Refused, outcome.Notice),
            _ => new(CombatOutcome.Unavailable, outcome.Notice),
        };
    }

    public CombatVerdict JoinManner(CombatPosture manner)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return new(CombatOutcome.Unavailable);
        FightingManner asked = manner switch
        {
            CombatPosture.Peace => FightingManner.NonCombat,
            CombatPosture.Melee => FightingManner.Melee,
            CombatPosture.Missile => FightingManner.Missile,
            CombatPosture.Magic => FightingManner.Magic,
            _ => (FightingManner)(-1),
        };
        if ((int)asked < 0)
            return new(CombatOutcome.Refused, "Invalid combat mode.");
        if (core.ActHolder.Combat.LatestMode == asked)
            return new(CombatOutcome.AlreadyReady);

        var outcome =
            core.ActHolder.CombatMode.Request(asked);
        return outcome.Status switch
        {
            SimFightingModeRequestStatus.Sent => new(
                CombatOutcome.ModeChangeSent),
            SimFightingModeRequestStatus.Rejected => new(
                CombatOutcome.Refused, outcome.Notice),
            _ => new(CombatOutcome.Unavailable, outcome.Notice),
        };
    }

    public CombatVerdict CommencePhysicalAssault(
        uint markObjectIdent,
        StrikeHeight height,
        float strength)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return new(CombatOutcome.Unavailable);
        if (!SimFoeTargetProbe.IsHostile(core, markObjectIdent))
            return new(CombatOutcome.InvalidTarget);
        if (!FightInputPlanner.SupportsTargetedAssault(
                core.ActHolder.Combat.LatestMode))

            return new(CombatOutcome.WrongMode);

        var assault = core.ActHolder.CombatAttack;
        if (assault.AssaultReqInHeadway
            || assault.AssaultSrvResponseQueued
            || assault.RepeatAssaultInHeadway)

            return new(CombatOutcome.Busy);

        core.ActHolder.Selection.Select(
            markObjectIdent,
            PickChangeSource.Plugin);
        assault.AssignWantedStrength(Math.Clamp(strength, 0f, 1f));
        assault.PressAssault(Project(height));
        return assault.AssaultReqInHeadway
            ? new(CombatOutcome.Started)
            : new(CombatOutcome.Refused);
    }

    public CombatVerdict FreePhysicalAssault()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return new(CombatOutcome.Unavailable);

        var assault = core.ActHolder.CombatAttack;
        if (!assault.AssaultReqInHeadway)
            return new(CombatOutcome.Refused);
        assault.FreeAssault();
        return new(CombatOutcome.Released);
    }

    public CombatVerdict CancelPhysicalAssault()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null)
            return new(CombatOutcome.Unavailable);
        core.ActHolder.CombatAttack.CancelAutomaticAttack();
        return new(CombatOutcome.Stopped);
    }

    public void Dispose()
    {
        _signals?.Tick -= OnCounterpartBeat;
        lock (_latch)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            _wield = null;
            _equipmentOccupied = null;
            _useGear = null;
            _enactGear = null;
            _relocateGear = null;
            _combineGearList = null;
            _discardGear = null;
            _handGear = null;
            _liftGear = null;
            _recognizeGear = null;
            _salvageGearList = null;
            _vendGear = null;
            _pickAct = null;
            UnfastenBolted();
        }
        RecognizedSelfBuffs = Array.Empty<SpellFacts>();
        RecognizedAssaultArcana = Array.Empty<SpellFacts>();
        RecognizedFightingArcana = Array.Empty<SpellFacts>();
        EngagedEnchantments = Array.Empty<ActiveEnchantmentFacts>();
        _peers.Dispose();
    }
    private ItemVerdict RelayGear(
        uint objectIdent,
        uint markObjectIdent)
    {
        Func<uint, bool>? use;
        Func<uint, uint, bool>? enact;
        SimCore? core;
        lock (_latch)
        {
            use = _useGear;
            enact = _enactGear;
            core = _runtime;
        }
        if (core is null || use is null || enact is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (objectIdent is 0u
            || objects.Get(objectIdent) is not { } gear
            || !IsAvatarPossessed(gear, avatarIdent, objects))

            return new(ItemOutcome.InvalidItem);
        if (markObjectIdent is not 0u && objects.Get(markObjectIdent) is null)
            return new(ItemOutcome.InvalidTarget);
        if (!core.SatchelHolder.Transactions.CanCommenceReq)
            return new(ItemOutcome.Busy);
        bool begun = markObjectIdent is 0u
            ? use(objectIdent)
            : enact(objectIdent, markObjectIdent);
        return begun
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    private static bool ValidQuantity(ClientThing gear, uint quantity) =>
        quantity is 0u || quantity <= (uint)Math.Max(1, gear.StackSize);

    private FellowshipVerdict CallFellowship(
        Func<CurrentGameEngineBridge, SimDirectiveResult> invoke)
    {
        CurrentGameEngineBridge? directives;
        lock (_latch)
            directives = _sessDirectives;
        if (directives is null || !IsAvailable)
            return new(FellowshipOutcome.Unavailable);
        var outcome = invoke(directives);
        return new(outcome.Status switch
        {
            SimDirectiveStatus.Accepted => FellowshipOutcome.Accepted,
            SimDirectiveStatus.Rejected => FellowshipOutcome.Rejected,
            _ => FellowshipOutcome.Unavailable,
        });
    }

    private void WatchSuccessfulOwnCasting()
    {
        lock (_latch)
        {
            SimArcanaCastFinish wrapUp =
                _casting?.PreviousWrapUp ?? default;
            if (wrapUp.Revision is 0
                || wrapUp.Revision <= _followedCastingWrapUpRev)

                return;
            _followedCastingWrapUpRev = wrapUp.Revision;
            if (!wrapUp.IsSuccess
                || wrapUp.TargetObjectId is 0u
                || _grimoire is null
                || !_grimoire.TryFetchMetadata(
                    wrapUp.SpellId,
                    out SpellMeta metadata)
                || metadata.Duration <= 0f)

                return;
            FollowEnchantment(
                wrapUp.TargetObjectId,
                metadata,
                metadata.Duration,
                DateTimeOffset.UtcNow);
        }
    }

    private void FollowEnchantment(
        uint markObjectIdent,
        SpellMeta metadata,
        double intervalSecs,
        DateTimeOffset instant)
    {
        FollowedEnchantment followed = new FollowedEnchantment(
            metadata.Family,
            metadata.Difficulty,
            metadata.IsUntargeted,
            instant.AddSeconds(intervalSecs));
        (uint Target, uint Spell) tag = (markObjectIdent, metadata.SpellId);
        if (!_followedEnchantments.TryGetValue(tag, out FollowedEnchantment former)
            || followed.ExpiresAt > former.ExpiresAt)

            _followedEnchantments[tag] = followed;
    }

    private void PruneFollowedEnchantments(DateTimeOffset instant)
    {
        foreach ((uint Target, uint Spell) tag in
            _followedEnchantments
                .Where(duo => duo.Value.ExpiresAt <= instant)
                .Select(static duo => duo.Key)
                .ToArray())
        {
            _followedEnchantments.Remove(tag);
        }
    }

    private bool PickExplicitMark(uint markObjectIdent)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable || markObjectIdent is 0u
            || !core.EntityObjects.Entities.TryFetchEngaged(markObjectIdent, out _))

            return false;
        core.ActHolder.Selection.Select(
            markObjectIdent,
            PickChangeSource.Plugin);
        return true;
    }
}
