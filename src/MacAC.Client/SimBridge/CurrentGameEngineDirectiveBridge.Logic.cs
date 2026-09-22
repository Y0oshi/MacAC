using MacAC.Client.Link;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Sim;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineDirectiveBridge
{
    public SimSessionStartResult Start(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _lens.Lifecycle.State;
        var latch = Validate(anticipatedGen, demandRealm: false);
        if (latch != SimDirectiveStatus.Accepted)
            return RejectedBegin(latch);

        var outcome =
            _sessHub.Start(anticipatedGen);
        _signals.WriteDirective(
            SimDirectiveDomain.Session,
            op: 0,
            ToDirectiveCondition(outcome.Status),
            outcome.CharacterId,
            outcome.CharacterName);
        _signals.WriteLifecycle(earlier, _lens.Lifecycle.State);
        return outcome;
    }

    public SimSessionStartResult Reconnect(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _lens.Lifecycle.State;
        var latch = Validate(anticipatedGen, demandRealm: false);
        if (latch != SimDirectiveStatus.Accepted)
            return RejectedBegin(latch);

        var outcome =
            _sessHub.Reconnect(anticipatedGen);
        _signals.WriteDirective(
            SimDirectiveDomain.Session,
            op: 1,
            ToDirectiveCondition(outcome.Status),
            outcome.CharacterId,
            outcome.CharacterName);
        _signals.WriteLifecycle(earlier, _lens.Lifecycle.State);
        return outcome;
    }

    public SimDirectiveResult PivotToBearing(
        SimEpochTicket anticipatedGen,
        float bearingDeg,
        bool enactExecGripTag = false)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition = _movement.PivotToHeading(
            bearingDeg,
            enactExecGripTag)
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Unsupported;
        _signals.WriteDirective(
            SimDirectiveDomain.Movement,
            op: 0x103,
            condition);
        return Result(condition);
    }

    public SimDirectiveResult DiscardArcanum(
        SimEpochTicket anticipatedGen,
        uint arcanumIdent)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (arcanumIdent is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Spellbook,
                op: 3,
                SimDirectiveStatus.Rejected,
                arcanumIdent);
        }

        _commands.Publish(new ForgetArcanaEngineCmd(arcanumIdent));
        return WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 3,
            SimDirectiveStatus.Accepted,
            arcanumIdent);
    }

    public SimDirectiveResult PersistKnobs(
        SimEpochTicket anticipatedGen)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new SaveToonKnobsEngineCmd());
        return WriteOutcome(
            SimDirectiveDomain.Character,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Recruit(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Fellowship,
                op: 1,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        _commands.Publish(new FellowsRecruitEngineCmd(markOid));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 1,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Dismiss(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Fellowship,
                op: 2,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        _commands.Publish(new FellowsDismissEngineCmd(markOid));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 2,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Quit(
        SimEpochTicket anticipatedGen,
        bool disband)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (_fellowship.RequiresLeaderHandoffPriorQuit(
                _lens.Lifecycle.PlayerGuid,
                disband,
                out uint newLeaderOid))
        {
            _commands.Publish(new FellowsAssignNewLeaderEngineCmd(newLeaderOid));
        }
        _commands.Publish(new FellowsQuitEngineCmd(disband));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 3,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AssignLeader(
        SimEpochTicket anticipatedGen,
        uint newLeaderOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (newLeaderOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Fellowship,
                op: 4,
                SimDirectiveStatus.Rejected,
                newLeaderOid);
        }
        _commands.Publish(new FellowsAssignNewLeaderEngineCmd(newLeaderOid));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 4,
            SimDirectiveStatus.Accepted,
            newLeaderOid);
    }

    public SimDirectiveResult Swear(
        SimEpochTicket anticipatedGen,
        uint patronOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (patronOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Allegiance,
                op: 0,
                SimDirectiveStatus.Rejected,
                patronOid);
        }
        _commands.Publish(new AllegianceSwearEngineCmd(patronOid));
        return WriteOutcome(
            SimDirectiveDomain.Allegiance,
            op: 0,
            SimDirectiveStatus.Accepted,
            patronOid);
    }

    public SimDirectiveResult Break(
        SimEpochTicket anticipatedGen,
        uint markOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (markOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Allegiance,
                op: 1,
                SimDirectiveStatus.Rejected,
                markOid);
        }
        _commands.Publish(new AllegianceBreakEngineCmd(markOid));
        return WriteOutcome(
            SimDirectiveDomain.Allegiance,
            op: 1,
            SimDirectiveStatus.Accepted,
            markOid);
    }

    public SimDirectiveResult Kick(
        SimEpochTicket anticipatedGen,
        uint vassalOid)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (vassalOid is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Allegiance,
                op: 2,
                SimDirectiveStatus.Rejected,
                vassalOid);
        }
        _commands.Publish(new AllegianceKickEngineCmd(vassalOid));
        return WriteOutcome(
            SimDirectiveDomain.Allegiance,
            op: 2,
            SimDirectiveStatus.Accepted,
            vassalOid);
    }

    public SimDirectiveResult ReqDetails(
        SimEpochTicket anticipatedGen,
        string avatarLabel)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new AllegianceInfoAskEngineCmd(avatarLabel ?? string.Empty));
        return WriteOutcome(
            SimDirectiveDomain.Allegiance,
            op: 3,
            SimDirectiveStatus.Accepted);
    }

    public SimTeardownAck Stop(
        SimEpochTicket anticipatedGen)
    {
        var earlier = _lens.Lifecycle.State;
        var latch = Validate(anticipatedGen, demandRealm: false);
        if (latch != SimDirectiveStatus.Accepted)
        {
            return new SimTeardownAck(
                anticipatedGen,
                _lens.Generation,
                latch,
                SimTeardownStage.None);
        }

        var acknowledgement =
            _sessHub.Stop(anticipatedGen);
        _signals.WriteDirective(
            SimDirectiveDomain.Session,
            op: 2,
            acknowledgement.Status,
            phrase: acknowledgement.Error?.GetType().Name ?? string.Empty);
        _signals.WriteLifecycle(earlier, _lens.Lifecycle.State);
        return acknowledgement;
    }

    public SimDirectiveResult ChooseObject(
        SimEpochTicket anticipatedGen,
        uint objectIdent)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition =
            objectIdent is not 0u
            && _satchel.Objects.Get(objectIdent) is not null
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        if (condition == SimDirectiveStatus.Accepted)
        {
            _actions.Selection.Select(
                objectIdent,
                PickChangeSource.Plugin);
        }
        _signals.WriteDirective(
            SimDirectiveDomain.Selection,
            op: 0x100,
            condition,
            objectIdent);
        return Result(condition, objectIdent);
    }

    public SimDirectiveResult Clear(
        SimEpochTicket anticipatedGen)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _actions.Selection.Clear(PickChangeSource.Plugin);
        _signals.WriteDirective(
            SimDirectiveDomain.Selection,
            op: 0x101,
            SimDirectiveStatus.Accepted);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult WipeIntent(
        SimEpochTicket anticipatedGen)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _movement.WipeDirectiveFeed();
        _signals.WriteDirective(
            SimDirectiveDomain.Movement,
            op: 0x101,
            SimDirectiveStatus.Accepted);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult WipeWantedModules(
        SimEpochTicket anticipatedGen)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _toon.WipeDesiredComponents(
            () => _commands.Publish(
                new ClearDesiredComponentsEngineCmd()));
        return WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AttachShortcut(
        SimEpochTicket anticipatedGen,
        in SimHotkeyDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        HotbarSlot listing = new HotbarSlot(
            directive.Index,
            directive.ObjectId,
            directive.SpellId);
        return !_satchel.TryAppendShortcut(
                listing,
                () => _commands.Publish(new AddShortcutEngineCmd(listing)))
            ? WriteOutcome(
                SimDirectiveDomain.InventoryState,
                op: 0,
                SimDirectiveStatus.Rejected,
                directive.ObjectId)
            : WriteOutcome(
            SimDirectiveDomain.InventoryState,
            op: 0,
            SimDirectiveStatus.Accepted,
            directive.ObjectId);
    }

    public SimDirectiveResult AddFavorite(
        SimEpochTicket anticipatedGen,
        int tabOrdinal,
        int locus,
        uint arcanumIdent)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        return !_toon.TryAppendFavorite(
                tabOrdinal,
                locus,
                arcanumIdent,
                () => _commands.Publish(new AddFavoriteEngineCmd(
                    arcanumIdent,
                    locus,
                    tabOrdinal)))
            ? WriteOutcome(
                SimDirectiveDomain.Spellbook,
                op: 0,
                SimDirectiveStatus.Rejected,
                arcanumIdent)
            : WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 0,
            SimDirectiveStatus.Accepted,
            arcanumIdent);
    }

    public SimDirectiveResult DeleteShortcut(
        SimEpochTicket anticipatedGen,
        int ordinal)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        return !_satchel.TryDropShortcut(
                ordinal,
                () => _commands.Publish(
                    new RemoveShortcutEngineCmd((uint)ordinal)))
            ? WriteOutcome(
                SimDirectiveDomain.InventoryState,
                op: 1,
                SimDirectiveStatus.Rejected)
            : WriteOutcome(
            SimDirectiveDomain.InventoryState,
            op: 1,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult RemoveFavorite(
        SimEpochTicket anticipatedGen,
        int tabOrdinal,
        uint arcanumIdent)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        return !_toon.TryDropFavorite(
                tabOrdinal,
                arcanumIdent,
                () => _commands.Publish(
                    new RemoveFavoriteEngineCmd(arcanumIdent, tabOrdinal)))
            ? WriteOutcome(
                SimDirectiveDomain.Spellbook,
                op: 1,
                SimDirectiveStatus.Rejected,
                arcanumIdent)
            : WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 1,
            SimDirectiveStatus.Accepted,
            arcanumIdent);
    }

    public SimDirectiveResult Advance(
        SimEpochTicket anticipatedGen,
        in SimProgressionDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (directive.StatId is 0u
            || directive.Cost is 0u)
        {
            return WriteOutcome(
                SimDirectiveDomain.Character,
                (int)directive.Kind,
                SimDirectiveStatus.Rejected,
                directive.StatId);
        }

        switch (directive.Kind)
        {
            case SimProgressionKind.Attribute:
                _commands.Publish(new RaiseAttributeEngineCmd(
                    directive.StatId,
                    directive.Cost));
                break;
            case SimProgressionKind.Vital:
                _commands.Publish(new RaiseVitalEngineCmd(
                    directive.StatId,
                    directive.Cost));
                break;
            case SimProgressionKind.Skill:
                _commands.Publish(new RaiseSkillEngineCmd(
                    directive.StatId,
                    directive.Cost));
                break;
            case SimProgressionKind.TrainSkill
                when directive.Cost <= uint.MaxValue:
                _commands.Publish(new TrainSkillEngineCmd(
                    directive.StatId,
                    (uint)directive.Cost));
                break;
            default:
                return WriteOutcome(
                    SimDirectiveDomain.Character,
                    (int)directive.Kind,
                    SimDirectiveStatus.Rejected,
                    directive.StatId);
        }

        return WriteOutcome(
            SimDirectiveDomain.Character,
            (int)directive.Kind,
            SimDirectiveStatus.Accepted,
            directive.StatId);
    }

    public SimDirectiveResult Create(
        SimEpochTicket anticipatedGen,
        string fellowshipLabel,
        bool portionXp)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (string.IsNullOrWhiteSpace(fellowshipLabel))
        {
            return WriteOutcome(
                SimDirectiveDomain.Fellowship,
                op: 0,
                SimDirectiveStatus.Rejected);
        }
        _commands.Publish(new FellowsCreateEngineCmd(fellowshipLabel, portionXp));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 0,
            SimDirectiveStatus.Accepted);
    }

    private SimDirectiveResult Result(
        SimDirectiveStatus condition,
        uint objectIdent = 0u) =>
        new(condition, _lens.Generation, objectIdent);

    private SimSessionStartResult RejectedBegin(SimDirectiveStatus condition)
    {
        return new(
            condition == SimDirectiveStatus.StaleGeneration
                ? SimSessionStartStatus.StaleGeneration
                : SimSessionStartStatus.Inactive,
            _lens.Generation);
    }

    private static SimDirectiveStatus ToDirectiveCondition(
        SimSessionStartStatus condition)
    {
        return condition switch
        {
            SimSessionStartStatus.Failed => SimDirectiveStatus.Rejected,
            SimSessionStartStatus.Inactive => SimDirectiveStatus.Inactive,
            SimSessionStartStatus.StaleGeneration =>
                SimDirectiveStatus.StaleGeneration,
            _ => SimDirectiveStatus.Accepted,
        };
    }

    private SimDirectiveStatus Validate(
        SimEpochTicket anticipatedGen,
        bool demandRealm)
    {
        SimDirectiveStatus condition;
        condition = _lens.Lifecycle.State == SimLifespanPhase.Disposed ? SimDirectiveStatus.Inactive : anticipatedGen != _lens.Generation ? SimDirectiveStatus.StaleGeneration : demandRealm && !_session.IsInWorld ? SimDirectiveStatus.Inactive : SimDirectiveStatus.Accepted;

        if (condition != SimDirectiveStatus.Accepted
            && MacAC.Wire.WireTelemetry.SensorNet)
        {
            Console.WriteLine(
                $"[cmd-gate] REJECT status={condition}"
                + $" wanted={anticipatedGen}"
                + $" view={_lens.Generation}"
                + $" lifecycle={_lens.Lifecycle.State}"
                + $" inWorld={_session.IsInWorld}");
        }
        return condition;
    }

    private SimDirectiveResult WriteOutcome(
        SimDirectiveDomain domain,
        int op,
        SimDirectiveStatus condition,
        uint objectIdent = 0u,
        string? phrase = null)
    {
        _signals.WriteDirective(domain, op, condition, objectIdent, phrase);
        return Result(condition, objectIdent);
    }

    private static bool TryLookup(
        SimCommsChannel lane,
        out CommsChannelKind outcome)
    {
        outcome = lane switch
        {
            SimCommsChannel.Say => CommsChannelKind.Say,
            SimCommsChannel.Tell => CommsChannelKind.Tell,
            SimCommsChannel.Fellowship => CommsChannelKind.Fellowship,
            SimCommsChannel.Allegiance => CommsChannelKind.Allegiance,
            SimCommsChannel.AllegianceBroadcast => CommsChannelKind.AllegianceBroadcast,
            SimCommsChannel.Vassals => CommsChannelKind.Vassals,
            SimCommsChannel.Patron => CommsChannelKind.Patron,
            SimCommsChannel.Monarch => CommsChannelKind.Monarch,
            SimCommsChannel.CoVassals => CommsChannelKind.CoVassals,
            SimCommsChannel.General => CommsChannelKind.General,
            SimCommsChannel.Trade => CommsChannelKind.Trade,
            SimCommsChannel.LookingForGroup => CommsChannelKind.Lfg,
            SimCommsChannel.Roleplay => CommsChannelKind.Roleplay,
            SimCommsChannel.Society => CommsChannelKind.Society,
            SimCommsChannel.Olthoi => CommsChannelKind.Olthoi,
            _ => CommsChannelKind.Unknown,
        };
        return outcome != CommsChannelKind.Unknown;
    }
}
