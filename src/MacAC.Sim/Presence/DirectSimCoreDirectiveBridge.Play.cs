using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Fighting;
using MacAC.Mechanics.Gear;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Targeting;
using MacAC.Mechanics.Arcana;
using MacAC.Sim.Actors;
using MacAC.Sim.Play;

namespace MacAC.Sim.Presence;

public sealed partial class DirectSimCoreDirectiveBridge
{
    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimCommsDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (string.IsNullOrWhiteSpace(directive.Text))
            return Unsupported(
                SimDirectiveDomain.Chat,
                (int)directive.Channel,
                SimDirectiveStatus.Rejected);

        switch (directive.Channel)
        {
            case SimCommsChannel.Say:
                sess!.TransmitTalk(directive.Text);
                break;
            case SimCommsChannel.Tell
                when !string.IsNullOrWhiteSpace(directive.TargetName):
                sess!.TransmitTell(directive.TargetName, directive.Text);
                break;
            default:
                if (!TryTransmitOnLane(sess!, directive.Channel, directive.Text))
                {
                    return Unsupported(
                        SimDirectiveDomain.Chat,
                        (int)directive.Channel);
                }
                break;
        }

        _sim.SignalDrain.WriteDirective(
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
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        switch (directive)
        {
            case SimPortalDirective.RecallLifestone:
                sess!.TransmitWarpToLifestone();
                break;
            case SimPortalDirective.RecallMarketplace:
                sess!.TransmitWarpToMarketplace();
                break;
            case SimPortalDirective.RecallHouse:
                sess!.TransmitWarpToHouse();
                break;
            case SimPortalDirective.RecallMansion:
                sess!.TransmitWarpToMansion();
                break;
            default:
                return Unsupported(
                    SimDirectiveDomain.Portal,
                    (int)directive);
        }

        _sim.SignalDrain.WriteDirective(
            SimDirectiveDomain.Portal,
            (int)directive,
            SimDirectiveStatus.Accepted);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimSelectionDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        var condition = SimDirectiveStatus.Accepted;
        var pick = _sim.ActHolder.Selection;
        switch (directive)
        {
            case SimSelectionDirective.SelectClosestHostile:
                {
                    uint? closest =
                        SimFoeTargetProbe.SeekClosest(_sim);
                    if (closest is { } objectIdent)
                    {
                        pick.Select(
                            objectIdent,
                            PickChangeSource.Keyboard);
                    }
                    else
                    {
                        pick.Clear(
                            PickChangeSource.Keyboard);
                    }
                    break;
                }
            case SimSelectionDirective.SelectPrevious:
                pick.PickEarlier();
                break;
            case SimSelectionDirective.ExamineSelected:
                if (pick.ChosenObjectTag is { } appraisalIdent)
                {
                    _sim.ActHolder.Transactions.TryReqAppraisal(
                        appraisalIdent,
                        sess!.TransmitEvaluate);
                }
                else
                {
                    _sim.ActHolder.Interaction.JoinExamine();
                }
                break;
            case SimSelectionDirective.UseSelected:
                condition = EmployPick(sess!);
                break;
            case SimSelectionDirective.PickUpSelected:
                condition = ChooseUpPick();
                break;
            default:
                condition = SimDirectiveStatus.Unsupported;
                break;
        }

        uint chosen = pick.ChosenObjectTag ?? 0u;
        return Emit(
            SimDirectiveDomain.Selection,
            (int)directive,
            condition,
            chosen);
    }

