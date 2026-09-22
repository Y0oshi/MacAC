using MacAC.Mechanics.Gear;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.Rigging;

internal sealed partial class DeferredGameEngineStateDirectives
{
    public bool IsInWorld
    {
        get
        {
            lock (_latch)
                return !_deactivated
                    && _lens?.Lifecycle.State == SimLifespanPhase.InWorld;
        }
    }

    public ISimToonPickLens? ToonPick
    {
        get
        {
            lock (_latch)
                return !_deactivated && _lens is not null
                    ? _lens.CharacterSelection
                    : null;
        }
    }

    public ISimToonGenesisLens? ToonCreation
    {
        get
        {
            lock (_latch)
                return !_deactivated && _lens is not null
                    ? _lens.CharacterCreation
                    : null;
        }
    }

    public SimDirectiveResult ToonPickHighlight(uint toonIdent)
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.Highlight(gen, toonIdent));
    }

    public SimDirectiveResult ToonPickJoin()
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.Enter(gen));
    }

    public SimDirectiveResult ToonPickReqErase()
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.ReqErase(gen));
    }

    public SimDirectiveResult ToonPickConfirmErase()
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.ConfirmErase(gen));
    }

    public SimDirectiveResult ToonPickRevert()
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.Restore(gen));
    }

    public SimDirectiveResult ToonPickAbort()
    {
        return Invoke((directives, gen) =>
            directives.CharacterSelection.Cancel(gen));
    }

    public SimDirectiveResult ToonCreationPickLineage(uint lineageIdent)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.PickLineage(gen, lineageIdent));
    }

    public SimDirectiveResult ToonCreationPickGender(uint genderTag)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.PickGender(gen, genderTag));
    }

    public SimDirectiveResult ToonCreationPickBlueprint(uint blueprintOrdinal)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.PickBlueprint(gen, blueprintOrdinal));
    }

    public SimDirectiveResult ToonCreationSetAttr(
        GenesisTraitId attrIdent,
        int val)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AssignAttr(gen, attrIdent, val));
    }

    public SimDirectiveResult ToonCreationSetAttrLock(
        GenesisTraitId attrIdent,
        bool bolted)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AssignAttrLock(gen, attrIdent, bolted));
    }

    public SimDirectiveResult ToonCreationTrainAptitude(uint aptitudeIdent)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.TrainSkill(gen, aptitudeIdent));
    }

    public SimDirectiveResult ToonCreationSpecializeAptitude(uint aptitudeIdent)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.SpecializeAptitude(gen, aptitudeIdent));
    }

    public SimDirectiveResult ToonCreationUntrainAptitude(uint aptitudeIdent)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.UntrainAptitude(gen, aptitudeIdent));
    }

    public SimDirectiveResult ToonCreationPickBeginArea(int beginAreaOrdinal)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.PickBeginArea(gen, beginAreaOrdinal));
    }

    public SimDirectiveResult ToonCreationComplete(bool confirmUnspentCredits)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.Finish(gen, confirmUnspentCredits));
    }

    public SimDirectiveResult ToonCreationSetLooksOrdinal(
        GenesisAppearanceSlot socket,
        uint ordinal)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AssignLooksOrdinal(gen, socket, ordinal));
    }

    public SimDirectiveResult ToonCreationSetShade(
        GenesisShadeSlot socket,
        double val)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AssignShade(gen, socket, val));
    }

    public SimDirectiveResult ToonCreationSetLabel(string label)
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AssignLabel(gen, label));
    }

    public SimDirectiveResult ToonCreationAcknowledgeRejection()
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.AcknowledgeRejection(gen));
    }

    public SimDirectiveResult ToonCreationRandomizeToon()
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.RandomizeToon(gen));
    }

    public SimDirectiveResult ToonCreationRandomizeLooks()
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.RandomizeLooks(gen));
    }

    public SimDirectiveResult ToonCreationRandomizeClothing()
    {
        return Invoke((directives, gen) =>
            directives.CharacterCreation.RandomizeClothing(gen));
    }

    public ISimLinkLens? Connection
    {
        get
        {
            lock (_latch)
                return !_deactivated ? _lens?.Connection : null;
        }
    }

    public IDisposable Bind(
        ISimCoreLens lens,
        ISimCoreDirectives directives)
    {
        ArgumentNullException.ThrowIfNull(lens);
        ArgumentNullException.ThrowIfNull(directives);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_lens is not null || _commands is not null)
            {
                throw new InvalidOperationException(
                    "The retained-UI game-runtime command seam is by now bound");
            }
            _lens = lens;
            _commands = directives;
        }
        return new ExpectedEngineWiring(this, lens, directives);
    }

    public SimDirectiveResult AppendShortcut(HotbarSlot listing)
    {
        return Invoke((directives, gen) => directives.InventoryState.AttachShortcut(
            gen,
            new SimHotkeyDirective(
                listing.Index,
                listing.ObjectId,
                listing.SpellId)));
    }

    public SimDirectiveResult AppendFavorite(
        int tab,
        int locus,
        uint arcanumIdent)
    {
        return Invoke((directives, gen) => directives.Spellbook.AddFavorite(
            gen,
            tab,
            locus,
            arcanumIdent));
    }

    public SimDirectiveResult DropShortcut(uint ordinal)
    {
        return ordinal > int.MaxValue
            ? LatestOutcome(SimDirectiveStatus.Rejected)
            : Invoke((directives, gen) =>
            directives.InventoryState.DeleteShortcut(
                gen,
                (int)ordinal));
    }

    public SimDirectiveResult RemoveFavorite(int tab, uint arcanumIdent)
    {
        return Invoke((directives, gen) => directives.Spellbook.RemoveFavorite(
            gen,
            tab,
            arcanumIdent));
    }

    public SimDirectiveResult AssignGrimoireSift(uint filters)
    {
        return Invoke((directives, gen) => directives.Spellbook.AssignSift(
            gen,
            filters));
    }

    public SimDirectiveResult SetDesiredComponent(
        uint moduleIdent,
        uint quantity)
    {
        return Invoke((directives, gen) =>
            directives.Spellbook.SetDesiredComponent(
                gen,
                moduleIdent,
                quantity));
    }

    public SimDirectiveResult SetTitle(uint bannerIdent)
    {
        return Invoke((directives, gen) => directives.Character.SetTitle(
            gen,
            bannerIdent));
    }

    public SimDirectiveResult DropArcanum(uint arcanumIdent)
    {
        return Invoke((directives, gen) => directives.Spellbook.DiscardArcanum(
            gen,
            arcanumIdent));
    }

    public SimDirectiveResult Advance(
        SimProgressionKind sort,
        uint statIdent,
        ulong price)
    {
        return Invoke((directives, gen) => directives.Character.Advance(
            gen,
            new SimProgressionDirective(sort, statIdent, price)));
    }

    public SimDirectiveResult FellowshipCreate(string fellowshipLabel, bool portionXp)
    {
        return Invoke((directives, gen) => directives.Fellowship.Create(
            gen, fellowshipLabel, portionXp));
    }

    public SimDirectiveResult FellowshipRecruit(uint markOid)
    {
        return Invoke((directives, gen) => directives.Fellowship.Recruit(
            gen, markOid));
    }

    public SimDirectiveResult FellowshipDismiss(uint markOid)
    {
        return Invoke((directives, gen) => directives.Fellowship.Dismiss(
            gen, markOid));
    }

    public SimDirectiveResult FellowshipQuit(bool disband)
    {
        return Invoke((directives, gen) => directives.Fellowship.Quit(
            gen, disband));
    }

    public SimDirectiveResult FellowshipAssignLeader(uint newLeaderOid)
    {
        return Invoke((directives, gen) => directives.Fellowship.AssignLeader(
            gen, newLeaderOid));
    }

    public SimDirectiveResult FellowshipSetOpen(bool isOpen)
    {
        return Invoke((directives, gen) => directives.Fellowship.SetOpen(
            gen, isOpen));
    }

    public SimDirectiveResult FellowshipSetBoardOpen(bool boardOpen)
    {
        return Invoke((directives, gen) => directives.Fellowship.ApplyBoardOpen(
            gen, boardOpen));
    }

    public SimDirectiveResult AllegianceSwear(uint patronOid)
    {
        return Invoke((directives, gen) => directives.Allegiance.Swear(
            gen, patronOid));
    }

    public SimDirectiveResult AllegianceBreak(uint markOid)
    {
        return Invoke((directives, gen) => directives.Allegiance.Break(
            gen, markOid));
    }

    public SimDirectiveResult AllegianceKick(uint vassalOid)
    {
        return Invoke((directives, gen) => directives.Allegiance.Kick(
            gen, vassalOid));
    }

    public SimDirectiveResult AllegianceSetRefreshSubscription(bool on)
    {
        return Invoke((directives, gen) => directives.Allegiance.AssignRefreshSubscription(
            gen, on));
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _lens = null;
            _commands = null;
        }
    }

    private SimDirectiveResult Invoke(
        Func<
            ISimCoreDirectives,
            SimEpochTicket,
            SimDirectiveResult> invoke)
    {
        ISimCoreDirectives directives;
        SimEpochTicket gen;
        lock (_latch)
        {
            if (_deactivated || _lens is null || _commands is null)
            {
                return new SimDirectiveResult(
                    SimDirectiveStatus.Inactive,
                    _lens?.Generation ?? default);
            }
            directives = _commands;
            gen = _lens.Generation;
        }
        return invoke(directives, gen);
    }

    private SimDirectiveResult LatestOutcome(SimDirectiveStatus condition)
    {
        lock (_latch)
        {
            return new SimDirectiveResult(
                condition,
                _lens?.Generation ?? default);
        }
    }

    private void Release(
        ISimCoreLens anticipatedLens,
        ISimCoreDirectives anticipatedDirectives)
    {
        lock (_latch)
        {
            if (!ReferenceEquals(_lens, anticipatedLens)
                || !ReferenceEquals(_commands, anticipatedDirectives))

                return;
            _lens = null;
            _commands = null;
        }
    }
}
