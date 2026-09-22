using MacAC.Client.Link;
using MacAC.Cockpit.Input;
using MacAC.Sim;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineDirectiveBridge
{
    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimSelectionDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        FeedAct act = directive switch
        {
            SimSelectionDirective.SelectClosestHostile =>
                FeedAct.SelectionClosestMonster,
            SimSelectionDirective.SelectPrevious =>
                FeedAct.SelectionPreviousSelection,
            SimSelectionDirective.ExamineSelected =>
                FeedAct.SelectionExamine,
            SimSelectionDirective.UseSelected =>
                FeedAct.UseSelected,
            SimSelectionDirective.PickUpSelected =>
                FeedAct.SelectionPickUp,
            _ => FeedAct.None,
        };
        SimDirectiveStatus condition = act != FeedAct.None
            && _pick.ServiceFeedAct(act)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        uint chosen = _actions.Selection.ChosenObjectTag ?? 0u;
        _signals.WriteDirective(
            SimDirectiveDomain.Selection,
            (int)directive,
            condition,
            chosen);
        return Result(condition, chosen);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimFightingDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition;
        if (directive == SimFightingDirective.ToggleMode)
        {
            var outcome =
                _actions.CombatMode.Toggle();
            condition = outcome.Status switch
            {
                SimFightingModeRequestStatus.Sent =>
                    SimDirectiveStatus.Accepted,
                SimFightingModeRequestStatus.Inactive =>
                    SimDirectiveStatus.Inactive,
                _ => SimDirectiveStatus.Rejected,
            };
            if (MacAC.Wire.WireTelemetry.SensorNet)
            {
                Console.WriteLine(
                    $"[cmd-gate] combat toggle result={outcome.Status}"
                    + $" mode={outcome.Mode}");
            }
        }
        else
        {
            condition = SimDirectiveStatus.Unsupported;
        }

        _signals.WriteDirective(SimDirectiveDomain.Combat, (int)directive, condition);
        return Result(condition);
    }

    public SimDirectiveResult PerformAssault(
        SimEpochTicket anticipatedGen,
        in SimFightingAttackInput directive)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition = _actions.CombatAttack.ServiceDirective(
            directive)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        _signals.WriteDirective(
            SimDirectiveDomain.Combat,
            op: 0x100 + (int)directive.Command,
            condition);
        return Result(condition);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimArcanaDirective directive)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        var casting = _actions.SpellCast.Cast(directive.SpellId);
        SimDirectiveStatus condition = casting == CastingRequestOutcome.Sent
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Rejected;
        _signals.WriteDirective(
            SimDirectiveDomain.Magic,
            op: (int)casting,
            condition,
            directive.SpellId);
        return Result(condition, directive.SpellId);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimLocomotionDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition = _movement.Execute(directive)
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Unsupported;

        _signals.WriteDirective(SimDirectiveDomain.Movement, (int)directive, condition);
        return Result(condition);
    }

    public SimDirectiveResult PerformLocomotion(
        SimEpochTicket anticipatedGen,
        uint locomotionDirective)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition = _movement.RunLocomotion(locomotionDirective)
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Unsupported;

        _signals.WriteDirective(
            SimDirectiveDomain.Movement,
            op: 0x102,
            condition,
            locomotionDirective);
        return Result(condition, locomotionDirective);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimCommsDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        if (!TryLookup(directive.Channel, out CommsChannelKind lane))
        {
            _signals.WriteDirective(
                SimDirectiveDomain.Chat,
                (int)directive.Channel,
                SimDirectiveStatus.Unsupported);
            return Result(SimDirectiveStatus.Unsupported);
        }

        _commands.Publish(new SendCommsCmd(
            lane,
            directive.TargetName,
            directive.Text));
        _signals.WriteDirective(
            SimDirectiveDomain.Chat,
            (int)directive.Channel,
            SimDirectiveStatus.Accepted,
            phrase: directive.Text);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimPortalDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        ClientDirectiveId? directiveIdent = directive switch
        {
            SimPortalDirective.RecallLifestone =>
                ClientDirectiveId.LifestoneRecall,
            SimPortalDirective.RecallMarketplace =>
                ClientDirectiveId.MarketplaceRecall,
            SimPortalDirective.RecallHouse =>
                ClientDirectiveId.HouseRecall,
            SimPortalDirective.RecallMansion =>
                ClientDirectiveId.MansionRecall,
            _ => null,
        };
        if (directiveIdent is null)
        {
            _signals.WriteDirective(
                SimDirectiveDomain.Portal,
                (int)directive,
                SimDirectiveStatus.Unsupported);
            return Result(SimDirectiveStatus.Unsupported);
        }

        _commands.Publish(new ExecuteClientDirectiveCmd(
            directiveIdent.Value,
            string.Empty));
        _signals.WriteDirective(
            SimDirectiveDomain.Portal,
            (int)directive,
            SimDirectiveStatus.Accepted);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimBuddyDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var condition = SimDirectiveStatus.Accepted;
        switch (directive.Kind)
        {
            case SimBuddyDirectiveKind.Add
                when !string.IsNullOrWhiteSpace(directive.Name):
                _commands.Publish(new AddBuddyEngineCmd(directive.Name));
                break;
            case SimBuddyDirectiveKind.Remove
                when directive.CharacterId is not 0u:
                _commands.Publish(new RemoveBuddyEngineCmd(
                    directive.CharacterId));
                break;
            case SimBuddyDirectiveKind.Clear:
                _commands.Publish(new ClearBuddysEngineCmd());
                break;
            case SimBuddyDirectiveKind.RequestLegacyList:
                _commands.Publish(new AskLegacyBuddysEngineCmd());
                break;
            default:
                condition = SimDirectiveStatus.Rejected;
                break;
        }

        return WriteOutcome(
            SimDirectiveDomain.Social,
            (int)directive.Kind,
            condition,
            directive.CharacterId,
            directive.Name);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimMuteDirective directive)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var condition = SimDirectiveStatus.Accepted;
        switch (directive.Scope)
        {
            case SimMuteScope.Character
                when directive.CharacterId is not 0u
                    && !string.IsNullOrWhiteSpace(directive.Name):
                _commands.Publish(new ModifyToonMuteEngineCmd(
                    directive.Add,
                    directive.CharacterId,
                    directive.Name,
                    directive.MessageType));
                break;
            case SimMuteScope.Account
                when !string.IsNullOrWhiteSpace(directive.Name):
                _commands.Publish(new ModifyAccountMuteEngineCmd(
                    directive.Add,
                    directive.Name));
                break;
            case SimMuteScope.Global:
                _commands.Publish(new ModifyGlobalMuteEngineCmd(
                    directive.Add,
                    directive.MessageType));
                break;
            default:
                condition = SimDirectiveStatus.Rejected;
                break;
        }

        return WriteOutcome(
            SimDirectiveDomain.Social,
            op: 4 + (int)directive.Scope,
            condition,
            directive.CharacterId,
            directive.Name);
    }
}
