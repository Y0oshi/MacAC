namespace MacAC.Client.Paging;

public sealed partial class PagingDriver
{
    public int NearRadius { get; private set; }

    /// <summary>Far-tier radius (LBs from observer that load terrain only).</summary>
    public int FarRadius { get; private set; }

    public int UpperCompletionsPerCycle
    {
        get;
        set
        {
            var profile =
                _configuredJobAllowanceKnobs
                    .ResizeForLegacyWrapUpTally(value);
            field = value;
            _jobAllowance = profile.ToAllowance();
        }
    } = 4;

    internal long EngagedUnveilGen =>
        _destReservation?.RevealGeneration ?? 0L;

    internal uint DestLbIdent =>
        _destReservation?.LandblockId ?? 0u;

    internal int DestRadius =>
        _destReservation?.Radius ?? 0;

    public bool IsRasterizeNeighborhoodHoused(
        uint chamberOrLbIdent,
        int nearRadius,
        int farRadius)
    {
        if (nearRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(nearRadius));
        if (farRadius < nearRadius)
        {
            throw new ArgumentOutOfRangeException(
                nameof(farRadius),
                "Far radius has to be greater than or equal to near radius");
        }

        if (!MacAC.Mechanics.Kinetics.MechLandDefs.IncomingValidChamberIdent(chamberOrLbIdent))
            return false;
        int cx = (int)((chamberOrLbIdent >> 24) & 0xFFu);
        int cy = (int)((chamberOrLbIdent >> 16) & 0xFFu);
        for (int dx = -farRadius; dx <= farRadius; ++dx)
            for (int dy = -farRadius; dy <= farRadius; ++dy)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx < 0 || nx > 254 || ny < 0 || ny > 254)
                    continue;

