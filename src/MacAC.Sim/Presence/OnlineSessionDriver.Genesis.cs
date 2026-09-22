using System.Net;
using System.Net.Sockets;
using MacAC.Mechanics.Genesis;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public sealed partial class OnlineSessionDriver
{

    public SimDirectiveResult PickLineage(
        SimEpochTicket anticipatedGen,
        uint lineageIdent)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryPickLineage(lineageIdent));
        }
    }

    public SimDirectiveResult PickGender(
        SimEpochTicket anticipatedGen,
        uint genderTag)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryPickGender(genderTag));
        }
    }

    public SimDirectiveResult PickBlueprint(
        SimEpochTicket anticipatedGen,
        uint blueprintOrdinal)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryPickBlueprint(blueprintOrdinal));
        }
    }

    public SimDirectiveResult AssignAttr(
        SimEpochTicket anticipatedGen,
        GenesisTraitId attrIdent,
        int val)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TrySetAttr(attrIdent, val));
        }
    }

    public SimDirectiveResult AssignAttrLock(
        SimEpochTicket anticipatedGen,
        GenesisTraitId attrIdent,
        bool bolted)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TrySetAttrMutex(attrIdent, bolted));
        }
    }

    public SimDirectiveResult TrainSkill(
        SimEpochTicket anticipatedGen,
        uint aptitudeIdent)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TryTrainAptitude(aptitudeIdent));
        }
    }

    public SimDirectiveResult SpecializeAptitude(
        SimEpochTicket anticipatedGen,
        uint aptitudeIdent)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TrySpecializeAptitude(aptitudeIdent));
        }
    }

    public SimDirectiveResult UntrainAptitude(
        SimEpochTicket anticipatedGen,
        uint aptitudeIdent)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TryUntrainAptitude(aptitudeIdent));
        }
    }

    public SimDirectiveResult AssignLooksOrdinal(
        SimEpochTicket anticipatedGen,
        GenesisAppearanceSlot socket,
        uint ordinal)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TrySetLooksOrdinal(socket, ordinal));
        }
    }

    public SimDirectiveResult AssignShade(
        SimEpochTicket anticipatedGen,
        GenesisShadeSlot socket,
        double val)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TrySetShade(socket, val));
        }
    }

    public SimDirectiveResult PickBeginArea(
        SimEpochTicket anticipatedGen,
        int beginAreaOrdinal)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryPickBeginArea(beginAreaOrdinal));
        }
    }

    public SimDirectiveResult AssignLabel(
        SimEpochTicket anticipatedGen,
        string label)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TrySetLabel(label ?? string.Empty));
        }
    }

    public SimDirectiveResult AssignSocket(
        SimEpochTicket anticipatedGen,
        uint socket)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(ToonCreationPhase.TrySetSocket(socket));
        }
    }

    public SimDirectiveResult Finish(
        SimEpochTicket anticipatedGen,
        bool confirmUnspentCredits = false)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);

            Scope ambit = _ambit!;
            var pick = ToonPickPhase.Snapshot;
            if (!ToonCreationPhase.TryCommenceComplete(
                    pick.RosterCount,
                    pick.SlotCount,
                    out CharacterForge.WireRequest req,
                    out uint[] aptitudeAdvancementClasses,
                    out _,
                    confirmUnspentCredits))

                return GenesisOutcome(SimDirectiveStatus.Rejected);

            try
            {
                _ops.BuildToon(
                    ambit.Session,
                    pick.AccountName,
                    req,
                    aptitudeAdvancementClasses);
                return GenesisOutcome(SimDirectiveStatus.Accepted);
            }
            catch (Exception problem) when (
                problem is InvalidOperationException
                    or System.Net.Sockets.SocketException)
            {
                ToonCreationPhase.ImposeCreationResponse(
                    new GenesisVerdict.Parsed(
                        (uint)GenesisVerdict.Opcode.Undef,
                        null,
                        null,
                        null));
                return GenesisOutcome(SimDirectiveStatus.Rejected);
            }
        }
    }

    public SimDirectiveResult AcknowledgeRejection(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryAcknowledgeRejection());
        }
    }

    public SimDirectiveResult RandomizeToon(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryRandomizeToon());
        }
    }

    public SimDirectiveResult RandomizeLooks(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryRandomizeLooks());
        }
    }

    public SimDirectiveResult RandomizeClothing(
        SimEpochTicket anticipatedGen)
    {
        lock (_latch)
        {
            var latch = LatchGenesis(anticipatedGen);
            if (latch != SimDirectiveStatus.Accepted)
                return GenesisOutcome(latch);
            return GenesisOutcome(
                ToonCreationPhase.TryRandomizeClothing());
        }
    }
    private void OnGenesisVerdict(
        Scope ambit,
        ulong gen,
        GenesisVerdict.Parsed response)
    {
        ToonCreationPhase.ImposeCreationResponse(response);
        var creation = ToonCreationPhase.Snapshot;

        if (creation.LastCreated is { } built)
            ambit.Host.ImposeToonBuilt(built);
        else if (creation.LastRejection is { } rejection)
            ambit.Host.ImposeCreationFailed(rejection);

        if (creation.LastCreated is not { } persona)
            return;

        var prior = ToonPickPhase.Snapshot;
        var listings = new List<OnlineSessionRosterEntry>(prior.RosterCount + 1);
        ToonPickPhase.View.Call(new LineupGatherer(listings));
        listings.Add(new OnlineSessionRosterEntry(
            persona.Guid,
            persona.Name,
            SecondsGreyedOut: 0u));
        var dossier = new OnlineSessionRosterNotice(
            prior.AccountName,
            prior.SlotCount,
            listings);

        int wireOrdinal =
            _ops.FetchToons(ambit.Session)?.Characters.Count
                is int stashedWireTally
            ? stashedWireTally + _createsSinceLineup
            : prior.RosterCount;
        ++_createsSinceLineup;
        ToonPickPhase.AffixBuiltToon(
            persona.Guid,
            persona.Name,
            wireOrdinal);
        ambit.Host.AnnounceLineup(dossier);
        if (!AmbitHolds(ambit, gen))
            return;

        if (!ToonPickPhase.TryHighlight(persona.Guid))
            return;
        if (!AmbitHolds(ambit, gen))
            return;

        _ = JoinBuilt(persona);
    }

    private SimDirectiveStatus LatchGenesis(
        SimEpochTicket anticipatedGen)
    {
        SimEpochTicket latest = new(_epoch);
        if (anticipatedGen != latest)
            return SimDirectiveStatus.StaleGeneration;
        if (_destroyed || _teardownAsked || _ambit is null || _inRealm)
            return SimDirectiveStatus.Inactive;
        return ToonPickPhase.Snapshot.Lifecycle
            == SimToonPickLifespan.AwaitingSelection
                ? SimDirectiveStatus.Accepted
                : SimDirectiveStatus.Inactive;
    }

    private SimDirectiveResult GenesisOutcome(bool approved)
    {
        return GenesisOutcome(
            approved ? SimDirectiveStatus.Accepted : SimDirectiveStatus.Rejected);
    }

    private SimDirectiveResult GenesisOutcome(SimDirectiveStatus condition) =>
        new(condition, new SimEpochTicket(_epoch));
}
