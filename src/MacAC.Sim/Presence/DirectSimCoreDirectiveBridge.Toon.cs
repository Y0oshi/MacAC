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
    public SimDirectiveResult AttachShortcut(
        SimEpochTicket anticipatedGen,
        in SimHotkeyDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        HotbarSlot listing = new HotbarSlot(
            directive.Index,
            directive.ObjectId,
            directive.SpellId);
        SimDirectiveStatus condition =
            _sim.SatchelHolder.TryAppendShortcut(
                listing,
                () => sess!.TransmitAppendShortcut(listing))
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.InventoryState,
            op: 0,
            condition,
            directive.ObjectId);
    }

    public SimDirectiveResult DeleteShortcut(
        SimEpochTicket anticipatedGen,
        int ordinal)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.SatchelHolder.TryDropShortcut(
                ordinal,
                () => sess!.TransmitDropShortcut((uint)ordinal))
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.InventoryState,
            op: 1,
            condition);
    }

    public SimDirectiveResult AddFavorite(
        SimEpochTicket anticipatedGen,
        int tabOrdinal,
        int locus,
        uint arcanumIdent)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.ToonHolder.TryAppendFavorite(
                tabOrdinal,
                locus,
                arcanumIdent,
                () => sess!.TransmitAppendArcanumFavorite(
                    arcanumIdent,
                    locus,
                    tabOrdinal))
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 0,
            condition,
            arcanumIdent);
    }

    public SimDirectiveResult RemoveFavorite(
        SimEpochTicket anticipatedGen,
        int tabOrdinal,
        uint arcanumIdent)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.ToonHolder.TryDropFavorite(
                tabOrdinal,
                arcanumIdent,
                () => sess!.TransmitDropArcanumFavorite(
                    arcanumIdent,
                    tabOrdinal))
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 1,
            condition,
            arcanumIdent);
    }

    public SimDirectiveResult AssignSift(
        SimEpochTicket anticipatedGen,
        uint filters)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.ToonHolder.ApplyGrimoireSift(
            filters,
            () => sess!.TransmitGrimoireSift(filters));
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 2,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult DiscardArcanum(
        SimEpochTicket anticipatedGen,
        uint arcanumIdent)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition = arcanumIdent is 0u
            ? SimDirectiveStatus.Rejected
            : SimDirectiveStatus.Accepted;
        if (condition == SimDirectiveStatus.Accepted)
            sess!.TransmitDropArcanum(arcanumIdent);
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 3,
            condition,
            arcanumIdent);
    }

    public SimDirectiveResult SetDesiredComponent(
        SimEpochTicket anticipatedGen,
        uint moduleIdent,
        uint quantity)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        SimDirectiveStatus condition =
            _sim.ToonHolder.TrySetWantedModule(
                moduleIdent,
                quantity,
                () => sess!.TransmitSetWantedModuleTier(
                    moduleIdent,
                    quantity))
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Rejected;
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 4,
            condition,
            moduleIdent);
    }

    public SimDirectiveResult WipeWantedModules(
        SimEpochTicket anticipatedGen)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.ToonHolder.WipeDesiredComponents(
            sess!.TransmitWipeWantedModules);
        return Emit(
            SimDirectiveDomain.Spellbook,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult Advance(
        SimEpochTicket anticipatedGen,
        in SimProgressionDirective directive)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        var condition = SimDirectiveStatus.Accepted;
        if (directive.StatId is 0u || directive.Cost is 0u)
        {
            condition = SimDirectiveStatus.Rejected;
        }
        else
        {
            switch (directive.Kind)
            {
                case SimProgressionKind.Attribute:
                    sess!.TransmitEmitAttr(
                        directive.StatId,
                        directive.Cost);
                    break;
                case SimProgressionKind.Vital:
                    sess!.TransmitEmitVital(
                        directive.StatId,
                        directive.Cost);
                    break;
                case SimProgressionKind.Skill:
                    sess!.TransmitEmitAptitude(
                        directive.StatId,
                        directive.Cost);
                    break;
                case SimProgressionKind.TrainSkill
                    when directive.Cost <= uint.MaxValue:
                    sess!.TransmitTrainAptitude(
                        directive.StatId,
                        (uint)directive.Cost);
                    break;
                default:
                    condition = SimDirectiveStatus.Rejected;
                    break;
            }
        }
        return Emit(
            SimDirectiveDomain.Character,
            (int)directive.Kind,
            condition,
            directive.StatId);
    }

    public SimDirectiveResult AssignSingleKnob(
        SimEpochTicket anticipatedGen,
        uint knobIdent,
        bool val)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        bool approved = _sim.ToonHolder.Options.TrySetKnob(
            knobIdent,
            val,
            transmitAutoPersist: sess!.TransmitSetSingleToonKnob);
        return Emit(
            SimDirectiveDomain.Character,
            op: 4,
            approved ? SimDirectiveStatus.Accepted : SimDirectiveStatus.Rejected);
    }

    public SimDirectiveResult PersistKnobs(
        SimEpochTicket anticipatedGen)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        _sim.ToonHolder.Options.TryDrain(() =>
        {
            var echo = ToonOptionsBlobSource.Capture(
                _sim.ToonHolder,
                _sim.SatchelHolder.Shortcuts);
            sess!.TransmitSetToonKnobs(
                echo.Options1,
                echo.Options2,
                echo.Shortcuts,
                echo.FavoriteSpells,
                echo.DesiredComponents,
                echo.SpellbookFilters);
        });
        return Emit(
            SimDirectiveDomain.Character,
            op: 5,
            SimDirectiveStatus.Accepted);
    }

    public SimDirectiveResult SetTitle(
        SimEpochTicket anticipatedGen,
        uint bannerIdent)
    {
        var latch =
            Latch(anticipatedGen, out RealmSession? sess);
        if (latch != SimDirectiveStatus.Accepted)
            return Result(latch);
        sess!.TransmitSetBanner(bannerIdent);
        return Emit(
            SimDirectiveDomain.Character,
            op: 6,
            SimDirectiveStatus.Accepted,
            bannerIdent);
    }
}
