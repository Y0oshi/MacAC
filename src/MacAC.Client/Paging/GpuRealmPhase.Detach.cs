using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Paging;

public sealed partial class GpuRealmPhase
{
    public GpuLandblockSunset? UnfastenLb(uint lbIdent)
    {
        using var alteration = CommenceAlterationLot();

        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        bool hadPhase = _fetched.ContainsKey(canon)
            || _queuedByLb.ContainsKey(canon)
            || _queuedRasterizeIdentsByLb.ContainsKey(canon)
            || _queuedNearbyTierLbs.Contains(canon)
            || _tierByLb.ContainsKey(canon)
            || _aabbs.ContainsKey(canon);
        if (!hadPhase)
            return null;

        IReadOnlyList<RealmActor> detachedActors =
            _fetched.TryGetValue(canon, out MountedLandblock? housedLb)
                ? housedLb.Entities
                : Array.Empty<RealmActor>();
        if (_queuedByLb.TryGetValue(canon, out List<RealmActor>? detachedQueued)
            && detachedQueued.Count > 0)
        {
            if (detachedActors.Count is 0)
            {
                detachedActors = detachedQueued;
            }
            else
            {
                List<RealmActor> combined = new List<RealmActor>(
                    detachedActors.Count + detachedQueued.Count);
                HashSet<RealmActor> observed = new HashSet<RealmActor>(ReferenceEqualityComparer.Instance);
                foreach (RealmActor actor in detachedActors)
                    if (observed.Add(actor))
                        combined.Add(actor);
                foreach (RealmActor actor in detachedQueued)
                    if (observed.Add(actor))
                        combined.Add(actor);
                detachedActors = combined;
            }
        }

        List<RealmActor> keptOnline = new List<RealmActor>();
        HashSet<RealmActor> keptOnlineSet = new HashSet<RealmActor>(ReferenceEqualityComparer.Instance);
        var keptOnlineTags = new Dictionary<RealmActor, SimActorKey>(
            ReferenceEqualityComparer.Instance);
        void KeepOrRescue(RealmActor entity, string src)
        {
            if (entity.ServerGuid is 0)
                return;
            if (!_projLocales.TryGetValue(entity, out MirrorLocation locale))
            {
                throw new InvalidOperationException(
                    $"Live projection 0x{entity.Id:X8} has no exact spatial owner");
            }
            if (_persistentOids.Contains(entity.ServerGuid))
            {
                _persistentRescued.Add(entity);
                ActorVanishProbe.Record(
                    $"[ent] RESCUE guid=0x{entity.ServerGuid:X8} from={src} lb=0x{canon:X8}");
                return;
            }
            if (keptOnlineSet.Add(entity))
            {
                keptOnline.Add(entity);
                keptOnlineTags.Add(entity, locale.Key);
            }
        }

        if (_fetched.TryGetValue(canon, out var landblock))
        {
            foreach (var actor in landblock.Entities)
                KeepOrRescue(actor, "loaded");
        }

        if (_queuedByLb.TryGetValue(canon, out var queuedForLb))
        {
            foreach (var actor in queuedForLb)
            {
                KeepOrRescue(actor, "pending");
                DropProjLocale(actor);
            }
        }

        _queuedByLb.Remove(canon);
        _queuedRasterizeIdentsByLb.Remove(canon);
        _queuedNearbyTierLbs.Remove(canon);
        if (keptOnline.Count > 0)
            _queuedByLb[canon] = keptOnline;
        _aabbs.Remove(canon);
        _movingOrdinalByLb.Remove(canon);

        _tierByLb.Remove(canon);
        if (DropFetchedLb(canon, out MountedLandblock? removed))
            DropFetchedLbFromPlanarLens(removed);

        for (int idx = 0; idx < keptOnline.Count; ++idx)
        {
            RealmActor actor = keptOnline[idx];
            AssignProjLocale(
                actor,
                canon,
                isFetched: false,
                idx,
                keptOnlineTags[actor]);
        }

        return new GpuLandblockSunset(
            canon,
            LandblockSunsetFlavor.Full,
            detachedActors);
    }

