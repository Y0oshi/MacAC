using System.Diagnostics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed partial class PagingDriver
{
    internal bool TryCancelOriginRecenter()
    {
        if (_originRecenterSunset is not { PrepSealed: true })
            return false;

        IsCollapsedToDungeon = false;
        _collapsedMiddle = 0u;
        _zone = null;
        _originRecenterSunset = null;
        return true;
    }

    private bool TryProceedOriginRecenterPrep(PagingWorkMeter gauge)
    {
        ArgumentNullException.ThrowIfNull(gauge);
        OriginRecenterSunset transaction = _originRecenterSunset
            ?? throw new InvalidOperationException(
                "No streaming-origin recenter transaction is pending");
        if (transaction.PrepSealed)
            return true;
        if (_advancingOriginRecenter)
            return false;

        _advancingOriginRecenter = true;
        try
        {
            if (!transaction.RadiiConverged)
            {
                if (_queuedRadiiReconfiguration is not null
                    && !TryExecPagingJob(
                        gauge,
                        new PagingWorkCost(
                            EntityOperations: Math.Max(
                                1,
                                _queuedRadiiReconfiguration.Alterations.Count)),
                        "recenter-radii-convergence",
                        () =>
                        {
                            ProgressRadiiReconfiguration();
                            return _queuedRadiiReconfiguration is null;
                        }))

                    return false;
                if (_queuedRadiiReconfiguration is not null)
                    return false;
                transaction.RadiiConverged = true;
            }
            if (!transaction.GenAdvanced)
            {
                if (!ProgressGen())
                    return false;
                transaction.GenAdvanced = true;
            }
            if (!transaction.QueuedLoadsCleared)
            {
                bool WipeQueuedLoads()
                {
                    try
                    {
                        _wipeQueuedLoads?.Invoke();
                        transaction.QueuedLoadsCleared = true;
                        return true;
                    }
                    catch (PagingMutationException problem) when (problem.MutationSealed)
                    {
                        transaction.QueuedLoadsCleared = true;
                        Console.WriteLine(
                            $"streaming: committed pending-load clear reported failure: {problem}");
                        return false;
                    }
                }

                if (!TryExecPagingJob(
                        gauge,
                        new PagingWorkCost(EntityOperations: 1),
                        "recenter-clear-worker-inbox",
                        WipeQueuedLoads))
                    return false;
            }
            if (!transaction.WrapUpFifoCleared)
            {
                while (_wrapUpFifo.Count is not 0)
                {
                    var admission = gauge.TryAllocate(
                        new PagingWorkCost(EntityOperations: 1),
                        "recenter-release-completion");
                    if (admission == PagingWorkAdmission.Yielded)
                        return false;

                    if (!_wrapUpFifo.TryDropOne())
                    {
                        gauge.Fail();
                        throw new InvalidOperationException(
                            "Completion queue count changed during recenter release");
                    }
                    gauge.Complete();
                }
                transaction.WrapUpFifoCleared = true;
            }
            if (!transaction.ZoneCleared)
            {
                if (!transaction.QueuedPublicationsCleared)
                {
                    bool cancelled = false;
                    if (!TryExecPagingJob(
                            gauge,
                            new PagingWorkCost(EntityOperations: 1),
                            "recenter-cancel-publications",
                            () =>
                            {
                                cancelled =
                                    _exhibit.AbortQueuedPublications();
                                return true;
                            }))

                        return false;
                    transaction.QueuedPublicationsCleared = cancelled;
                    if (!cancelled)
                        return false;
                }
                if (!TryExecPagingJob(
                        gauge,
                        new PagingWorkCost(EntityOperations: 1),
                        "recenter-clear-region",
                        () =>
                        {
                            IsCollapsedToDungeon = false;
                            _zone = null;
                            return true;
                        }))

                    return false;
                transaction.ZoneCleared = true;
            }
            if (!transaction.SpatialGenDetached)
            {
                var admission = gauge.TryAllocate(
                    new PagingWorkCost(
                        EntityOperations:
                            _phase.OriginRecenterSpatialOpTally),
                    "recenter-detach-spatial-generation",
                    secureHeadway: true);
                if (admission == PagingWorkAdmission.Yielded)
                    return false;

                try
                {
                    var detached =
                        _exhibit.UnfastenAllForOriginRecenter();
                    transaction.SpatialGenDetached = true;
                    ++WholePaneSunsetTally;
                    PreviousWholePaneSunsetLbTally =
                        detached.Landblocks.Count;
                    gauge.Complete();
                    if (detached.ObserverFailure is not null)
                    {
                        Console.WriteLine(
                            "streaming: committed origin-recenter spatial " +
                            $"generation reported failure: {detached.ObserverFailure}");
                        return false;
                    }
                }
                catch
                {
                    gauge.Fail();
                    throw;
                }
            }

            transaction.PrepSealed = true;
            return true;
        }
        catch (PagingMutationException problem) when (problem.MutationSealed)
        {
            throw;
        }
        catch (Exception problem)
        {
            Console.WriteLine(
                $"streaming: origin-recenter preparation will resume: {problem}");
            return false;
        }
        finally
        {
            _advancingOriginRecenter = false;
        }
    }

    private void CommenceWholePaneSunset() => _wholePaneSunset ??= new WholePaneSunset();

    private bool TryProceedWholePaneSunset(PagingWorkMeter gauge)
    {
        WholePaneSunset transaction = _wholePaneSunset
            ?? throw new InvalidOperationException(
                "No full-window retirement transaction is pending");
        if (transaction.PrepCommitted)
            return true;

        try
        {
            if (!transaction.GenerationAdvanced)
            {
                if (!ProgressGen())

                    return false;
                transaction.GenerationAdvanced = true;
            }

            if (!transaction.PendingLoadsCleared)
            {
                bool WipeQueuedLoads()
                {
                    try
                    {
                        _wipeQueuedLoads?.Invoke();
                        transaction.PendingLoadsCleared = true;
                        return true;
                    }
                    catch (PagingMutationException problem) when (problem.MutationSealed)
                    {
                        transaction.PendingLoadsCleared = true;
                        Console.WriteLine(
                            $"streaming: committed reload pending-load clear reported failure: {problem}");
                        return false;
                    }
                }

                if (!TryExecPagingJob(
                        gauge,
                        new PagingWorkCost(EntityOperations: 1),
                        "reload-clear-worker-inbox",
                        WipeQueuedLoads))

                    return false;
            }

            if (!transaction.WrapUpQueueCleared)
            {
                while (_wrapUpFifo.Count is not 0)
                {
                    var admission = gauge.TryAllocate(
                        new PagingWorkCost(EntityOperations: 1),
                        "reload-release-completion");
                    if (admission == PagingWorkAdmission.Yielded)
                        return false;

                    if (!_wrapUpFifo.TryDropOne())
                    {
                        gauge.Fail();
                        throw new InvalidOperationException(
                            "Completion queue count changed during reload release");
                    }
                    gauge.Complete();
                }
                transaction.WrapUpQueueCleared = true;
            }

            if (!transaction.RegionCleared)
            {
                if (!transaction.PendingPublicationsCleared)
                {
                    bool cancelled = false;
                    if (!TryExecPagingJob(
                            gauge,
                            new PagingWorkCost(EntityOperations: 1),
                            "reload-cancel-publications",
                            () =>
                            {
                                cancelled =
                                    _exhibit.AbortQueuedPublications();
                                return true;
                            }))

                        return false;
                    transaction.PendingPublicationsCleared = cancelled;
                    if (!cancelled)
                        return false;
                }
                if (!TryExecPagingJob(
                        gauge,
                        new PagingWorkCost(EntityOperations: 1),
                        "reload-clear-region",
                        () =>
                        {
                            IsCollapsedToDungeon = false;
                            _zone = null;
                            return true;
                        }))

                    return false;
                transaction.RegionCleared = true;
            }

            if (transaction.HousedIdents is null)
            {
                transaction.HousedIdents = [];
                transaction.HousedEnumerator =
                    _phase.FetchedLbIdents.GetEnumerator();
            }
            while (transaction.HousedEnumerator is { } housedEnumerator)
            {
                var admission = gauge.TryAllocate(
                    new PagingWorkCost(EntityOperations: 1),
                    "reload-capture-resident-id");
                if (admission == PagingWorkAdmission.Yielded)
                    return false;

                bool moved;
                try
                {
                    moved = housedEnumerator.MoveNext();
                    if (moved)
                        transaction.HousedIdents.Add(housedEnumerator.Current);
                    else
                    {
                        housedEnumerator.Dispose();
                        transaction.HousedEnumerator = null;
                        ++WholePaneSunsetTally;
                        PreviousWholePaneSunsetLbTally =
                            transaction.HousedIdents.Count;
                    }
                    gauge.Complete();
                }
                catch
                {
                    gauge.Fail();
                    throw;
                }
            }

            List<uint> housedIdents = transaction.HousedIdents
                ?? throw new InvalidOperationException(
                    "Full-window resident capture didn't commit");
            while (transaction.SunsetCur < housedIdents.Count)
            {
                uint ident = housedIdents[transaction.SunsetCur];
                int actorTally = _phase.TryFetchLb(
                        ident,
                        out MountedLandblock? fetched)
                    ? fetched!.Entities.Count
                    : 0;
                var admission = gauge.TryAllocate(
                    new PagingWorkCost(
                        EntityOperations: Math.Max(1, actorTally)),
                    $"reload-detach-0x{ident:X8}");
                if (admission == PagingWorkAdmission.Yielded)
                    return false;

                try
                {
                    _exhibit.QueueWholeSunset(ident);
                    transaction.SunsetCur++;
                    gauge.Complete();
                }
                catch (Exception problem)
                {
                    if (!_phase.IsLoaded(ident))
                        transaction.SunsetCur++;
                    gauge.Fail();
                    Console.WriteLine(
                        $"streaming: full-window retirement for 0x{ident:X8} " +
                        $"will resume: {problem}");
                    return false;
                }
            }

            transaction.PrepCommitted = true;
            return true;
        }
        catch (Exception problem)
        {
            Console.WriteLine(
                $"streaming: full-window preparation will resume: {problem}");
            return false;
        }
    }

    private void EmptyAndEnact(bool preferDest = false)
    {
        PagingWorkMeter gauge = _engagedJobGauge
            ?? throw new InvalidOperationException(
                "Completion scheduling needs an active frame meter");

        AdmitCompletions(gauge);
        bool destQueued =
            preferDest
            && _wrapUpFifo.HasPrecedence(
                PagingCompletionPriority.Destination);
        bool executed = false;
        while (_wrapUpFifo.TryGlimpseUpcoming(
            _isBulletinBlockedBySunset,
            out PagingQueuedCompletion? wrapUp,
            destQueued
                ? PagingCompletionPriority.Unload
                : PagingCompletionPriority.Far))
        {
            PagingQueuedCompletion job = wrapUp
                ?? throw new InvalidOperationException(
                    "The completion queue returned a null head");
            try
            {
                using var lane = gauge.JoinLane(
                    job.Priority == PagingCompletionPriority.Destination
                        && job.RevealGeneration == EngagedUnveilGen
                            ? PagingWorkLane.Destination
                            : PagingWorkLane.NonDestination);
                var proceed = ImposeOutcome(
                    job,
                    gauge,
                    secureHeadway: !executed);
                executed |= proceed.Progressed;
                if (!proceed.Completed)
                    break;
                _wrapUpFifo.DropFront(job);
            }
            catch
            {
                if (!_exhibit.HasQueuedBulletin(job.Result))
                    _wrapUpFifo.DropFront(job);
                throw;
            }
        }
    }

    private static bool TryExecPagingJob(
        PagingWorkMeter gauge,
        PagingWorkCost price,
        string juncture,
        Func<bool> op)
    {
        var admission = gauge.TryAllocate(price, juncture);
        if (admission == PagingWorkAdmission.Yielded)
            return false;

        try
        {
            bool finished = op();
            if (finished)
                gauge.Complete();
            else
                gauge.Fail();
            return finished;
        }
        catch
        {
            gauge.Fail();
            throw;
        }
    }

    private void AdmitCompletions(PagingWorkMeter gauge)
    {
        while (_wrapUpSrc.TryPeek(out LandblockFlowOutcome? peeked))
        {
            LandblockFlowOutcome outcome = peeked
                ?? throw new InvalidOperationException(
                    "The completion source returned a null peek");
            bool stale = IsStaleGen(outcome)
                && !_exhibit.HasQueuedBulletin(outcome);
            PagingCompletionPriority precedence = stale
                ? PagingCompletionPriority.Control
                : ClassifyWrapUp(outcome);
            var estimate =
                LandblockFlowOutcomePrice.Estimate(outcome);
            PagingWorkCost admissionPrice = stale
                ? default
                : new PagingWorkCost(
                    CompletionAdmissions:
                        estimate.Work.CompletionAdmissions,
                    AdoptedCpuBytes:
                        estimate.Work.AdoptedCpuBytes);
            using var lane = gauge.JoinLane(
                precedence == PagingCompletionPriority.Destination
                    ? PagingWorkLane.Destination
                    : PagingWorkLane.NonDestination);
            var admission = gauge.TryAllocate(
                admissionPrice,
                "completion-admission");
            if (admission == PagingWorkAdmission.Yielded)
                return;

            try
            {
                if (!_wrapUpSrc.TryRead(
                        out LandblockFlowOutcome? consumed)
                    || !ReferenceEquals(outcome, consumed))
                {
                    throw new InvalidOperationException(
                        "The single-consumer completion source changed between peek and read");
                }

                if (!stale)
                {
                    _wrapUpFifo.Line(new PagingQueuedCompletion(
                        outcome,
                        estimate,
                        precedence,
                        precedence == PagingCompletionPriority.Destination
                            ? EngagedUnveilGen
                            : 0L,
                        outcome.Generation,
                        checked(_upcomingWrapUpSeries++),
                        Stopwatch.GetTimestamp()));
                }
                gauge.Complete();
            }
            catch
            {
                gauge.Fail();
                throw;
            }
        }
    }

    private PagingCompletionPriority ClassifyWrapUp(
        LandblockFlowOutcome outcome)
    {
        return outcome is LandblockFlowOutcome.Fetched
                or LandblockFlowOutcome.Elevated
            && IsDestJob(outcome.LandblockId)
            ? PagingCompletionPriority.Destination
            : outcome switch
            {
                LandblockFlowOutcome.Botched
                    or LandblockFlowOutcome.ClientWorkerCrashed =>
                    PagingCompletionPriority.Control,
                LandblockFlowOutcome.Dropped =>
                    PagingCompletionPriority.Unload,
                LandblockFlowOutcome.Elevated
                    or LandblockFlowOutcome.Fetched
                {
                    Tier: LandblockFlowTier.Near,
                } =>
                    PagingCompletionPriority.Near,
                _ => PagingCompletionPriority.Far,
            };
    }

    private static LandblockBulletinProceed ExecuteSimpleOutcomeOp(
        PagingWorkMeter gauge,
        string juncture,
        bool secureHeadway,
        Action op)
    {
        var admission = gauge.TryAllocate(
            default,
            juncture,
            secureHeadway);
        if (admission == PagingWorkAdmission.Yielded)
            return new LandblockBulletinProceed(false, false);

        try
        {
            op();
            gauge.Complete();
            return new LandblockBulletinProceed(true, true);
        }
        catch
        {
            gauge.Fail();
            throw;
        }
    }

    private LandblockBulletinProceed ImposeOutcome(
        PagingQueuedCompletion job,
        PagingWorkMeter gauge,
        bool secureHeadway)
    {
        var outcome = job.Result;
        if (_exhibit.HasQueuedBulletin(outcome))
        {
            var resumed =
                _exhibit.ReactivateBulletin(
                    outcome,
                    gauge,
                    secureHeadway);
            if (resumed.Completed)
                SettleFinishedQueuedBulletin(outcome.LandblockId);
            return resumed;
        }

        if (IsStaleGen(outcome))
        {
            return ExecuteSimpleOutcomeOp(
                gauge,
                "execute-stale-generation",
                secureHeadway,
                static () => { });
        }

        if (outcome is LandblockFlowOutcome.Dropped
            && _zone?.TryFetchWantedTier(outcome.LandblockId, out _) == true)
        {
            return ExecuteSimpleOutcomeOp(
                gauge,
                "execute-reowned-unload",
                secureHeadway,
                static () => { });
        }

        if (outcome is LandblockFlowOutcome.Fetched
                or LandblockFlowOutcome.Elevated)
        {
            if (_zone is null
                || !_zone.TryFetchWantedTier(outcome.LandblockId, out var wantedTier))
            {
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-undesired-load",
                    secureHeadway,
                    static () => { });
            }

            bool isNearbyWrapUp = outcome is LandblockFlowOutcome.Elevated
                or LandblockFlowOutcome.Fetched { Tier: LandblockFlowTier.Near };
            if (isNearbyWrapUp
                && _phase.IsNearbyTier(outcome.LandblockId)
                && !_exhibit.HasQueuedBulletin(outcome))
            {
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-duplicate-near",
                    secureHeadway,
                    static () => { });
            }
            if (wantedTier == LandblockFlowTier.Far && isNearbyWrapUp)
            {
                if (!_phase.IsLoaded(outcome.LandblockId))
                {
                    switch (outcome)
                    {
                        case LandblockFlowOutcome.Fetched fetched:
                            return _exhibit.BroadcastAsFaraway(
                                fetched,
                                fetched.Build,
                                fetched.MeshData,
                                gauge,
                                secureHeadway);
                        case LandblockFlowOutcome.Elevated promoted:
                            return _exhibit.BroadcastAsFaraway(
                                promoted,
                                promoted.Build,
                                promoted.MeshData,
                                gauge,
                                secureHeadway);
                    }
                }
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-already-far",
                    secureHeadway,
                    static () => { });
            }
        }

        switch (outcome)
        {
            case LandblockFlowOutcome.Fetched fetched:
                if (fetched.Tier == LandblockFlowTier.Far
                    && _phase.IsNearbyTier(fetched.LandblockId))
                {
                    return ExecuteSimpleOutcomeOp(
                        gauge,
                        "execute-stale-far-tier",
                        secureHeadway,
                        static () => { });
                }
                return _exhibit.BroadcastFetched(
                    fetched,
                    job.Estimate,
                    gauge,
                    secureHeadway);
            case LandblockFlowOutcome.Elevated promoted:
                return _exhibit.BroadcastPromoted(
                    promoted,
                    combineIntoExtantLb:
                        _phase.IsLoaded(promoted.LandblockId),
                    job.Estimate,
                    gauge,
                    secureHeadway);
            case LandblockFlowOutcome.Dropped unloaded:
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-unload",
                    secureHeadway,
                    () => _exhibit.QueueWholeSunset(
                        unloaded.LandblockId));
            case LandblockFlowOutcome.Botched failed:
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-load-failure",
                    secureHeadway,
                    () => Console.WriteLine(
                        $"streaming: load failed for 0x{failed.LandblockId:X8}: {failed.Error}"));
            case LandblockFlowOutcome.ClientWorkerCrashed crashed:
                return ExecuteSimpleOutcomeOp(
                    gauge,
                    "execute-worker-crash",
                    secureHeadway,
                    () => Console.WriteLine(
                        $"streaming: worker CRASHED: {crashed.Error}"));
            default:
                throw new InvalidOperationException(
                    $"Not supported streaming result {outcome.GetType().Name}.");
        }
    }
}
