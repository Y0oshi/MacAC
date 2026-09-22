using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Realm;

public sealed partial class SimRealmCrossingLedger
{
    public bool CanFifoWarpBegin(ushort series) =>
        !_teleport.Started || IsNewer(_teleport.LastSequence, series);

    public bool TryFifoWarpBegin(ushort series)
    {
        if (!CanFifoWarpBegin(series))
            return false;

        Prune(series);
        _teleport.Enqueue(series);
        return true;
    }

    public bool EngageQueuedWarp()
    {
        if (!_teleport.Pending)
            return false;

        _teleport.Activate();
        if (_buffered.Remove(_teleport.EngagedSeries, out SimWarpDestination early))
            _teleport.Adopt(early);
        return true;
    }

    public bool OfferWarpDest(
        in SimWarpDestination dest,
        bool warpStampAdvanced)
    {
        ushort series = dest.TeleportSequence;
        if (dest.CellId is 0u)
            return false;

        if (warpStampAdvanced)
            Prune(series);

        if (_teleport.Active && series == _teleport.EngagedSeries)
        {
            if (_teleport.Accepted)
                return false;
            _teleport.Adopt(dest);
            return true;
        }

        if (_teleport.Pending && series == _teleport.QueuedSeries)
        {
            _buffered.TryAdd(series, dest);
            return false;
        }

        ushort reference = _teleport.Pending ? _teleport.QueuedSeries : _teleport.EngagedSeries;
        bool mayBeAhead = (!_teleport.Active && !_teleport.Pending) || IsNewer(reference, series);
        if (warpStampAdvanced && mayBeAhead)
            _buffered.TryAdd(series, dest);
        return false;
    }

    public bool TryFetchApprovedWarpDest(out SimWarpDestination dest)
    {
        dest = _teleport.Destination;
        return _teleport.Active && _teleport.Held;
    }

    public bool CanPlaceGatewayDestination(
        long gen,
        ushort warpSeries,
        uint destChamber) =>
        IsLatestGatewayDest(gen, warpSeries, destChamber);

    public void FinishWarp() => _teleport.End();

    private bool IsLatestGatewayDest(
        long gen,
        ushort warpSeries,
        uint destChamber)
    {
        return _teleport.Active
        && warpSeries == _teleport.EngagedSeries
        && gen is not 0
        && gen == _capture.Generation
        && _capture.Kind == SimPortalKind.Portal
        && !_capture.Cancelled
        && !_capture.Completed
        && destChamber is not 0u
        && destChamber == _capture.DestinationCell;
    }

    // Drops buffered destinations whose sequence is newer than series
    private void Prune(ushort series)
    {
        foreach (ushort buffered in _buffered.Keys.ToArray())
        {
            if (IsNewer(buffered, series))
                _buffered.Remove(buffered);
        }
    }
}
