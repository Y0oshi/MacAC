using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Paging;

public sealed partial class GpuRealmPhase
{

    public void AddLandblock(
        MountedLandblock lb,
        IEnumerable<ulong>? additionalRasterizeIdents = null,
        LandblockFlowTier tier = LandblockFlowTier.Near)
    {
        using var alteration = CommenceAlterationLot();
        var bulletin =
            SealLbSpatialCore(lb, additionalRasterizeIdents, tier);
        EngageLbExhibit(bulletin);
    }

    public bool AppendActorsToExtantLb(
        uint lbIdent,
        IReadOnlyList<RealmActor> actors,
        IEnumerable<ulong>? additionalRasterizeIdents = null)
    {
        using var alteration = CommenceAlterationLot();
        var bulletin =
            SealActorsToExtantLbSpatialCore(
                lbIdent,
                actors,
                additionalRasterizeIdents,
                additionalPlainRasterizeIdents: null,
                parkIfAbsent: true);
        if (bulletin is null)
            return false;

        EngageLbExhibit(bulletin);
        return true;
    }
    private void AppendFetchedLb(
        uint lbIdent,
        MountedLandblock lb)
    {
        if (!_fetched.TryAdd(lbIdent, lb))
        {
            throw new InvalidOperationException(
                $"Landblock 0x{lbIdent:X8} is by now loaded");
        }

        int socket;
        if (_releaseRasterizeTraversalLbSockets.TryPop(out int releaseSocket))
        {
            socket = releaseSocket;
            if (_rasterizeTraversalLbSockets[socket] is not 0)
            {
                throw new InvalidOperationException(
                    $"Render traversal slot {socket} wasn't free");
            }
            _rasterizeTraversalLbSockets[socket] = lbIdent;
        }
        else
        {
            socket = _rasterizeTraversalLbSockets.Count;
            _rasterizeTraversalLbSockets.Add(lbIdent);
        }

        if (!_rasterizeTraversalSocketByLb.TryAdd(lbIdent, socket))
        {
            throw new InvalidOperationException(
                $"Landblock 0x{lbIdent:X8} by now owns a render traversal slot");
        }
        HousedPaneRev = checked(HousedPaneRev + 1);
        DirtyLbRasterizeViews(listings: true, limits: true);
    }

    private void AppendPlanarActor(RealmActor actor)
    {
        if (!_planarActorOrdinals.TryAdd(actor, _planarActors.Count))
            throw new InvalidOperationException(
                $"Entity 0x{actor.Id:X8} by now exists in the flat render view");
        _planarActors.Add(actor);
        _planarMembershipStale = true;
    }

    private void AppendFetchedOnlineOrdinal(uint lbIdent, RealmActor actor)
    {
        if (!_fetchedOnlineByLb.TryGetValue(
                lbIdent,
                out HashSet<RealmActor>? actors))
        {
            actors = new HashSet<RealmActor>(ReferenceEqualityComparer.Instance);
            _fetchedOnlineByLb.Add(lbIdent, actors);
        }
        if (!actors.Add(actor))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{actor.Id:X8} by now exists in landblock index 0x{lbIdent:X8}.");
        }
    }

    private void AppendFetchedProj(
        uint lbIdent,
        RealmActor actor,
        SimActorKey? askedTag = null)
    {
        var lb = _fetched[lbIdent];
        var actors = MutableActors(lb);
        int binOrdinal = actors.Count;
        actors.Add(actor);
        _movingOrdinalByLb.Remove(lbIdent);
        DirtyLbRasterizeViews(listings: true, limits: false);
        AppendPlanarActor(actor);
        if (actor.ServerGuid is not 0)
        {
            AssignProjLocale(
                actor,
                lbIdent,
                isFetched: true,
                binOrdinal,
                askedTag);
            TuneShownOnlineProj(
                _projLocales[actor].Key,
                actor,
                +1);
        }
    }

    private void AppendFetchedLbToPlanarLens(MountedLandblock lb)
    {
        for (int idx = 0; idx < lb.Entities.Count; ++idx)
        {
            RealmActor actor = lb.Entities[idx];
            AppendPlanarActor(actor);
            if (actor.ServerGuid is not 0)
                AssignProjLocale(actor, lb.LandblockId, isFetched: true, idx);
            if (actor.ServerGuid is not 0)
            {
                TuneShownOnlineProj(
                    _projLocales[actor].Key,
                    actor,
                    +1);
            }
        }
    }
}