                uint canon = ((uint)nx << 24) | ((uint)ny << 16) | 0xFFFFu;
                if (!_exhibit.IsLbExhibitPrimed(canon))
                    return false;
                if (!_phase.IsRasterizePrimed(canon))
                    return false;
                bool isInteriorLoop = Math.Abs(dx) <= nearRadius
                    && Math.Abs(dy) <= nearRadius;
                if (isInteriorLoop && !_phase.IsNearbyTier(canon))
                    return false;
            }

        return true;
    }

    public void ReconfigureRadii(int nearRadius, int farRadius)
    {
        if (nearRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(nearRadius));
        if (farRadius < nearRadius)
            throw new ArgumentOutOfRangeException(
                nameof(farRadius),
                "Far radius has to be greater than or equal to near radius");

        if (_originRecenterSunset is not null)
        {
            _postponedRadiiReq = (nearRadius, farRadius);
            return;
        }

        if (_advancingRadiiReconfiguration)
        {
            _postponedRadiiReq = (nearRadius, farRadius);
            return;
        }

        if (_queuedRadiiReconfiguration is not null)
            ProgressRadiiReconfiguration();

        _postponedRadiiReq = null;

        if (nearRadius == NearRadius && farRadius == FarRadius)
            return;

        if (!ConvergeQueuedPublications())
        {
            _postponedRadiiReq = (nearRadius, farRadius);
            return;
        }

        if (IsCollapsedToDungeon || _zone is null)
        {
            NearRadius = nearRadius;
            FarRadius = farRadius;
            return;
        }

        PagingRegion rebuilt = new PagingRegion(
            _zone.CenterX,
            _zone.CenterY,
            nearRadius,
            farRadius);
        ClientTwoTierDiff bootstrap = rebuilt.ComputeFirstTickDiff();
        HashSet<uint> wantedNearby = new HashSet<uint>(bootstrap.ToLoadNear);
        HashSet<uint> wantedFaraway = new HashSet<uint>(bootstrap.ToLoadFar);
        List<RadiiAlteration> alterations = new List<RadiiAlteration>();

        uint[] fetched = [.. _phase.FetchedLbIdents];
        for (int idx = 0; idx < fetched.Length; ++idx)
        {
            uint ident = fetched[idx];
            if (wantedNearby.Contains(ident))
            {
                if (!_phase.IsNearbyTier(ident))
                    alterations.Add(new RadiiAlteration(
                        () => QueuePull(ident, LandblockFlowJobFlavor.PromoteToNear)));
            }
            else if (wantedFaraway.Contains(ident))
            {
                if (_phase.IsNearbyTier(ident))
                    alterations.Add(new RadiiAlteration(() => DemoteLb(ident)));
            }
            else
            {
                alterations.Add(new RadiiAlteration(() => QueueUnload(ident)));
            }
        }

        foreach (uint ident in wantedNearby)
            if (!_phase.IsLoaded(ident))
                alterations.Add(new RadiiAlteration(
                    () => QueuePull(ident, LandblockFlowJobFlavor.LoadNear)));
        foreach (uint ident in wantedFaraway)
            if (!_phase.IsLoaded(ident))
                alterations.Add(new RadiiAlteration(
                    () => QueuePull(ident, LandblockFlowJobFlavor.LoadFar)));

        _queuedRadiiReconfiguration = new ClientRadiiReconfiguration(
            nearRadius,
            farRadius,
            rebuilt,
            alterations);
        ProgressRadiiReconfiguration();
    }

    public void Tick(int watcherCx, int watcherCy, bool insideDungeon = false)
    {
        if (_engagedJobGauge is not null)
            throw new InvalidOperationException(
                "StreamingController.Tick can't be reentered");

        bool destGrip = _destReservation is not null;
        PagingWorkMeter gauge = new PagingWorkMeter(
            destGrip
                ? _jobAllowance.WidenForDestGrip(
                    _configuredJobAllowanceKnobs
                        .HoldDestinationCeilingMilliseconds)
                : _jobAllowance,
            _jobStamp,
            _jobStampFrequency,
            destReservationEngaged: destGrip);
        _engagedJobGauge = gauge;
        try
        {
            if (_wholePaneSunset is not null)
            {
                bool sunsetWasQueued =
                    _exhibit.QueuedSunsetTally is not 0;
                TryProceedWholePaneSunset(gauge);
                ProgressDestSunsetDep(gauge);
                if (_exhibit.UsesBudgetedSunsetHops
                    || sunsetWasQueued)

                    _exhibit.AdvanceRetirements(gauge);
                if (_wholePaneSunset is
                    {
                        PrepCommitted: true,
                    }
                    && _exhibit.QueuedSunsetTally is 0)

                    _wholePaneSunset = null;
                return;
            }

            if (_originRecenterSunset is not null)
            {
                bool sunsetWasQueued =
                    _exhibit.QueuedSunsetTally is not 0;
                TryProceedOriginRecenterPrep(gauge);
                ProgressDestSunsetDep(gauge);
                if (_exhibit.UsesBudgetedSunsetHops
                    || sunsetWasQueued)

                    _exhibit.AdvanceRetirements(gauge);
                return;
            }

            if (_queuedRadiiReconfiguration is not null)
                ProgressRadiiReconfiguration();
            else if (_postponedRadiiReq is { } postponed)
            {
                _postponedRadiiReq = null;
                ReconfigureRadii(postponed.NearRadius, postponed.FarRadius);
            }

            bool sunsetWasQueuedAtCycleBegin =
                _exhibit.QueuedSunsetTally is not 0;
            ProgressDestSunsetDep(gauge);
            _exhibit.AdvanceRetirements(gauge);
            bool destBulletinIncomplete =
                _destReservation is not null
                && !IsRasterizeNeighborhoodHoused(
                    DestLbIdent,
                    Math.Min(NearRadius, DestRadius),
                    DestRadius);
            if (!ConvergeQueuedPublications(
                    preferDest: destBulletinIncomplete))
                return;

            uint middleIdent = PagingRegion.PackLbIdent(watcherCx, watcherCy);

            if (IsCollapsedToDungeon)
            {
                if (insideDungeon && middleIdent != _collapsedMiddle)
                    JoinDungeonFold(watcherCx, watcherCy, middleIdent);
                else if (!insideDungeon && ChebyshevLbs(middleIdent, _collapsedMiddle) > 1)
                    QuitDungeonWiden(watcherCx, watcherCy);
                else
                    SweepCollapsed();
            }
            else if (insideDungeon)
            {
                JoinDungeonFold(watcherCx, watcherCy, middleIdent);
            }
            else
            {
                NormBeat(watcherCx, watcherCy);
            }

            EmptyAndEnact(
                preferDest: destBulletinIncomplete);
            if (_exhibit.UsesBudgetedSunsetHops
                && !sunsetWasQueuedAtCycleBegin)
                _exhibit.AdvanceRetirements(gauge);
        }
        finally
        {
            gauge.CompleteCycle();
            var capture = gauge.Snapshot;
            WatchJobLifespan(capture);
            _previousJobGauge = capture;
            _engagedJobGauge = null;
            if (BulletinTimingSensor.Enabled)
            {
                BulletinTimingSensor.WatchPagingBeat(
                    capture,
                    _wrapUpSrc.BacklogTally,
                    _wrapUpFifo.Count);
            }
        }
    }

    public void BootstrapRecognizedSigninMiddle(int cx, int cy, bool isSealedDungeon)
    {
        if (isSealedDungeon)
            PreFoldToDungeon(cx, cy);
    }

    /// <summary>Pins streaming to a sealed dungeon before the first normal Tick.</summary>
    public void PreFoldToDungeon(int cx, int cy)
    {
        uint middleIdent = PagingRegion.PackLbIdent(cx, cy);
        if (IsCollapsedToDungeon && _collapsedMiddle == middleIdent) return;
        JoinDungeonFold(cx, cy, middleIdent);
    }

    public void ForceReloadPane() => CommenceWholePaneSunset();

    internal void CommenceDestReservation(
        long revealGeneration,
        uint destinationCell,
        int requiredRenderRadius)
    {
        if (revealGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(revealGeneration));
        if (destinationCell is 0u)
            throw new ArgumentOutOfRangeException(nameof(destinationCell));
        if (requiredRenderRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredRenderRadius));

        _destReservation = new DestReservation(
            revealGeneration,
            (destinationCell & 0xFFFF0000u) | 0xFFFFu,
            requiredRenderRadius);
    }

    internal void FinishDestReservation(long unveilGen)
    {
        if (_destReservation is { } engaged
            && engaged.RevealGeneration == unveilGen)

            _destReservation = null;
    }

    internal bool IsLbExhibitPrimed(uint lbIdent) =>
        _exhibit.IsLbExhibitPrimed(lbIdent);

    internal void BeginOriginRecenter() => _originRecenterSunset ??= new OriginRecenterSunset();

    internal bool IsOriginRecenterRetirementComplete()
    {
        return _originRecenterSunset is null
            ? throw new InvalidOperationException(
                "No streaming-origin recenter transaction is pending")
            : _originRecenterSunset.PrepSealed;
    }

    internal bool TryCommitOriginRecenter(
        int destX,
        int destY,
        bool isSealedDungeon)
    {
        if (_advancingOriginRecenter)
            return false;

        _advancingOriginRecenter = true;
        try
        {
            return TrySealOriginRecenterCore(
                destX,
                destY,
                isSealedDungeon);
        }
        finally
        {
            _advancingOriginRecenter = false;
        }
    }

    void IRealmRevealPagingRota.OpenDestReservation(
        long unveilGen,
        uint destChamber,
        int neededRasterizeRadius)
    {
        CommenceDestReservation(
            unveilGen,
            destChamber,
            neededRasterizeRadius);
    }

    void IRealmRevealPagingRota.ConcludeDestReservation(
        long unveilGen) =>
        FinishDestReservation(unveilGen);

    private void QueueUnload(uint ident) => _queueUnload(ident, _gen);

    private void DemoteLb(uint ident)
    {
        uint canon = (ident & 0xFFFF0000u) | 0xFFFFu;
        _exhibit.QueueNearbyStratumSunset(canon);
    }

    private bool ProgressGen()
    {
        if (!ConvergeQueuedPublications())
            return false;
        _gen = unchecked(_gen + 1);
        return true;
    }

    private bool ConvergeQueuedPublications(
        bool preferDest = false)
    {
        var queued =
            _exhibit.FetchQueuedBulletinOutcomes();
        if (queued.Count is 0)
            return true;
        if (_engagedJobGauge is null)

            return false;

        try
        {
            bool progressed = false;
            bool destQueued = false;
            if (preferDest)
            {
                for (int idx = 0; idx < queued.Count; ++idx)
                {
                    if (!IsDestJob(queued[idx].LandblockId))
                        continue;
                    destQueued = true;
                    break;
                }
            }
            for (int destPass = 1; destPass >= 0; --destPass)
            {
                bool demandDest = destPass is not 0;
                for (int idx = 0; idx < queued.Count; ++idx)
                {
                    var outcome = queued[idx];
                    bool isDest = IsDestJob(outcome.LandblockId);
                    if (destQueued && !isDest)
                        continue;
                    if (isDest != demandDest)
                        continue;

                    using var lane =
                        _engagedJobGauge.JoinLane(
                            isDest
                                ? PagingWorkLane.Destination
                                : PagingWorkLane.NonDestination);
                    var proceed =
                        _exhibit.ReactivateBulletin(
                            outcome,
                            _engagedJobGauge,
                            secureHeadway: !progressed);
                    progressed |= proceed.Progressed;
                    if (!proceed.Completed)
                        return false;
                }
            }
            return true;
        }
        finally
        {
            _wrapUpFifo.DropOutcomes(
                queued,
                result => !_exhibit.HasQueuedBulletin(result));
        }
    }

    private void ProgressDestSunsetDep(
        PagingWorkMeter gauge)
    {
        if (_destReservation is null
            || !_exhibit.IsSunsetQueued(DestLbIdent))

            return;

        using var lane =
            gauge.JoinLane(PagingWorkLane.Destination);
        _exhibit.ProgressPrecedenceSunset(
            DestLbIdent,
            gauge);
    }

    private void WatchJobLifespan(PagingWorkMeterCapture capture)
    {
        _lifespanJobOverruns = SaturatingAppend(
            _lifespanJobOverruns,
            capture.OverrunCount);
        _lifespanOversizedHeadway = SaturatingAppend(
            _lifespanOversizedHeadway,
            capture.OversizedProgressCount);

        if (_ceilingJobCycleJuncture is null
            || capture.ElapsedMilliseconds > _ceilingJobCycleMillis)
        {
            _ceilingJobCycleMillis = capture.ElapsedMilliseconds;
            _ceilingJobCycleJuncture = capture.LastStage;
        }

        if (_ceilingJobOpJuncture is null
            || capture.MaximumOperationMilliseconds
                > _ceilingJobOpMillis)
        {
            _ceilingJobOpMillis =
                capture.MaximumOperationMilliseconds;
            _ceilingJobOpJuncture = capture.MaximumOperationStage;
        }
    }

    private static long SaturatingAppend(long left, int right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private void ProgressRadiiReconfiguration()
    {
        if (_advancingRadiiReconfiguration)
            return;

        ClientRadiiReconfiguration queued = _queuedRadiiReconfiguration
            ?? throw new InvalidOperationException("No radii reconfiguration is pending");
        List<Exception> misses = new List<Exception>();
        _advancingRadiiReconfiguration = true;
        try
        {

            if (!queued.GenerationAdvanced)
            {
                if (!ProgressGen())
                    return;
                queued.GenerationAdvanced = true;
            }

            if (!queued.PendingLoadsCleared)
            {
                try
                {
                    _wipeQueuedLoads?.Invoke();
                    queued.PendingLoadsCleared = true;
                }
                catch (Exception problem)
                {
                    if (problem is PagingMutationException { MutationSealed: true })
                        queued.PendingLoadsCleared = true;
                    misses.Add(problem);
                }
            }

            for (int idx = 0; queued.PendingLoadsCleared && idx < queued.Alterations.Count; ++idx)
            {
                var alteration = queued.Alterations[idx];
                if (alteration.Completed)
                    continue;
                try
                {
                    alteration.Apply();
                    alteration.Completed = true;
                }
                catch (Exception problem)
                {
                    if (problem is PagingMutationException { MutationSealed: true })
                        alteration.Completed = true;
                    misses.Add(problem);
                }
            }

            bool converged = queued.PendingLoadsCleared
                && queued.Alterations.All(static mutation => mutation.Completed);
            if (converged)
            {
                queued.Region.MarkResidentFromBootstrap();
                _zone = queued.Region;
                NearRadius = queued.NearRadius;
                FarRadius = queued.FarRadius;
                _wrapUpFifo.Clear();
                _queuedRadiiReconfiguration = null;
            }
        }
        finally
        {
            _advancingRadiiReconfiguration = false;
        }

        if (misses.Count is not 0)
        {
            throw new AggregateException(
                "Streaming quality reconfiguration didn't complete cleanly",
                misses);
        }

        if (_queuedRadiiReconfiguration is null
            && _postponedRadiiReq is { } postponed)
        {
            _postponedRadiiReq = null;
            ReconfigureRadii(postponed.NearRadius, postponed.FarRadius);
        }
    }

    // Outdoor / building-interior streaming - the original two-tier model
    private void NormBeat(int watcherCx, int watcherCy)
    {
        if (_zone is null)
        {
            _zone = new PagingRegion(watcherCx, watcherCy, NearRadius, FarRadius);
            ClientTwoTierDiff bootstrap = _zone.ComputeFirstTickDiff();
            QueueLoadsByUnveilPrecedence(
                bootstrap.ToLoadNear,
                LandblockFlowJobFlavor.LoadNear);
            foreach (var ident in bootstrap.ToLoadFar) QueuePull(ident, LandblockFlowJobFlavor.LoadFar);
            _zone.MarkResidentFromBootstrap();
        }
        else if (_zone.CenterX != watcherCx || _zone.CenterY != watcherCy)
        {
            ClientTwoTierDiff diff = _zone.RecenterTo(watcherCx, watcherCy);
            QueueLoadsByUnveilPrecedence(
                diff.ToPromote,
                LandblockFlowJobFlavor.PromoteToNear);
            QueueLoadsByUnveilPrecedence(
                diff.ToLoadNear,
                LandblockFlowJobFlavor.LoadNear);
            foreach (var ident in diff.ToLoadFar) QueuePull(ident, LandblockFlowJobFlavor.LoadFar);
            foreach (var ident in diff.ToDemote) DemoteLb(ident);
            foreach (var ident in diff.ToUnload) QueueUnload(ident);
        }
    }

    private void QueueLoadsByUnveilPrecedence(
        IReadOnlyList<uint> lbIdents,
        LandblockFlowJobFlavor sort,
        bool skipFetched = false)
    {
        for (int destPass = 1; destPass >= 0; --destPass)
        {
            bool demandDest = destPass is not 0;
            for (int idx = 0; idx < lbIdents.Count; ++idx)
            {
                uint ident = lbIdents[idx];
                if ((!skipFetched || !_phase.IsLoaded(ident))
                    && IsDestJob(ident) == demandDest)

                    QueuePull(ident, sort);
            }
        }
    }

    private void JoinDungeonFold(int cx, int cy, uint middleIdent)
    {
        bool traceChangeover = !IsCollapsedToDungeon || _collapsedMiddle != middleIdent;
        if (traceChangeover)
            Console.WriteLine($"streaming: dungeon collapse -> 0x{middleIdent:X8}");
        if (!ProgressGen())
            return;
        IsCollapsedToDungeon = true;
        _collapsedMiddle = middleIdent;
        _wipeQueuedLoads?.Invoke();
        _wrapUpFifo.Clear();
        _zone = null;

        foreach (var ident in _phase.FetchedLbIdents)
            if (ident != middleIdent) QueueUnload(ident);

        _zone = new PagingRegion(cx, cy, 0, 0);
        _zone.MarkResidentFromBootstrap();

        if (!_phase.IsLoaded(middleIdent))
            QueuePull(middleIdent, LandblockFlowJobFlavor.LoadNear);
        else if (!_phase.IsNearbyTier(middleIdent))
            QueuePull(middleIdent, LandblockFlowJobFlavor.PromoteToNear);
    }

    private void SweepCollapsed()
    {
        foreach (var ident in _phase.FetchedLbIdents)
            if (ident != _collapsedMiddle) QueueUnload(ident);
    }

    private static int ChebyshevLbs(uint a, uint b)
    {
        int ax = (int)((a >> 24) & 0xFFu), ay = (int)((a >> 16) & 0xFFu);
        int bx = (int)((b >> 24) & 0xFFu), by = (int)((b >> 16) & 0xFFu);
        return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
    }

    private bool IsDestJob(uint ident)
    {
        return _destReservation is { } reservation
               && ChebyshevLbs(ident, reservation.LandblockId)
                    <= reservation.Radius;
    }

    private void QuitDungeonWiden(int watcherCx, int watcherCy)
    {
        Console.WriteLine(
            $"streaming: dungeon EXIT-expand -> ({watcherCx},{watcherCy}) " +
            $"(was collapsed on 0x{_collapsedMiddle:X8})");
        if (!ProgressGen())
            return;
        IsCollapsedToDungeon = false;
        PagingRegion rebuilt = new PagingRegion(watcherCx, watcherCy, NearRadius, FarRadius);

        foreach (var ident in _phase.FetchedLbIdents)
            if (!rebuilt.Resident.Contains(ident)) QueueUnload(ident);

        ClientTwoTierDiff boot = rebuilt.ComputeFirstTickDiff();
        QueueLoadsByUnveilPrecedence(
            boot.ToLoadNear,
            LandblockFlowJobFlavor.LoadNear,
            skipFetched: true);
        foreach (var ident in boot.ToLoadFar)
            if (!_phase.IsLoaded(ident)) QueuePull(ident, LandblockFlowJobFlavor.LoadFar);
        rebuilt.MarkResidentFromBootstrap();
        _zone = rebuilt;
    }

    private bool TrySealOriginRecenterCore(
        int destX,
        int destY,
        bool isSealedDungeon)
    {
        OriginRecenterSunset transaction = _originRecenterSunset
            ?? throw new InvalidOperationException(
                "No streaming-origin recenter transaction is pending");
        if (!transaction.PrepSealed)
            throw new InvalidOperationException(
                "Streaming-origin retirement preparation hasn't completed");
        var dest = (destinationX: destX, destinationY: destY, isSealedDungeon);
        if (transaction.Destination is { } kept && kept != dest)
        {
            throw new InvalidOperationException(
                "A recenter transaction can't commit two different destinations");
        }
        transaction.Destination ??= dest;

        if (!transaction.DestConfigured)
        {
            IsCollapsedToDungeon = isSealedDungeon;
            _collapsedMiddle = isSealedDungeon
                ? PagingRegion.PackLbIdent(destX, destY)
                : 0u;
            if (isSealedDungeon)
            {
                _zone = new PagingRegion(
                    destX,
                    destY,
                    nearbyRadius: 0,
                    farawayRadius: 0);
                _zone.MarkResidentFromBootstrap();
            }
            else
            {
                _zone = null;
            }
            transaction.DestConfigured = true;
        }

        if (isSealedDungeon && !transaction.DestPullEnqueued)
        {
            try
            {
                QueuePull(
                    PagingRegion.PackLbIdent(destX, destY),
                    LandblockFlowJobFlavor.LoadNear);
                transaction.DestPullEnqueued = true;
            }
            catch (PagingMutationException problem) when (problem.MutationSealed)
            {
                transaction.DestPullEnqueued = true;
                Console.WriteLine(
                    $"streaming: committed dungeon recenter enqueue reported failure: {problem}");
                return false;
            }
            catch (Exception problem)
            {
                Console.WriteLine(
                    $"streaming: dungeon recenter enqueue will resume: {problem}");
                return false;
            }
        }

        _originRecenterSunset = null;
        return true;
    }
}
