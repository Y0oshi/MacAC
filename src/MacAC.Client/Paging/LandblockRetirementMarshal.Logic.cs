namespace MacAC.Client.Paging;

public sealed partial class LandblockRetirementMarshal
{
    public int QueuedTally { get; private set; }

    public bool IsQueued(uint lbIdent) =>
        _queued.ContainsKey(Canonicalize(lbIdent));

    internal bool UsesBudgetedHops => _proceedExhibitHop is not null;

    public void BeginFull(uint lbIdent)
    {
        QueueReq(
        new ClientRequest(AskKind.BeginFull, Canonicalize(lbIdent)));
    }

    public void BeginNearLayer(uint lbIdent)
    {
        QueueReq(
        new ClientRequest(AskKind.BeginNearLayer, Canonicalize(lbIdent)));
    }

    internal bool FitsPhase(GpuRealmPhase phase) =>
        ReferenceEquals(_phase, phase);

    internal Exception? AdoptDetachedWhole(
        IReadOnlyList<GpuLandblockSunset> retirements)
    {
        ArgumentNullException.ThrowIfNull(retirements);
        HashSet<uint> incomingIdents = new HashSet<uint>();
        for (int ordinal = 0; ordinal < retirements.Count; ++ordinal)
        {
            var sunset = retirements[ordinal];
            if (sunset.Kind != LandblockSunsetFlavor.Full)
            {
                throw new ArgumentException(
                    "Origin-recenter adoption accepts only full retirements",
                    nameof(retirements));
            }

            uint canon = Canonicalize(sunset.LandblockId);
            if (!incomingIdents.Add(canon))
            {
                throw new ArgumentException(
                    $"Origin-recenter receipts contain duplicate landblock " +
                    $"0x{canon:X8}.",
                    nameof(retirements));
            }
            if (_queued.TryGetValue(
                    canon,
                    out List<LandblockSunsetTicket>? extant)
                && extant.Any(
                    static ticket =>
                        ticket.Kind == LandblockSunsetFlavor.Full))
            {
                throw new InvalidOperationException(
                    $"Landblock 0x{canon:X8} by now has a full " +
                    "retirement receipt");
            }
        }

        var needed =
            CoreJunctures
            | _neededExhibitJunctures(LandblockSunsetFlavor.Full);
        List<Exception>? misses = null;
        for (int ordinal = 0; ordinal < retirements.Count; ++ordinal)
        {
            var sunset = retirements[ordinal];
            uint canon = Canonicalize(sunset.LandblockId);
            _queued.TryGetValue(
                canon,
                out List<LandblockSunsetTicket>? extant);

            var ticket = new LandblockSunsetTicket(
                sunset,
                needed);
            if (extant is null)
            {
                extant = new List<LandblockSunsetTicket>(1);
                _queued.Add(canon, extant);
            }
            extant.Add(ticket);
            ++QueuedTally;
            if (_proceedExhibitHop is not null)
                QueueBudgetedTicket(ticket);

            try
            {
                _onDetached?.Invoke(sunset);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }

            if (_proceedExhibitHop is null)
            {
                ProgressTicket(ticket);
                if (ticket.IsComplete)
                {
                    extant.Remove(ticket);
                    --QueuedTally;
                    if (extant.Count is 0)
                        _queued.Remove(canon);
                }
            }
        }

        return misses switch
        {
            null => null,
            { Count: 1 } => misses[0],
            _ => new AggregateException(
                "One or more detached landblock observers failed",
                misses),
        };
    }

    internal static LandblockRetirementMarshal BuildBudgeted(
        GpuRealmPhase phase,
        Func<LandblockSunsetTicket, LandblockSunsetOpOutcome>
            proceedExhibitHop,
        Action<LandblockSunsetTicket> proceedExhibit,
        Action<GpuLandblockSunset>? onDetached = null)
    {
        return new LandblockRetirementMarshal(
            phase,
            proceedExhibitHop,
            proceedExhibit,
            sort => sort == LandblockSunsetFlavor.Full
                ? ProductionExhibitJunctures
                : ProductionExhibitJunctures & ~LandblockSunsetJuncture.Terrain,
            onDetached);
    }

    internal static LandblockRetirementMarshal BuildLegacy(
        GpuRealmPhase phase,
        Action<uint>? dropLand,
        Action<uint>? demoteNearbyStratum)
    {
        LandblockSunsetJuncture Needed(LandblockSunsetFlavor sort) =>
            sort switch
            {
                LandblockSunsetFlavor.Full when dropLand is not null =>
                    LandblockSunsetJuncture.LegacyPresentation,
                LandblockSunsetFlavor.NearLayer when demoteNearbyStratum is not null =>
                    LandblockSunsetJuncture.LegacyPresentation,
                _ => LandblockSunsetJuncture.None,
            };

        void ProgressLegacy(LandblockSunsetTicket ticket)
        {
            Action<uint>? hook = ticket.Kind == LandblockSunsetFlavor.Full
                ? dropLand
                : demoteNearbyStratum;
            if (hook is not null)
            {
                ticket.RunOnce(
                    LandblockSunsetJuncture.LegacyPresentation,
                    () => hook(ticket.LandblockId));
            }
        }

        return new LandblockRetirementMarshal(phase, ProgressLegacy, Needed);
    }