    public GpuLandblockSunset? UnfastenNearbyStratum(uint lbIdent)
    {
        using var alteration = CommenceAlterationLot();

        uint canon = (lbIdent & 0xFFFF0000u) | 0xFFFFu;
        bool hadNearbyStratum = _queuedNearbyTierLbs.Remove(canon);
        hadNearbyStratum |= _queuedRasterizeIdentsByLb.Remove(canon);
        hadNearbyStratum |= IsNearbyTier(canon);
        if (!hadNearbyStratum)
            return null;

        List<RealmActor> retiredActors = new List<RealmActor>();
        if (_queuedByLb.TryGetValue(canon, out var queued))
        {
            int queuedEmitOrdinal = 0;
            for (int scanOrdinal = 0; scanOrdinal < queued.Count; ++scanOrdinal)
            {
                RealmActor actor = queued[scanOrdinal];
                if (actor.ServerGuid is 0)
                {
                    retiredActors.Add(actor);
                    continue;
                }
                if (queuedEmitOrdinal != scanOrdinal)
                    queued[queuedEmitOrdinal] = actor;
                AssignProjLocale(
                    actor,
                    canon,
                    isFetched: false,
                    queuedEmitOrdinal);
                ++queuedEmitOrdinal;
            }
            if (queuedEmitOrdinal < queued.Count)
                queued.RemoveRange(queuedEmitOrdinal, queued.Count - queuedEmitOrdinal);
            if (queuedEmitOrdinal is 0)
                _queuedByLb.Remove(canon);
        }

        if (_fetched.TryGetValue(canon, out var landblock))
        {

            List<RealmActor> keptOnline = new List<RealmActor>();
            for (int scanOrdinal = 0; scanOrdinal < landblock.Entities.Count; ++scanOrdinal)
            {
                RealmActor actor = landblock.Entities[scanOrdinal];
                if (actor.ServerGuid is not 0)
                {
                    int onlineEmitOrdinal = keptOnline.Count;
                    keptOnline.Add(actor);
                    AssignProjLocale(
                        actor,
                        canon,
                        isFetched: true,
                        onlineEmitOrdinal);
                    continue;
                }

                retiredActors.Add(actor);
                DropPlanarActor(actor);
            }
            _fetched[canon] = StandardizeFetchedLb(landblock, keptOnline);
            _movingOrdinalByLb.Remove(canon);
            DirtyLbRasterizeViews(listings: true, limits: false);
            _tierByLb[canon] = LandblockFlowTier.Far;
        }

        return new GpuLandblockSunset(
            canon,
            LandblockSunsetFlavor.NearLayer,
            retiredActors);
    }

