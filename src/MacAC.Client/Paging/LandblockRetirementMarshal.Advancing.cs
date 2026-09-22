namespace MacAC.Client.Paging;

public sealed partial class LandblockRetirementMarshal
{
    public void Advance()
    {
        QueueReq(
        new ClientRequest(AskKind.Advance, 0u));
    }

    public void Advance(PagingWorkMeter gauge)
    {
        ArgumentNullException.ThrowIfNull(gauge);
        if (_proceedExhibitHop is null)
        {
            Advance();
            return;
        }

        while (_queuedOrdering.First is { } front)
        {
            var ticket = front.Value;
            if (ticket.IsComplete)
            {
                DropFinishedTicket(ticket);
                continue;
            }

            if (ProgressBudgetedTicket(ticket, gauge)
                != AllottedAdvanceResult.Progressed)
                return;
            if (ticket.IsComplete)
                DropFinishedTicket(ticket);
        }
    }

    internal void ProgressPrecedence(uint lbIdent, PagingWorkMeter gauge)
    {
        ArgumentNullException.ThrowIfNull(gauge);
        if (_proceedExhibitHop is null)
        {
            Advance();
            return;
        }

        uint canon = Canonicalize(lbIdent);
        while (_queued.TryGetValue(
            canon,
            out List<LandblockSunsetTicket>? tickets))
        {
            if (tickets.Count is 0)
                throw new InvalidOperationException(
                    "A pending retirement owner has no tickets");

            var ticket = tickets[0];
            if (!ticket.IsComplete
                && ProgressBudgetedTicket(ticket, gauge)
                    != AllottedAdvanceResult.Progressed)

                return;

            if (ticket.IsComplete)
                DropFinishedTicket(ticket);
        }
    }

    private void ProgressCore()
    {
        if (_proceedExhibitHop is not null)
        {
            ProgressBudgetedEagerAttempt();
            return;
        }

        if (_queued.Count is 0)
            return;

        _finishedIdents.Clear();
        foreach ((uint ident, List<LandblockSunsetTicket> tickets) in _queued)
        {
            for (int idx = tickets.Count - 1; idx >= 0; --idx)
            {
                var ticket = tickets[idx];
                ProgressTicket(ticket);
                if (!ticket.IsComplete)
                    continue;

                tickets.RemoveAt(idx);
                --QueuedTally;
            }

            if (tickets.Count is 0)
                _finishedIdents.Add(ident);
        }

        for (int idx = 0; idx < _finishedIdents.Count; ++idx)
            _queued.Remove(_finishedIdents[idx]);
    }

    private void ProgressTicket(LandblockSunsetTicket ticket)
    {
        ticket.CommenceAttempt();
        ticket.RunOnce(
            LandblockSunsetJuncture.MeshReferences,
            () => _phase.FreeLbTriMeshReferences(ticket.LandblockId));
        ticket.ExecuteForEachActor(
            LandblockSunsetJuncture.StaticScripts,
            static actor => actor.ServerGuid == 0,
            _phase.HaltStaticActorProgram);
        ticket.RunOnce(
            LandblockSunsetJuncture.Classification,
            () => _phase.DirtyLbTaxonomy(ticket.LandblockId));

        try
        {
            _proceedExhibit(ticket);
        }
        catch (Exception problem)
        {
            ticket.CaptureHookMiss(problem);
        }

        if (ticket.TryGrabNewMiss(out Exception? miss))
        {
            Console.WriteLine(
                $"streaming: retirement for 0x{ticket.LandblockId:X8} " +
                $"({ticket.Kind}) remains pending: {miss}");
        }
    }

    private void ProgressBudgetedEagerAttempt()
    {
        if (_queuedOrdering.First is not { } front)
            return;
        var ticket = front.Value;

        if (ticket.IsComplete)
        {
            DropFinishedTicket(ticket);
            return;
        }

        ProgressTicket(ticket);
        if (ticket.IsComplete)
            DropFinishedTicket(ticket);
    }

    private LandblockSunsetOpOutcome ProgressTicketOne(
        LandblockSunsetTicket ticket)
    {
        ticket.CommenceAttempt();
        var juncture = ticket.UpcomingIncompleteJuncture;
        LandblockSunsetOpOutcome outcome;
        try
        {
            outcome = juncture switch
            {
                LandblockSunsetJuncture.MeshReferences =>
                    ticket.ExecuteOnceHop(
                        juncture,
                        () => _phase.FreeLbTriMeshReferences(
                            ticket.LandblockId)),
                LandblockSunsetJuncture.StaticScripts =>
                    ticket.ExecuteActorHop(
                        juncture,
                        static actor => actor.ServerGuid == 0,
                        _phase.HaltStaticActorProgram),
                LandblockSunsetJuncture.Classification =>
                    ticket.ExecuteOnceHop(
                        juncture,
                        () => _phase.DirtyLbTaxonomy(
                            ticket.LandblockId)),
                LandblockSunsetJuncture.None =>
                    LandblockSunsetOpOutcome.NoWork,
                _ => _proceedExhibitHop!(ticket),
            };
        }
        catch (Exception problem)
        {
            ticket.CaptureHookMiss(problem);
            outcome = LandblockSunsetOpOutcome.Failed;
        }

        if (ticket.TryGrabNewMiss(out Exception? miss))
        {
            Console.WriteLine(
                $"streaming: retirement for 0x{ticket.LandblockId:X8} " +
                $"({ticket.Kind}) remains pending: {miss}");
        }

        return outcome;
    }

    private AllottedAdvanceResult ProgressBudgetedTicket(
        LandblockSunsetTicket ticket,
        PagingWorkMeter gauge)
    {
        var juncture = ticket.UpcomingIncompleteJuncture;
        if (juncture == LandblockSunsetJuncture.None)
        {
            throw new InvalidOperationException(
                "An incomplete retirement ticket has no unfinished stage");
        }

        PagingWorkCost price = new(
            EntityOperations: 1,
            GlRetireOperations:
                juncture is LandblockSunsetJuncture.MeshReferences
                    or LandblockSunsetJuncture.Terrain
                    ? 1
                    : 0);
        var admission = gauge.TryAllocate(
            price,
            $"retire-{juncture}-0x{ticket.LandblockId:X8}");
        if (admission == PagingWorkAdmission.Yielded)
            return AllottedAdvanceResult.Yielded;

        var outcome = ProgressTicketOne(ticket);
        if (outcome == LandblockSunsetOpOutcome.Failed)
        {
            gauge.Fail();
            return AllottedAdvanceResult.Failed;
        }

        gauge.Complete();
        return outcome == LandblockSunsetOpOutcome.Pending ? AllottedAdvanceResult.Yielded : AllottedAdvanceResult.Progressed;
    }
}