    private static uint Canonicalize(uint ident) =>
        (ident & 0xFFFF0000u) | 0xFFFFu;

    private void CommenceCore(uint lbIdent, LandblockSunsetFlavor sort)
    {
        uint canon = Canonicalize(lbIdent);
        if (_queued.TryGetValue(canon, out List<LandblockSunsetTicket>? extant))
        {
            for (int idx = 0; idx < extant.Count; ++idx)
            {
                if (extant[idx].Kind == sort)
                {
                    var extantTicket = extant[idx];
                    if (_proceedExhibitHop is not null)
                        return;

                    ProgressTicket(extantTicket);
                    if (extantTicket.IsComplete)
                    {
                        extant.RemoveAt(idx);
                        --QueuedTally;
                        if (extant.Count is 0)
                            _queued.Remove(canon);
                    }
                    return;
                }
            }
        }

        var needed =
            CoreJunctures | _neededExhibitJunctures(sort);
        GpuLandblockSunset? phaseSunset = null;
        Exception? detachmentWatcherMiss = null;
        var alteration = _phase.CommenceAlterationLot();
        try
        {
            phaseSunset = sort == LandblockSunsetFlavor.Full
                ? _phase.UnfastenLb(canon)
                : _phase.UnfastenNearbyStratum(canon);
        }
        finally
        {
            try
            {
                alteration.Dispose();
            }
            catch (Exception problem)
            {
                detachmentWatcherMiss = problem;
            }
        }
        if (phaseSunset is null)
        {
            if (detachmentWatcherMiss is not null)
                throw detachmentWatcherMiss;
            return;
        }

        var ticket = new LandblockSunsetTicket(phaseSunset, needed);
        if (extant is null)
        {
            extant = new List<LandblockSunsetTicket>(1);
            _queued.Add(canon, extant);
        }
        extant.Add(ticket);
        ++QueuedTally;
        if (_proceedExhibitHop is not null)
            QueueBudgetedTicket(ticket);

        _onDetached?.Invoke(phaseSunset);

        if (_proceedExhibitHop is null)
        {
            ProgressTicket(ticket);
            if (ticket.IsComplete)
            {
                extant.Remove(ticket);
                --QueuedTally;
                if (extant.Count is 0)
                    _queued.Remove(canon);
            }
        }
        if (detachmentWatcherMiss is not null)
            throw detachmentWatcherMiss;
    }

    private void QueueBudgetedTicket(LandblockSunsetTicket ticket)
    {
        var joint =
            _queuedOrdering.AddLast(ticket);
        if (!_queuedJoints.TryAdd(ticket, joint))
        {
            _queuedOrdering.Remove(joint);
            throw new InvalidOperationException(
                "A retirement ticket can't enter the budgeted FIFO twice");
        }
    }

    private void QueueReq(ClientRequest req)
    {
        if (_drainingReqs)
        {
            if (_observedDuringEmpty.Add(req))
                _reqs.Enqueue(req);
            return;
        }

        _drainingReqs = true;
        _observedDuringEmpty.Clear();
        _reqs.Clear();
        _observedDuringEmpty.Add(req);
        _reqs.Enqueue(req);
        List<Exception>? misses = null;
        try
        {
            while (_reqs.TryDequeue(out ClientRequest queued))
            {
                try
                {
                    switch (queued.Kind)
                    {
                        case AskKind.BeginFull:
                            CommenceCore(
                                queued.LandblockId,
                                LandblockSunsetFlavor.Full);
                            break;
                        case AskKind.BeginNearLayer:
                            CommenceCore(
                                queued.LandblockId,
                                LandblockSunsetFlavor.NearLayer);
                            break;
                        case AskKind.Advance:
                            ProgressCore();
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Unrecognized retirement request {queued.Kind}.");
                    }
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(problem);
                }
            }
        }
        finally
        {
            _reqs.Clear();
            _observedDuringEmpty.Clear();
            _drainingReqs = false;
        }

        if (misses is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(misses[0])
                .Throw();
        if (misses is { Count: > 1 })
        {
            throw new AggregateException(
                "One or more serialized landblock retirement requests failed",
                misses);
        }
    }

    private void DropFinishedTicket(LandblockSunsetTicket ticket)
    {
        if (!_queuedJoints.Remove(
                ticket,
                out LinkedListNode<LandblockSunsetTicket>? joint))
        {
            throw new InvalidOperationException(
                "Completed retirement ticket is absent from its budgeted order");
        }
        _queuedOrdering.Remove(joint);

        uint ident = Canonicalize(ticket.LandblockId);
        if (!_queued.TryGetValue(ident, out List<LandblockSunsetTicket>? tickets)
            || !tickets.Remove(ticket))
        {
            throw new InvalidOperationException(
                "Completed retirement ticket is absent from its owner ledger");
        }

        --QueuedTally;
        if (tickets.Count is 0)
            _queued.Remove(ident);
    }
}