    internal GpuRealmRecenterRetirement UnfastenAllForOriginRecenter()
    {
        List<uint> idents = new List<uint>(
            _fetched.Count
            + _queuedByLb.Count
            + _queuedRasterizeIdentsByLb.Count);
        HashSet<uint> observedIdents = new HashSet<uint>();

        void AppendIdent(uint ident)
        {
            uint canon = (ident & 0xFFFF0000u) | 0xFFFFu;
            if (observedIdents.Add(canon))
                idents.Add(canon);
        }

        for (int idx = 0; idx < _rasterizeTraversalLbSockets.Count; ++idx)
        {
            uint tag = _rasterizeTraversalLbSockets[idx];
            if (tag is not 0u)
                AppendIdent(tag);
        }
        foreach (uint tag in _queuedRasterizeIdentsByLb.Keys)
            AppendIdent(tag);
        foreach (uint tag in _queuedNearbyTierLbs)
            AppendIdent(tag);
        foreach (uint tag in _tierByLb.Keys)
            AppendIdent(tag);
        foreach (uint tag in _aabbs.Keys)
            AppendIdent(tag);

        var retirements = new List<GpuLandblockSunset>(idents.Count);
        for (int idx = 0; idx < idents.Count; ++idx)
        {
            uint tag = idents[idx];
            IReadOnlyList<RealmActor> fetched =
                _fetched.TryGetValue(tag, out MountedLandblock? lb)
                    ? lb.Entities
                    : Array.Empty<RealmActor>();
            IReadOnlyList<RealmActor> queued =
                _queuedByLb.TryGetValue(tag, out List<RealmActor>? bin)
                    ? bin
                    : Array.Empty<RealmActor>();
            IReadOnlyList<RealmActor> detached = fetched;
            if (queued.Count is not 0)
            {
                if (fetched.Count is 0)
                {
                    detached = queued;
                }
                else
                {
                    List<RealmActor> combined = new List<RealmActor>(
                        fetched.Count + queued.Count);
                    HashSet<RealmActor> observedActors = new HashSet<RealmActor>(
                        ReferenceEqualityComparer.Instance);
                    for (int actorOrdinal = 0;
                         actorOrdinal < fetched.Count;
                         ++actorOrdinal)
                    {
                        RealmActor actor = fetched[actorOrdinal];
                        if (observedActors.Add(actor))
                            combined.Add(actor);
                    }
                    for (int actorOrdinal = 0;
                         actorOrdinal < queued.Count;
                         ++actorOrdinal)
                    {
                        RealmActor actor = queued[actorOrdinal];
                        if (observedActors.Add(actor))
                            combined.Add(actor);
                    }
                    detached = combined;
                }
            }

            retirements.Add(new GpuLandblockSunset(
                tag,
                LandblockSunsetFlavor.Full,
                detached));
        }

        var keptByLb =
            new Dictionary<uint, List<(
                RealmActor Entity,
                int BucketIndex,
                SimActorKey Key)>>();
        HashSet<RealmActor> rescued = new HashSet<RealmActor>(
            _persistentRescued,
            ReferenceEqualityComparer.Instance);
        foreach ((RealmActor actor, MirrorLocation locale) in
                 _projLocales)
        {
            if (_persistentOids.Contains(actor.ServerGuid))
            {
                if (rescued.Add(actor))
                {
                    _persistentRescued.Add(actor);
                    ActorVanishProbe.Record(
                        $"[ent] RESCUE guid=0x{actor.ServerGuid:X8} " +
                        $"from=recenter lb=0x{locale.LandblockId:X8}");
                }
                continue;
            }

            if (!keptByLb.TryGetValue(
                    locale.LandblockId,
                    out List<(
                        RealmActor Entity,
                        int BucketIndex,
                        SimActorKey Key)>? kept))
            {
                kept = [];
                keptByLb.Add(locale.LandblockId, kept);
            }
            kept.Add((actor, locale.BucketIndex, locale.Key));
        }

        int spatialOpTally = OriginRecenterSpatialOpTally;
        Exception? watcherMiss = null;
        var alteration = CommenceAlterationLot();
        try
        {
            foreach ((SimActorKey lookupKey, int tally) in _visibleLiveProjectionCounts)
            {
                if (tally > 0
                    && !_visibilityBeforeMutation.ContainsKey(lookupKey))

                    _visibilityBeforeMutation.Add(lookupKey, true);
            }
            _visibleLiveProjectionCounts.Clear();

            if (_planarActors.Count is not 0)
                _planarMembershipStale = true;
            _planarActors.Clear();
            _planarActorOrdinals.Clear();
            _projLocales.Clear();
            _fetchedOnlineByLb.Clear();
            _liveProjectionByKey.Clear();

            if (_fetched.Count is not 0)
                HousedPaneRev = checked(HousedPaneRev + 1);
            _fetched.Clear();
            _rasterizeTraversalLbSockets.Clear();
            _releaseRasterizeTraversalLbSockets.Clear();
            _rasterizeTraversalSocketByLb.Clear();
            _tierByLb.Clear();
            _aabbs.Clear();
            _movingOrdinalByLb.Clear();
            _queuedByLb.Clear();
            _queuedRasterizeIdentsByLb.Clear();
            _queuedNearbyTierLbs.Clear();

            _lbListingsLens.Clear();
            _lbListingsWithoutMovingOrdinalLens.Clear();
            _lbLimitsLens.Clear();
            DirtyLbRasterizeViews(listings: true, limits: true);

            foreach ((uint tag, List<(
                         RealmActor Entity,
                         int BucketIndex,
                         SimActorKey Key)> kept) in
                     keptByLb)
            {
                kept.Sort(static (left, right) =>
                    left.BucketIndex.CompareTo(right.BucketIndex));
                List<RealmActor> queued = new List<RealmActor>(kept.Count);
                for (int idx = 0; idx < kept.Count; ++idx)
                {
                    RealmActor actor = kept[idx].Entity;
                    queued.Add(actor);
                    AssignProjLocale(
                        actor,
                        tag,
                        isFetched: false,
                        idx,
                        kept[idx].Key);
                }
                _queuedByLb.Add(tag, queued);
            }
        }
        finally
        {
            try
            {
                alteration.Dispose();
            }
            catch (Exception problem)
            {
                watcherMiss = problem;
            }
        }

        return new GpuRealmRecenterRetirement(
            retirements,
            spatialOpTally,
            watcherMiss);
    }
}
