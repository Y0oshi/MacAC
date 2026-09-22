using System.Numerics;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Paging;

public sealed partial class GpuRealmPhase
{
    public IReadOnlyList<RealmActor> Entities => _planarActors;

    public ulong PlanarLensGen { get; private set; }

    public IReadOnlyCollection<uint> FetchedLbIdents => _fetched.Keys;

    internal ulong HousedPaneRev { get; private set; }

    public int FetchedLbTally => _fetched.Count;

    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                         IReadOnlyList<RealmActor> Entities,
                         IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> LandblockListings
        => FetchLbListingsLens(includeMovingOrdinal: true);

    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
                         IReadOnlyList<RealmActor> Entities,
                         IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> LbListingsWithoutMovingOrdinal
        => FetchLbListingsLens(includeMovingOrdinal: false);

    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax)> LbBounds
    {
        get
        {
            if (!_readiness.IsRealmOnHand)
                return Array.Empty<(uint, Vector3, Vector3)>();
            if (!_lbLimitsLensStale)
                return _lbLimitsLens;

            _lbLimitsLens.Clear();
            for (int socket = 0; socket < _rasterizeTraversalLbSockets.Count; ++socket)
            {
                uint lbIdent = _rasterizeTraversalLbSockets[socket];
                if (lbIdent is 0)
                    continue;
                if (_aabbs.TryGetValue(lbIdent, out var aabb))
                    _lbLimitsLens.Add((lbIdent, aabb.Min, aabb.Max));
                else
                    _lbLimitsLens.Add(
                        (lbIdent, Vector3.Zero, Vector3.Zero));
            }

            _lbLimitsLensStale = false;
            return _lbLimitsLens;
        }
    }

    public void RebucketLiveEntity(RealmActor actor, uint newCanonLb)
    {
        ArgumentNullException.ThrowIfNull(actor);
        SimActorKey tag = _projLocales.TryGetValue(
                actor,
                out MirrorLocation extant)
            ? extant.Key
            : new SimActorKey(actor.Id, 0);
        RebucketLiveEntity(tag, actor, newCanonLb);
    }

    public int QueuedOnlineActorTally => _queuedByLb.Values.Sum(roster => roster.Count);

    public int QueuedBinTally => _queuedByLb.Count;

    public int QueuedRescueTally => _persistentRescued.Count;

    public int PersistentOidTally => _persistentOids.Count;

    public int QueuedVisChangeoverTally => _visChangeovers.Count;

    internal long VisSealTally { get; private set; }

    internal int OriginRecenterSpatialOpTally
    {
        get
        {
            return Math.Max(
            1,
            _fetched.Count
            + _queuedByLb.Count
            + _queuedRasterizeIdentsByLb.Count
            + _queuedNearbyTierLbs.Count
            + _projLocales.Count);
        }
    }

    public void RebucketLiveEntity(
        SimActorKey tag,
        RealmActor actor,
        uint newCanonLb)
    {
        if (actor.ServerGuid is 0) return;
        if (tag.LocalEntityId is 0 || tag.LocalEntityId != actor.Id)
            throw new InvalidOperationException(
                "The exact rebucket key must match the RealmActor local ID");

        using var alteration = CommenceAlterationLot();

        uint canon = (newCanonLb & 0xFFFF0000u) | 0xFFFFu;

        bool hasLatest = _projLocales.TryGetValue(
            actor,
            out MirrorLocation latest);
        if (hasLatest && latest.Key != tag)
        {
            throw new InvalidOperationException(
                "The live spatial projection is owned by another Runtime incarnation");
        }
        if (hasLatest
            && latest.IsLoaded
            && latest.LandblockId == canon)

            return;

        DropActorFromAllBins(actor);
        PlaceLiveEntityProjection(tag, canon, actor);
    }

    public void DropOnlineActor(uint srvOid)
    {
        RemoveLiveEntityProjection(srvOid);
        _persistentOids.Remove(srvOid);
        _persistentInPlanarSensor.Remove(srvOid);
    }

    public bool IsLoaded(uint lbIdent) => _fetched.ContainsKey(lbIdent);

    public bool IsNearbyTier(uint lbIdent)
    {
        return _tierByLb.TryGetValue(lbIdent, out var tier)
        && tier == LandblockFlowTier.Near;
    }

    public bool IsNearbyTierOrQueued(uint lbIdent) =>
        IsNearbyTier(lbIdent) || _queuedNearbyTierLbs.Contains(lbIdent);

    public bool IsRasterizePrimed(uint lbIdent)
    {
        return _fetched.ContainsKey(lbIdent)
        && (_wbSummonBridge?.IsLbRasterizePrimed(lbIdent) ?? true);
    }

    public bool IsOnlineActorProjHoused(SimActorKey tag)
    {
        return tag.LocalEntityId is not 0
        && _visibleLiveProjectionCounts.ContainsKey(tag);
    }

    public bool IsOnlineActorShown(SimActorKey tag)
    {
        return _readiness.IsRealmOnHand
        && IsOnlineActorProjHoused(tag);
    }

    public bool TryFetchLb(uint lbIdent, out MountedLandblock? landblock)
    {
        if (_fetched.TryGetValue(lbIdent, out var located))
        {
            landblock = located;
            return true;
        }
        landblock = null;
        return false;
    }

    public void AssignLbAabb(uint lbIdent, Vector3 lower, Vector3 upper)
    {
        _aabbs[lbIdent] = (lower, upper);
        DirtyLbRasterizeViews(listings: true, limits: true);
    }

    public AlterationLot CommenceAlterationLot()
    {
        ++_alterationZDepth;
        return new AlterationLot(this);
    }

    public void DuplicateOnlineActorsNearbyLb(
        uint middleChamberOrLbIdent,
        int lbRadius,
        List<KeyValuePair<uint, RealmActor>> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentOutOfRangeException.ThrowIfNegative(lbRadius);
        dest.Clear();
        if (!_readiness.IsRealmOnHand)
            return;

        int middleX = (int)((middleChamberOrLbIdent >> 24) & 0xFFu);
        int middleY = (int)((middleChamberOrLbIdent >> 16) & 0xFFu);
        for (int dx = -lbRadius; dx <= lbRadius; ++dx)
            for (int dy = -lbRadius; dy <= lbRadius; ++dy)
            {
                int x = middleX + dx;
                int y = middleY + dy;
                if ((uint)x > 0xFFu || (uint)y > 0xFFu)
                    continue;

                uint lbIdent = ((uint)x << 24) | ((uint)y << 16) | 0xFFFFu;
                if (!_fetchedOnlineByLb.TryGetValue(
                        lbIdent,
                        out HashSet<RealmActor>? onlineActors))
                    continue;

                foreach (RealmActor actor in onlineActors)
                    dest.Add(new KeyValuePair<uint, RealmActor>(actor.ServerGuid, actor));
            }
    }

    public void FlagPersistent(uint srvOid) => _persistentOids.Add(srvOid);

    public List<RealmActor> DrainRescued()
    {
        if (_persistentRescued.Count is 0) return _persistentRescued;
        List<RealmActor> outcome = new List<RealmActor>(_persistentRescued);
        _persistentRescued.Clear();
        return outcome;
    }

    public void WipeOnlineActorLifespanPhase()
    {
        _persistentOids.Clear();
        _persistentRescued.Clear();
        _persistentInPlanarSensor.Clear();
        _visChangeovers.Clear();
        _visibleLiveProjectionCounts.Clear();
        _visibilityBeforeMutation.Clear();
        _projLocales.Clear();
        _fetchedOnlineByLb.Clear();
        _liveProjectionByKey.Clear();
    }

    public void PlaceLiveEntityProjection(uint lbIdent, RealmActor actor)
    {
        PlaceLiveEntityProjection(
            new SimActorKey(actor.Id, 0),
            lbIdent,
            actor);
    }

    public void PlaceLiveEntityProjection(
        SimActorKey tag,
        uint lbIdent,
        RealmActor actor)
    {
        using var alteration = CommenceAlterationLot();

        if (tag.LocalEntityId is 0 || tag.LocalEntityId != actor.Id)
            throw new InvalidOperationException(
                "The exact placement key must match the RealmActor local ID");
        if (_projLocales.ContainsKey(actor))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{actor.Id:X8} is by now spatially projected");
        }

        uint canonLbIdent = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        bool sensorPersistent = ActorVanishProbe.Enabled
            && actor.ServerGuid is not 0 && _persistentOids.Contains(actor.ServerGuid);

        if (_fetched.ContainsKey(canonLbIdent))
        {
            AppendFetchedProj(canonLbIdent, actor, tag);
            if (sensorPersistent)
                ActorVanishProbe.Record($"[ent] APPEND guid=0x{actor.ServerGuid:X8} lb=0x{canonLbIdent:X8} -> LOADED(drawn)");
            return;
        }

        if (!_queuedByLb.TryGetValue(canonLbIdent, out var bin))
        {
            bin = [];
            _queuedByLb[canonLbIdent] = bin;
        }
        int binOrdinal = bin.Count;
        bin.Add(actor);
        AssignProjLocale(
            actor,
            canonLbIdent,
            isFetched: false,
            binOrdinal,
            tag);
        if (sensorPersistent)
            ActorVanishProbe.Record($"[ent] APPEND guid=0x{actor.ServerGuid:X8} lb=0x{canonLbIdent:X8} -> PENDING(hidden)");
    }

    internal GpuLandblockSpatialBulletin SealLbSpatial(
        MountedLandblock lb,
        IEnumerable<ulong>? additionalRasterizeIdents,
        LandblockFlowTier tier,
        IEnumerable<ulong>? additionalPlainRasterizeIdents = null)
    {
        return SealLbSpatialCore(
            lb,
            additionalRasterizeIdents,
            tier,
            additionalPlainRasterizeIdents);
    }

    internal void DirtyLbTaxonomy(uint lbIdent) =>
        _onLbUnloaded?.Invoke(lbIdent);

    internal GpuLandblockSpatialBulletin SealActorsToExtantLbSpatial(
        uint lbIdent,
        IReadOnlyList<RealmActor> actors,
        IEnumerable<ulong>? additionalRasterizeIdents,
        IEnumerable<ulong>? additionalPlainRasterizeIdents = null)
    {
        var bulletin =
            SealActorsToExtantLbSpatialCore(
                lbIdent,
                actors,
                additionalRasterizeIdents,
                additionalPlainRasterizeIdents,
                parkIfAbsent: false);
        return bulletin
            ?? throw new InvalidOperationException(
                $"Landblock 0x{lbIdent:X8} isn't resident for an accepted promotion");
    }

    internal bool TryFetchRasterizeTraversalOrderTag(
        RealmActor actor,
        out RasterizeOrderTag orderTag)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (_projLocales.TryGetValue(
                actor,
                out MirrorLocation locale)
            && locale.IsLoaded
            && _rasterizeTraversalSocketByLb.TryGetValue(
                locale.LandblockId,
                out int lbSocket))
        {
            orderTag = RasterizeTraversalOrderTag.Compose(
                checked((uint)lbSocket + 1),
                locale.BucketIndex);
            return true;
        }

        orderTag = default;
        return false;
    }

    internal ResidentPagingWindowFact GrabHousedPagingPane(
        int middleX,
        int middleY)
    {
        if ((uint)middleX > byte.MaxValue || (uint)middleY > byte.MaxValue)
            return ResidentPagingWindowFact.Unavailable(HousedPaneRev);

        uint middle = PagingRegion.PackLbIdent(middleX, middleY);
        if (!_fetched.ContainsKey(middle))
            return ResidentPagingWindowFact.Unavailable(HousedPaneRev);

        int ceilingContender = 0;
        foreach (uint lbIdent in _fetched.Keys)
        {
            int x = (int)((lbIdent >> 24) & 0xFFu);
            int y = (int)((lbIdent >> 16) & 0xFFu);
            ceilingContender = Math.Max(
                ceilingContender,
                Math.Max(Math.Abs(x - middleX), Math.Abs(y - middleY)));
        }

        int doneRadius = 0;
        for (int radius = 1; radius <= ceilingContender; ++radius)
        {
            if (!ContainsDoneLoop(radius))
                break;
            doneRadius = radius;
        }

        return new ResidentPagingWindowFact(
            HousedPaneRev,
            middleX,
            middleY,
            doneRadius,
            _fetched.Count,
            HasPublishedCenter: true);

        bool ContainsDoneLoop(int radius)
        {
            int lowerX = Math.Max(0, middleX - radius);
            int upperX = Math.Min(byte.MaxValue, middleX + radius);
            int lowerY = Math.Max(0, middleY - radius);
            int upperY = Math.Min(byte.MaxValue, middleY + radius);
            for (int x = lowerX; x <= upperX; ++x)
            {
                if (!_fetched.ContainsKey(PagingRegion.PackLbIdent(x, lowerY))
                    || !_fetched.ContainsKey(PagingRegion.PackLbIdent(x, upperY)))

                    return false;
            }
            for (int y = lowerY + 1; y < upperY; ++y)
            {
                if (!_fetched.ContainsKey(PagingRegion.PackLbIdent(lowerX, y))
                    || !_fetched.ContainsKey(PagingRegion.PackLbIdent(upperX, y)))

                    return false;
            }
            return true;
        }
    }

    internal void DuplicatePublishedActorsNearbyLbForReadiness(
        uint middleChamberOrLbIdent,
        int lbRadius,
        List<RealmActor> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        ArgumentOutOfRangeException.ThrowIfNegative(lbRadius);
        dest.Clear();

        int middleX = (int)((middleChamberOrLbIdent >> 24) & 0xFFu);
        int middleY = (int)((middleChamberOrLbIdent >> 16) & 0xFFu);
        for (int dx = -lbRadius; dx <= lbRadius; ++dx)
            for (int dy = -lbRadius; dy <= lbRadius; ++dy)
            {
                int x = middleX + dx;
                int y = middleY + dy;
                if ((uint)x > 0xFFu || (uint)y > 0xFFu)
                    continue;

                uint lbIdent =
                    ((uint)x << 24) | ((uint)y << 16) | 0xFFFFu;
                if (!_fetched.TryGetValue(
                        lbIdent,
                        out MountedLandblock? lb))

                    continue;

                for (int idx = 0; idx < lb.Entities.Count; ++idx)
                    dest.Add(lb.Entities[idx]);
            }
    }

    internal void EngageLbExhibit(
        GpuLandblockSpatialBulletin bulletin)
    {
        ArgumentNullException.ThrowIfNull(bulletin);
        if (!bulletin.RequiresActivation)
            return;
        if (!_fetched.TryGetValue(bulletin.LandblockId, out MountedLandblock? latest)
            || !ReferenceEquals(latest, bulletin.Landblock))
        {
            throw new InvalidOperationException(
                $"Landblock 0x{bulletin.LandblockId:X8} changed prior to presentation activation");
        }

        _wbSummonBridge?.OnLbFetched(
            bulletin.Landblock,
            bulletin.AdditionalRenderIds,
            bulletin.AdditionalOrdinaryRenderIds,
            bulletin.ReplacesMeshRegistration);

        if (_actorProgramActivator is not null)
        {
            for (int idx = 0; idx < bulletin.StaticEntities.Count; ++idx)
                _actorProgramActivator.OnBuild(bulletin.StaticEntities[idx]);
        }
    }

    internal void FreeLbTriMeshReferences(uint lbIdent)
    {
        if (_wbSummonBridge is null)
            return;

        _wbSummonBridge.OnLbUnloaded(lbIdent);

        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        if (!_fetched.TryGetValue(canon, out MountedLandblock? kept))
            return;
        if (!_tierByLb.TryGetValue(canon, out LandblockFlowTier tier)
            || tier != LandblockFlowTier.Far)

            return;

        _wbSummonBridge.OnLbFetched(kept);
    }

    internal void HaltStaticActorProgram(RealmActor actor) =>
        _actorProgramActivator?.OnDrop(actor);

    private void DirtyLbRasterizeViews(bool listings, bool limits)
    {
        if (listings)
        {
            _lbListingsLensStale = true;
            _lbListingsWithoutMovingOrdinalLensStale = true;
        }
        if (limits)
            _lbLimitsLensStale = true;
    }

    private void TuneShownOnlineProj(
        SimActorKey tag,
        RealmActor actor,
        int diff)
    {
        if (actor.ServerGuid is 0 || tag.LocalEntityId is 0 || diff is 0)
            return;
        if (tag.LocalEntityId != actor.Id)
            throw new InvalidOperationException(
                "A live visibility key must match its RealmActor local ID");

        _visibleLiveProjectionCounts.TryGetValue(tag, out int precedingTally);
        if (!_visibilityBeforeMutation.ContainsKey(tag))
            _visibilityBeforeMutation.Add(tag, precedingTally > 0);

        int upcomingTally = checked(precedingTally + diff);
        if (upcomingTally < 0)
            throw new InvalidOperationException(
                $"Live projection visibility count underflow for local " +
                $"0x{tag.LocalEntityId:X8}/{tag.Incarnation} / " +
                $"server 0x{actor.ServerGuid:X8}.");

        if (upcomingTally is 0)
        {
            _visibleLiveProjectionCounts.Remove(tag);
        }
        else
        {
            _visibleLiveProjectionCounts[tag] = upcomingTally;
        }
    }

    private static List<RealmActor> MutableActors(MountedLandblock lb) =>
        (List<RealmActor>)lb.Entities;

    private GpuLandblockSpatialBulletin SealLbSpatialCore(
        MountedLandblock lb,
        IEnumerable<ulong>? additionalRasterizeIdents,
        LandblockFlowTier tier,
        IEnumerable<ulong>? additionalPlainRasterizeIdents = null)
    {
        ArgumentNullException.ThrowIfNull(lb);

        if (tier == LandblockFlowTier.Far && IsNearbyTier(lb.LandblockId))
        {
            var kept = _fetched[lb.LandblockId];
            return new GpuLandblockSpatialBulletin(
                kept.LandblockId,
                kept,
                Array.Empty<ulong>(),
                Array.Empty<RealmActor>(),
                RequiresActivation: false,
                RenderTraversalOrder:
                    FetchRasterizeTraversalOrdering(kept.LandblockId));
        }

        lb = StandardizeFetchedLb(lb);
        if (_fetched.TryGetValue(lb.LandblockId, out MountedLandblock? displaced))
            SettleKeptStaticActorPrograms(displaced, lb);

        if (_queuedByLb.TryGetValue(lb.LandblockId, out var queued) && queued.Count > 0)
        {
            List<RealmActor> merged = new List<RealmActor>(lb.Entities.Count + queued.Count);
            merged.AddRange(lb.Entities);
            merged.AddRange(queued);
            lb = StandardizeFetchedLb(lb, merged);
            _queuedByLb.Remove(lb.LandblockId);
        }
        HashSet<ulong>? mergedRasterizeIdents = additionalRasterizeIdents is null
            ? null
            : [.. additionalRasterizeIdents];
        if (_queuedRasterizeIdentsByLb.Remove(lb.LandblockId, out var queuedRasterizeIdents))
        {
            mergedRasterizeIdents ??= [];
            mergedRasterizeIdents.UnionWith(queuedRasterizeIdents);
        }

        bool queuedNearby = _queuedNearbyTierLbs.Remove(lb.LandblockId);
        if (queuedNearby
            || (_tierByLb.TryGetValue(lb.LandblockId, out var latestTier)
                && latestTier == LandblockFlowTier.Near))

            tier = LandblockFlowTier.Near;

        bool replacesTriMeshEnrollment = false;
        if (DropFetchedLb(lb.LandblockId, out displaced))
        {
            replacesTriMeshEnrollment = true;
            DropFetchedLbFromPlanarLens(displaced);
            _movingOrdinalByLb.Remove(lb.LandblockId);
        }

        AppendFetchedLb(lb.LandblockId, lb);
        AppendFetchedLbToPlanarLens(lb);
        _tierByLb[lb.LandblockId] = tier;
        return BuildSpatialBulletin(
            _fetched[lb.LandblockId],
            (IEnumerable<ulong>?)mergedRasterizeIdents ?? Array.Empty<ulong>(),
            FetchRasterizeTraversalOrdering(lb.LandblockId),
            additionalPlainRasterizeIdents,
            replacesTriMeshEnrollment);
    }

    private void SettleKeptStaticActorPrograms(
        MountedLandblock displaced,
        MountedLandblock substitute)
    {
        if (_actorProgramActivator is null)
            return;

        var substitutesByIdent = new Dictionary<uint, RealmActor>();
        for (int idx = 0; idx < substitute.Entities.Count; ++idx)
        {
            RealmActor actor = substitute.Entities[idx];
            if (actor.ServerGuid is 0)
                substitutesByIdent.Add(actor.Id, actor);
        }

        for (int idx = 0; idx < displaced.Entities.Count; ++idx)
        {
            RealmActor preceding = displaced.Entities[idx];
            if (preceding.ServerGuid is not 0)
                continue;
            if (substitutesByIdent.TryGetValue(preceding.Id, out RealmActor? kept))
                _actorProgramActivator.OnRebindStatic(preceding, kept);
            else
                _actorProgramActivator.OnDrop(preceding);
        }
    }

    private GpuLandblockSpatialBulletin? SealActorsToExtantLbSpatialCore(
        uint lbIdent,
        IReadOnlyList<RealmActor> actors,
        IEnumerable<ulong>? additionalRasterizeIdents,
        IEnumerable<ulong>? additionalPlainRasterizeIdents,
        bool parkIfAbsent)
    {
        ArgumentNullException.ThrowIfNull(actors);

        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        if (!_fetched.TryGetValue(canon, out var landblock))
        {
            if (!parkIfAbsent)
                return null;

            // Park as pending - same pattern as live projections for not-yet-loaded LBs.
            if (!_queuedByLb.TryGetValue(canon, out var bin))
            {
                bin = [];
                _queuedByLb[canon] = bin;
            }
            int leadOrdinal = bin.Count;
            bin.AddRange(actors);
            for (int idx = 0; idx < actors.Count; ++idx)
            {
                RealmActor actor = actors[idx];
                if (actor.ServerGuid is not 0)
                {
                    AssignProjLocale(
                        actor,
                        canon,
                        isFetched: false,
                        leadOrdinal + idx);
                }
            }
            _queuedNearbyTierLbs.Add(canon);
            if (additionalRasterizeIdents is not null)
            {
                if (!_queuedRasterizeIdentsByLb.TryGetValue(canon, out var rasterizeIdents))
                {
                    rasterizeIdents = [];
                    _queuedRasterizeIdentsByLb[canon] = rasterizeIdents;
                }
                rasterizeIdents.UnionWith(additionalRasterizeIdents);
            }
            return null;
        }
        var housed = MutableActors(landblock);
        for (int idx = 0; idx < actors.Count; ++idx)
        {
            RealmActor actor = actors[idx];
            int binOrdinal = housed.Count;
            housed.Add(actor);
            AppendPlanarActor(actor);
            if (actor.ServerGuid is not 0)
            {
                AssignProjLocale(actor, canon, isFetched: true, binOrdinal);
                TuneShownOnlineProj(
                    _projLocales[actor].Key,
                    actor,
                    +1);
            }
        }
        _movingOrdinalByLb.Remove(canon);
        DirtyLbRasterizeViews(listings: true, limits: false);
        _tierByLb[canon] = LandblockFlowTier.Near;
        ulong[] netRasterizeIdents = additionalRasterizeIdents?.ToArray() ?? [];
        RealmActor[] staticActors = [.. actors.Where(static entity => entity.ServerGuid == 0)];
        return new GpuLandblockSpatialBulletin(
            canon,
            _fetched[canon],
            netRasterizeIdents,
            staticActors,
            RenderTraversalOrder: FetchRasterizeTraversalOrdering(canon),
            AdditionalOrdinaryRenderIds:
                additionalPlainRasterizeIdents?.ToArray() ?? []);
    }

    private void InspectPlanarLensChangeovers()
    {
        HashSet<uint> instant = new HashSet<uint>();
        foreach (var entity in _planarActors)
            if (entity.ServerGuid is not 0 && _persistentOids.Contains(entity.ServerGuid))
                instant.Add(entity.ServerGuid);
        foreach (var g in instant)
            if (!_persistentInPlanarSensor.Contains(g))
                ActorVanishProbe.Record($"[ent] DRAWSET guid=0x{g:X8} -> PRESENT (flatCount={_planarActors.Count})");
        foreach (var g in _persistentInPlanarSensor)
            if (!instant.Contains(g))
                ActorVanishProbe.Record($"[ent] DRAWSET guid=0x{g:X8} -> ABSENT (flatCount={_planarActors.Count})");
        _persistentInPlanarSensor.Clear();
        foreach (var g in instant) _persistentInPlanarSensor.Add(g);
    }

    private void AssignProjLocale(
        RealmActor actor,
        uint lbIdent,
        bool isFetched,
        int binOrdinal,
        SimActorKey? askedTag = null)
    {
        if (actor.ServerGuid is 0)
            return;

        bool isNew = !_projLocales.TryGetValue(
            actor,
            out MirrorLocation precedingLocale);
        SimActorKey tag = isNew
            ? askedTag ?? new SimActorKey(actor.Id, 0)
            : precedingLocale.Key;
        if (tag.LocalEntityId is 0 || tag.LocalEntityId != actor.Id)
        {
            throw new InvalidOperationException(
                "The exact spatial projection key must match the RealmActor local ID");
        }
        if (!isNew && askedTag is { } asked && asked != tag)
        {
            throw new InvalidOperationException(
                "A live spatial projection can't change exact Runtime identity");
        }
        if (!isNew
            && precedingLocale.IsLoaded
            && (!isFetched || precedingLocale.LandblockId != lbIdent))
        {
            DropFetchedOnlineOrdinal(precedingLocale.LandblockId, actor);
        }
        _projLocales[actor] = new MirrorLocation(
            lbIdent,
            isFetched,
            binOrdinal,
            tag);
        if (isFetched
            && (isNew || !precedingLocale.IsLoaded || precedingLocale.LandblockId != lbIdent))

            AppendFetchedOnlineOrdinal(lbIdent, actor);
        if (!isNew)
            return;

        if (!_liveProjectionByKey.TryAdd(tag, actor))
        {
            throw new InvalidOperationException(
                $"Runtime projection {tag} is by now spatially owned");
        }
    }

    private uint FetchRasterizeTraversalOrdering(uint lbIdent)
    {
        return !_rasterizeTraversalSocketByLb.TryGetValue(
                lbIdent,
                out int socket)
            ? throw new InvalidOperationException(
                $"Loaded landblock 0x{lbIdent:X8} has no render traversal slot")
            : checked((uint)socket + 1);
    }

    private void DisposeRest()
    {
        if (_alterationZDepth <= 0)
            throw new InvalidOperationException("GpuRealmPhase mutation scope underflow");
        if (--_alterationZDepth is not 0)
            return;

        foreach ((SimActorKey tag, bool wasShown) in _visibilityBeforeMutation)
        {
            bool isShown = _visibleLiveProjectionCounts.ContainsKey(tag);
            if (wasShown != isShown)
                _visChangeovers.Enqueue((tag, isShown));
        }
        _visibilityBeforeMutation.Clear();
        ++VisSealTally;
        if (_planarMembershipStale)
        {
            ++PlanarLensGen;
            _planarMembershipStale = false;
        }

        EmptyVisChangeovers();
        if (ActorVanishProbe.Enabled)
            InspectPlanarLensChangeovers();
    }

    private static MountedLandblock StandardizeFetchedLb(
        MountedLandblock src,
        List<RealmActor>? actors = null)
    {
        return new(
            src.LandblockId,
            src.Heightmap,
            actors ?? [.. src.Entities],
            KineticDatBundle.Empty);
    }

    private static GpuLandblockSpatialBulletin BuildSpatialBulletin(
        MountedLandblock lb,
        IEnumerable<ulong> additionalRasterizeIdents,
        uint rasterizeTraversalOrdering,
        IEnumerable<ulong>? additionalPlainRasterizeIdents = null,
        bool replacesTriMeshEnrollment = false)
    {
        ulong[] rasterizeIdents = additionalRasterizeIdents as ulong[]
            ?? [.. additionalRasterizeIdents];
        ulong[] plainRasterizeIdents = additionalPlainRasterizeIdents as ulong[]
            ?? additionalPlainRasterizeIdents?.ToArray()
            ?? [];
        RealmActor[] staticActors = [.. lb.Entities.Where(static actor => actor.ServerGuid == 0)];
        return new GpuLandblockSpatialBulletin(
            lb.LandblockId,
            lb,
            rasterizeIdents,
            staticActors,
            RenderTraversalOrder: rasterizeTraversalOrdering,
            AdditionalOrdinaryRenderIds: plainRasterizeIdents,
            ReplacesMeshRegistration: replacesTriMeshEnrollment);
    }

    private void EmptyVisChangeovers()
    {
        if (_dispatchingVisChangeovers)
            return;

        List<Exception>? misses = null;
        _dispatchingVisChangeovers = true;
        try
        {
            while (_visChangeovers.TryDequeue(out var changeover))
            {
                Delegate[] subscribers = LiveProjectionVisibilityChanged?
                    .GetInvocationList()
                    ?? [];
                for (int idx = 0; idx < subscribers.Length; ++idx)
                {
                    try
                    {
                        ((Action<SimActorKey, bool>)subscribers[idx])(
                            changeover.Key,
                            changeover.Visible);
                    }
                    catch (Exception problem)
                    {
                        (misses ??= []).Add(problem);
                    }
                }
            }
        }
        finally
        {
            _dispatchingVisChangeovers = false;
        }

        if (misses is not null)
        {
            throw new AggregateException(
                "One or more live projection visibility observers failed",
                misses);
        }
    }
}
