using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Paging;

public sealed partial class LandblockDisplayPipeline
{
    private readonly LandblockRenderHerald? _rasterizePublisher;

    internal LandblockRenderHerald? RasterizePublisher => _rasterizePublisher;
    public IReadOnlyCollection<StructureRegistry> StructureRegistries
    {
        get
        {
            return _rasterizePublisher?.BuildingRegistries ?? Array.Empty<StructureRegistry>();
        }
    }

    public LandblockDisplayTelemetry Diagnostics
    {
        get
        {
            return new(
        _rasterizePublisher?.Diagnostics ?? default,
        _kineticsPublisher?.Diagnostics ?? default,
        _staticPublisher?.Diagnostics ?? default);
        }
    }

    public int QueuedSunsetTally => _retirements.QueuedTally;

    internal bool UsesBudgetedSunsetHops => _retirements.UsesBudgetedHops;

    public int QueuedBulletinTally => _publications.Count;

    public void RestartBulletin(LandblockFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!_publications.TryGetValue(outcome, out BulletinTransaction? transaction))
            return;
        Advance(outcome, transaction, gauge: null, secureHeadway: false);
    }

    public bool IsSunsetQueued(uint lbIdent) =>
        _retirements.IsQueued(lbIdent);

    public bool HasQueuedBulletin(LandblockFlowOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return _publications.ContainsKey(outcome);
    }

    public IReadOnlyList<LandblockFlowOutcome> FetchQueuedBulletinOutcomes() =>
        _publications.Keys.ToArray();

    public void CommenceWholeSunset(uint lbIdent)
    {
        _retirements.BeginFull(lbIdent);
        if (_retirements.UsesBudgetedHops)
        {
            _retirements.Advance();
            if (_retirements.IsQueued(lbIdent)
                && (_kineticsPublisher?.CanContinueAlterationSynchronously()
                    ?? true))
                _retirements.Advance();
        }
    }

    public void CommenceNearbyStratumSunset(uint lbIdent)
    {
        _retirements.BeginNearLayer(lbIdent);
        if (_retirements.UsesBudgetedHops)
        {
            _retirements.Advance();
            if (_retirements.IsQueued(lbIdent)
                && (_kineticsPublisher?.CanContinueAlterationSynchronously()
                    ?? true))
                _retirements.Advance();
        }
    }

    internal bool FitsPhase(GpuRealmPhase phase) =>
        ReferenceEquals(_phase, phase);

    internal LandblockBulletinProceed ReactivateBulletin(
        LandblockFlowOutcome outcome,
        PagingWorkMeter gauge,
        bool secureHeadway)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(gauge);
        return !_publications.TryGetValue(outcome, out BulletinTransaction? transaction)
            ? new LandblockBulletinProceed(Completed: true, Progressed: false)
            : Advance(outcome, transaction, gauge, secureHeadway);
    }

    internal bool IsLbExhibitPrimed(uint lbIdent)
    {
        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        foreach (BulletinTransaction transaction in _publications.Values)
        {
            if ((transaction.LandblockId & 0xFFFF0000u) ==
                    (canon & 0xFFFF0000u)
                && (!transaction.ExhibitSealed
                    || !transaction.SpatialExhibitSealed))

                return false;
        }
        return true;
    }

    internal bool AbortQueuedPublications()
    {
        if (_publications.Count is 0)
            return true;
        LandblockFlowOutcome[] queued = [.. _publications.Keys];
        bool finished = true;
        for (int ordinal = 0; ordinal < queued.Length; ++ordinal)
        {
            var outcome = queued[ordinal];
            if (!_publications.TryGetValue(
                    outcome,
                    out BulletinTransaction? transaction))

                continue;
            bool cancelled = transaction.KineticsBulletin is not { } kinetics
                || kinetics.TryAbort();
            if (cancelled)
                _publications.Remove(outcome);
            else
                finished = false;
        }
        return finished && _publications.Count is 0;
    }

    internal void QueueWholeSunset(uint lbIdent) =>
        _retirements.BeginFull(lbIdent);

    internal void QueueNearbyStratumSunset(uint lbIdent) =>
        _retirements.BeginNearLayer(lbIdent);

    internal GpuRealmRecenterRetirement UnfastenAllForOriginRecenter()
    {
        var detached =
            _phase.UnfastenAllForOriginRecenter();
        Exception? adoptionMiss;
        try
        {
            adoptionMiss =
                _retirements.AdoptDetachedWhole(detached.Landblocks);
        }
        catch (Exception problem)
        {
            throw new PagingMutationException(
                "Origin-recenter retirement receipt adoption failed after " +
                "the spatial generation detached",
                alterationSealed: true,
                problem);
        }
        Exception? miss = (detached.ObserverFailure, adoptionFailure: adoptionMiss) switch
        {
            (null, null) => null,
            ({ } watcher, null) => watcher,
            (null, { } adoption) => adoption,
            ({ } watcher, { } adoption) => new AggregateException(
                "Origin-recenter spatial observers and retirement adoption failed",
                watcher,
                adoption),
        };
        return detached with { ObserverFailure = miss };
    }

    private void SealSpatialExhibit(BulletinTransaction transaction)
    {
        using var alteration = _phase.CommenceAlterationLot();
        if (!transaction.SpatialSealed)
        {
            var rasterizeIdents =
                transaction.Build.EnvCells?.Shells.Select(
                    static shell => shell.GeometryId);
            IEnumerable<ulong>? plainRasterizeIdents =
                transaction.Build.EnvCells?.StrollStructureTriMeshDeps;
            transaction.SpatialBulletin = transaction.Kind switch
            {
                BulletinFlavor.Loaded
                    or BulletinFlavor.PromoteSelfContained
                    or BulletinFlavor.Far =>
                    _phase.SealLbSpatial(
                        transaction.Build.Landblock,
                        rasterizeIdents,
                        transaction.Tier,
                        plainRasterizeIdents),
                BulletinFlavor.PromoteExisting =>
                    _phase.SealActorsToExtantLbSpatial(
                        transaction.LandblockId,
                        transaction.Build.Landblock.Entities,
                        rasterizeIdents,
                        plainRasterizeIdents),
                _ => throw new InvalidOperationException(
                    $"Unrecognized landblock publication kind {transaction.Kind}."),
            };
            transaction.SpatialSealed = true;
        }

        _phase.EngageLbExhibit(
            transaction.SpatialBulletin
            ?? throw new InvalidOperationException(
                "A committed spatial publication has no activation receipt"));
        if (transaction.SpatialBulletin.RequiresActivation)
        {
            _staticProjDrain?.Reconcile(
                transaction.Build,
                transaction.SpatialBulletin);
        }
        transaction.SpatialExhibitSealed = true;
    }

    private BulletinTransaction FetchOrBuild(
        LandblockFlowOutcome outcome,
        BulletinFlavor sort,
        LandblockAssemble assemble,
        LandblockTessellationData triMeshBlob,
        uint lbIdent,
        LandblockFlowTier tier,
        LandblockFlowPriceEstimate? estimate)
    {
        if (_publications.TryGetValue(outcome, out BulletinTransaction? extant))
        {
            return extant.Kind != sort
                && !(extant.Kind is BulletinFlavor.PromoteExisting
                    or BulletinFlavor.PromoteSelfContained
                    && sort is BulletinFlavor.PromoteExisting
                        or BulletinFlavor.PromoteSelfContained)
                ? throw new InvalidOperationException(
                    $"Landblock publication 0x{extant.LandblockId:X8} changed kind while pending")
                : extant;
        }

        BulletinTransaction built = new BulletinTransaction
        {
            Kind = sort,
            Build = assemble,
            TriMeshBlob = triMeshBlob,
            LandblockId = lbIdent,
            Cost = estimate
                ?? LandblockFlowOutcomePrice.Estimate(assemble, triMeshBlob),
            Tier = tier,
            Timing = BulletinTimingSensor.BuildTimings(),
        };
        _publications.Add(outcome, built);
        return built;
    }
}