    public SimDirectiveResult ChooseObject(
        SimEpochTicket anticipatedGen,
        uint objectIdent)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition =
            objectIdent is not 0u
            && _sim.SatchelHolder.Objects.Get(objectIdent) is not null
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        if (condition == SimDirectiveStatus.Accepted)
        {
            _sim.ActHolder.Selection.Select(
                objectIdent,
                PickChangeSource.Plugin);
        }
        return Emit(
            SimDirectiveDomain.Selection,
            op: 0x100,
            condition,
            objectIdent);
    }

    public SimDirectiveResult Clear(
        SimEpochTicket anticipatedGen)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.ActHolder.Selection.Clear(
            PickChangeSource.Plugin);
        return Emit(
            SimDirectiveDomain.Selection,
            op: 0x101,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimFightingDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);

        SimDirectiveStatus condition;
        if (directive == SimFightingDirective.ToggleMode)
        {
            var outcome =
                _sim.ActHolder.CombatMode.Toggle();
            condition = outcome.Status switch
            {
                SimFightingModeRequestStatus.Sent =>
                    SimDirectiveStatus.Accepted,
                SimFightingModeRequestStatus.Inactive =>
                    SimDirectiveStatus.Inactive,
                _ => SimDirectiveStatus.Rejected,
            };
        }
        else
        {
            condition = SimDirectiveStatus.Unsupported;
        }
        return Emit(
            SimDirectiveDomain.Combat,
            (int)directive,
            condition);
    }

    public SimDirectiveResult PerformAssault(
        SimEpochTicket anticipatedGen,
        in SimFightingAttackInput directive)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.ActHolder.CombatAttack.ServiceDirective(directive)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        return Emit(
            SimDirectiveDomain.Combat,
            0x100 + (int)directive.Command,
            condition);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        in SimArcanaDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var casting =
            _sim.ActHolder.SpellCast.Cast(directive.SpellId);
        SimDirectiveStatus condition = casting == CastingRequestOutcome.Sent
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.Magic,
            (int)casting,
            condition,
            directive.SpellId);
    }

    public SimDirectiveResult Execute(
        SimEpochTicket anticipatedGen,
        SimLocomotionDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.MovementOwner.Execute(directive)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        return Emit(
            SimDirectiveDomain.Movement,
            (int)directive,
            condition);
    }

    public SimDirectiveResult PerformLocomotion(
        SimEpochTicket anticipatedGen,
        uint locomotionDirective)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.MovementOwner.RunLocomotion(locomotionDirective)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        return Emit(
            SimDirectiveDomain.Movement,
            op: 0x102,
            condition,
            locomotionDirective);
    }

    public SimDirectiveResult AssignIntent(
        SimEpochTicket anticipatedGen,
        in LocomotionInput feed)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.MovementOwner.AssignDirectiveFeed(feed);
        return Emit(
            SimDirectiveDomain.Movement,
            op: 0x100,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult WipeIntent(
        SimEpochTicket anticipatedGen)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.MovementOwner.WipeDirectiveFeed();
        return Emit(
            SimDirectiveDomain.Movement,
            op: 0x101,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult PivotToBearing(
        SimEpochTicket anticipatedGen,
        float bearingDeg,
        bool enactExecGripTag = false)
    {
        var latch =
            Latch(anticipatedGen, out _);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.MovementOwner.PivotToHeading(
                bearingDeg,
                enactExecGripTag)
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Unsupported;
        return Emit(
            SimDirectiveDomain.Movement,
            op: 0x103,
            condition);
    }

    private SimDirectiveStatus EmployPick(RealmSession sess)
    {
        if (_sim.ActHolder.Selection.ChosenObjectTag
            is not uint chosen)
        {
            _sim.ActHolder.Interaction.JoinUse();
            return SimDirectiveStatus.Accepted;
        }
        if (_sim.SatchelHolder.Objects.Get(chosen)
            is not { } gear)

            return SimDirectiveStatus.Rejected;

        long instantMsec = checked((long)Math.Floor(
            _sim.Clock.SimulationMomentSecs * 1000d));
        if (!_sim.ActHolder.Transactions
                .TryAbsorbUseThrottle(instantMsec))

            return SimDirectiveStatus.Accepted;

        uint useability = gear.Useability ?? GearUseability.Undef;
        if (GearUseability.IsTargeted(useability))
        {
            uint markIdent;
            if (GearUseability.AllowsSelfMark(useability))
                markIdent = _sim.AvatarIdentity.ServerGuid;
            else if (GearUseability.AllowsObjectSelfMark(useability))
                markIdent = chosen;
            else
            {
                _sim.ActHolder.Interaction
                    .JoinUseGearOnMark(chosen);
                return SimDirectiveStatus.Accepted;
            }

            bool sent = _sim.ActHolder.Transactions
                .TryRelayTargetedUse(
                    chosen,
                    markIdent,
                    sess.TransmitUseWithMark,
                    incrementOccupied: true);
            return sent
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        }

        bool possessedByAvatar = PossessedBySelf(gear);
        ItemUseHold reservation;
        try
        {
            reservation = _sim.ActHolder.Transactions
                .OpenUseReqReservation();
        }
        catch (InvalidOperationException)
        {
            return SimDirectiveStatus.Rejected;
        }

        var dispatched =
            _sim.ActHolder.Transactions.TryRelayUse(
                chosen,
                possessedByAvatar,
                possessedByAvatar || GearUseability.IsUseable(useability),
                reservation,
                this,
                out _);
        return dispatched == SimDealingDispatchResult.Dispatched
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Rejected;
    }

    private SimDirectiveStatus ChooseUpPick()
    {
        if (_sim.ActHolder.Selection.ChosenObjectTag
            is not uint chosen)

            return SimDirectiveStatus.Accepted;
        var gear =
            _sim.SatchelHolder.Objects.Get(chosen);
        if (gear is null
            || (gear.Type & GearKind.Creature) != 0)

            return SimDirectiveStatus.Rejected;

        uint avatarOid = _sim.AvatarIdentity.ServerGuid;
        if (avatarOid is 0u)
            return SimDirectiveStatus.Inactive;
        uint ownActorIdent =
            _sim.EntityObjects.Entities.TryFetchEngaged(
                chosen,
                out SimActorRecord capture)
                ? capture.OwnActorTag ?? 0u
                : 0u;
        SimPendingGrab lift = new SimPendingGrab(
            Token: 0u,
            chosen,
            ownActorIdent,
            avatarOid,
            Placement: 0,
            PendingPlacementToken: 0u,
            ApproachToken: default);
        return _sim.ActHolder.Transactions.TryRelayLift(
            lift,
            this,
            out _)
            ? SimDirectiveStatus.Accepted
            : SimDirectiveStatus.Rejected;
    }

    private bool PossessedBySelf(ClientThing gear)
    {
        uint avatarOid = _sim.AvatarIdentity.ServerGuid;
        if (avatarOid is 0u)
            return false;
        ClientThing latest = gear;
        for (int zDepth = 0; zDepth < 16; ++zDepth)
        {
            if (latest.ObjectId == avatarOid
                || latest.WielderIdent == avatarOid
                || latest.VesselTag == avatarOid)

                return true;
            if (latest.VesselTag is 0u
                || _sim.SatchelHolder.Objects.Get(
                    latest.VesselTag) is not { } ancestor)

                return false;
            latest = ancestor;
        }
        return false;
    }
}
