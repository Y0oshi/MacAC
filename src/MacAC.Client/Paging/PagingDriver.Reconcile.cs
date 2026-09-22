namespace MacAC.Client.Paging;

public sealed partial class PagingDriver
{
    private void SettleFinishedQueuedBulletin(uint lbIdent)
    {
        if (_zone is null
            || !_zone.TryFetchWantedTier(lbIdent, out LandblockFlowTier wantedTier))
        {
            if (_phase.IsLoaded(lbIdent))
                _exhibit.QueueWholeSunset(lbIdent);
            return;
        }

        if (wantedTier == LandblockFlowTier.Far
            && _phase.IsNearbyTier(lbIdent))

            _exhibit.QueueNearbyStratumSunset(lbIdent);
    }

    private bool IsStaleGen(LandblockFlowOutcome outcome)
    {
        return outcome is not LandblockFlowOutcome.ClientWorkerCrashed
        && outcome.Generation != _gen;
    }

    private bool IsBulletinBlockedBySunset(LandblockFlowOutcome outcome)
    {
        return outcome is LandblockFlowOutcome.Fetched or LandblockFlowOutcome.Elevated
        && _exhibit.IsSunsetQueued(outcome.LandblockId);
    }

    private static uint OutcomeLbIdent(LandblockFlowOutcome outcome) => outcome.LandblockId;

    public int PostponedEnactBacklog => _wrapUpFifo.Count;

    public int QueuedRetirementCount => _exhibit.QueuedSunsetTally;

    public PagingWorkTelemetry JobTelemetry
    {
        get
        {
            var queued =
                _wrapUpFifo.GrabSnapshot();
            return new PagingWorkTelemetry(
                _previousJobGauge,
                _lifespanJobOverruns,
                _lifespanOversizedHeadway,
                _ceilingJobCycleMillis,
                _ceilingJobCycleJuncture,
                _ceilingJobOpMillis,
                _ceilingJobOpJuncture,
                queued.Count,
                queued.RetainedCpuBytes,
                queued.OldestAgeMilliseconds,
                _exhibit.QueuedBulletinTally,
                _exhibit.QueuedSunsetTally,
                _wrapUpSrc.BacklogTally,
                queued.Destination,
                queued.Control,
                queued.Unload,
                queued.Near,
                queued.Far);
        }
    }

    internal bool IsCollapsedToDungeon { get; private set; }

    public int WholePaneSunsetTally { get; private set; }

    public int PreviousWholePaneSunsetLbTally { get; private set; }
}
