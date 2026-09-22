using MacAC.Client.Link;
using MacAC.Sim;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineDirectiveBridge
{
    public SimDirectiveResult AssignIntent(
        SimEpochTicket anticipatedGen,
        in LocomotionInput feed)
    {
        var latch = Validate(
            anticipatedGen,
            demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _movement.AssignDirectiveFeed(feed);
        _signals.WriteDirective(
            SimDirectiveDomain.Movement,
            op: 0x100,
            SimDirectiveStatus.Accepted);
        return Result(SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AssignSift(
        SimEpochTicket anticipatedGen,
        uint filters)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _toon.ApplyGrimoireSift(
            filters,
            () => _commands.Publish(
                new SetArcanabookFilterEngineCmd(filters)));
        return WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 2,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult SetDesiredComponent(
        SimEpochTicket anticipatedGen,
        uint moduleIdent,
        uint quantity)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        return !_toon.TrySetWantedModule(
                moduleIdent,
                quantity,
                () => _commands.Publish(new SetDesiredComponentEngineCmd(
                    moduleIdent,
                    quantity)))
            ? WriteOutcome(
                SimDirectiveDomain.Spellbook,
                op: 4,
                SimDirectiveStatus.Rejected,
                moduleIdent)
            : WriteOutcome(
            SimDirectiveDomain.Spellbook,
            op: 4,
            SimDirectiveStatus.Accepted,
            moduleIdent);
    }

    public SimDirectiveResult AssignSingleKnob(
        SimEpochTicket anticipatedGen,
        uint knobIdent,
        bool val)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        if (!ToonOptionChart.TryGet(knobIdent, out _))
        {
            return WriteOutcome(
                SimDirectiveDomain.Character,
                op: 4,
                SimDirectiveStatus.Rejected);
        }
        _commands.Publish(new SetSingleToonKnobEngineCmd(knobIdent, val));
        return WriteOutcome(
            SimDirectiveDomain.Character,
            op: 4,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult SetTitle(
        SimEpochTicket anticipatedGen,
        uint bannerIdent)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new SetTitleEngineCmd(bannerIdent));
        return WriteOutcome(
            SimDirectiveDomain.Character,
            op: 6,
            SimDirectiveStatus.Accepted,
            bannerIdent);
    }

    public SimDirectiveResult SetOpen(
        SimEpochTicket anticipatedGen,
        bool isOpen)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new FellowsChangeOpennessEngineCmd(isOpen));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult ApplyBoardOpen(
        SimEpochTicket anticipatedGen,
        bool boardOpen)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new FellowsPulseAskEngineCmd(boardOpen));
        return WriteOutcome(
            SimDirectiveDomain.Fellowship,
            op: 6,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult AssignRefreshSubscription(
        SimEpochTicket anticipatedGen,
        bool on)
    {
        var latch = Validate(anticipatedGen, demandRealm: true);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _commands.Publish(new AllegiancePulseAskEngineCmd(on));
        return WriteOutcome(
            SimDirectiveDomain.Allegiance,
            op: 4,
            SimDirectiveStatus.Accepted);
    }
}
