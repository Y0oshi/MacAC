namespace MacAC.Sim.Play;

/// <summary>The outbound interaction queue, drained once per frame.</summary>
public sealed partial class SimDealingTransactionLedger
{
    private readonly Queue<SimQueuedDealing> _outgoing = new();
    private long _relayMisses;

    public int OutgoingTally => _outgoing.Count;
    public long DispatchFailureCount => Interlocked.Read(ref _relayMisses);
    public Exception? LastDispatchFailure { get; private set; }

    public void Enqueue(SimQueuedDealing dealing)
    {
        Live();
        if (dealing.Identity.ServerGuid is 0u)
            return;
        _outgoing.Enqueue(dealing);
        Touch();
    }

    public int AbortQueuedInteractions(uint srvOid, uint? ownActorIdent = null)
    {
        Live();
        if (srvOid is 0u || _outgoing.Count is 0)
            return 0;

        int removed = 0;
        for (int leftover = _outgoing.Count; leftover > 0; --leftover)
        {
            var queued = _outgoing.Dequeue();
            var ident = queued.Identity;
            bool strike = ident.ServerGuid == srvOid && (ownActorIdent is not uint precise || ident.LocalEntityId == precise);
            if (strike)
                ++removed;
            else
                _outgoing.Enqueue(queued);
        }
        if (removed is not 0)
            Touch();
        return removed;
    }

    public void DrainOutbound(Action<SimQueuedDealing> relay)
    {
        Live();
        ArgumentNullException.ThrowIfNull(relay);

        int allowance = _outgoing.Count;
        uint epoch = _wipeEpoch;
        bool any = false;
        while (allowance-- > 0 && epoch == _wipeEpoch && _outgoing.Count > 0)
        {
            var upcoming = _outgoing.Dequeue();
            any = true;
            try
            {
                relay(upcoming);
            }
            catch (Exception problem)
            {
                Interlocked.Increment(ref _relayMisses);
                LastDispatchFailure = problem;
                throw;
            }
        }
        if (any)
            Touch();
    }
}
