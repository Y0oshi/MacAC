using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger
{
    public bool TryEnrollHubProj(
        long gen,
        uint destChamber,
        out SimRealmHarborMirrorTicket ticket)
    {
        ticket = new SimRealmHarborMirrorTicket(gen, destChamber);
        if (!ticket.IsValid
            || gen != _capture.Generation
            || destChamber != _capture.DestinationCell
            || _capture.Cancelled
            || _capture.Completed)
        {
            TraceRejected(
                "host-register-mismatch",
                $"generation={gen} "
                + $"cell=0x{destChamber:X8}");
            ticket = default;
            return false;
        }

        if (!_mirrors.TryGetValue(gen, out Mirror? extant))
        {
            _mirrors.Add(gen, new Mirror(ticket));
            return true;
        }

        if (extant.Ticket == ticket)
            return true;

        TraceRejected(
            "host-register-conflict",
            $"generation={gen} "
            + $"expectedCell=0x{extant.Ticket.DestinationCell:X8} "
            + $"actualCell=0x{destChamber:X8}");
        ticket = default;
        return false;
    }

    public bool TryFetchHubProj(
        in SimRealmHarborMirrorTicket ticket,
        out SimRealmHarborMirrorCapture proj)
    {
        if (TryMirror(ticket, out Mirror? mirror))
        {
            proj = mirror.Snapshot;
            return true;
        }

        proj = default;
        return false;
    }

    public bool IsLatestStanceArbiter(
        in SimPortalPlacementAuthority arbiter,
        uint preciseChamberIdent)
    {
        if (!arbiter.Present)
            return arbiter.IsVacant;

        return arbiter.IsValid
            && arbiter.Projection.DestinationCell == preciseChamberIdent
            && IsLatestGatewayDest(
                arbiter.RevealGeneration,
                arbiter.TeleportSequence,
                preciseChamberIdent)
            && TryFetchHubProj(arbiter.Projection, out SimRealmHarborMirrorCapture hub)
            && !hub.IsSuperseding;
    }

    public bool DemandDestReservationFree(in SimRealmHarborMirrorTicket ticket)
    {
        if (!TryMirror(ticket, out Mirror? mirror) || mirror.ReservationReleased)
            return false;

        mirror.Owed |= SimRealmHarborAckStage.DestinationReservationReleased;
        return true;
    }

    public bool CommenceHubProjSupersession(in SimRealmHarborMirrorTicket ticket)
    {
        if (!TryMirror(ticket, out Mirror? mirror))
            return false;

        mirror.Supersede();
        return true;
    }

    public bool AcknowledgeHubProj(in SimRealmHarborAck acknowledgement)
    {
        var juncture = acknowledgement.Stage;
        if (!IsSingleJuncture(juncture)
            || !TryMirror(acknowledgement.Projection, out Mirror? mirror)
            || (mirror.Owed & juncture) == 0)

            return false;

        switch (juncture)
        {
            case SimRealmHarborAckStage.ProjectionRegistered:
                if (mirror.Superseding)
                    return false;
                mirror.Registered = true;
                break;
            case SimRealmHarborAckStage.SimulationReleaseProjected:
                if (mirror.Ticket.Generation == _capture.Generation
                    && !_capture.WorldSimulationAvailable)
                {
                    return false;
                }
                mirror.SimulationReleased = true;
                break;
            case SimRealmHarborAckStage.DestinationReservationReleased:
                mirror.ReservationReleased = true;
                break;
            case SimRealmHarborAckStage.TerminalProjected:
                bool terminal = _capture.Completed || _capture.Cancelled;
                if (!mirror.Superseding
                    && (mirror.Ticket.Generation != _capture.Generation || !terminal))
                {
                    return false;
                }
                if ((mirror.Owed & ~juncture) != 0)
                    return false;
                mirror.Owed &= ~juncture;
                _mirrors.Remove(mirror.Ticket.Generation);
                return true;
            default:
                return false;
        }

        mirror.Owed &= ~juncture;
        return true;
    }

    private bool TryMirror(in SimRealmHarborMirrorTicket ticket, out Mirror mirror)
    {
        if (ticket.IsValid
            && _mirrors.TryGetValue(ticket.Generation, out Mirror? located)
            && located.Ticket == ticket)
        {
            mirror = located;
            return true;
        }

        mirror = null!;
        return false;
    }

    private static bool IsSingleJuncture(SimRealmHarborAckStage juncture)
    {
        int bitset = (int)juncture;
        return bitset is not 0 && (bitset & (bitset - 1)) is 0;
    }
}
