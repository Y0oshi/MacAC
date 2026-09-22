using System.Diagnostics.CodeAnalysis;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Paging;

public sealed partial class GpuRealmPhase
{

    public void RemoveLandblock(uint lbIdent)
    {
        var sunset = UnfastenLb(lbIdent);
        if (sunset is null)
            return;

        FreeLbTriMeshReferences(sunset.LandblockId);
        DirtyLbTaxonomy(sunset.LandblockId);
        for (int idx = 0; idx < sunset.Entities.Count; ++idx)
        {
            RealmActor actor = sunset.Entities[idx];
            if (actor.ServerGuid is 0)
                HaltStaticActorProgram(actor);
        }
    }

    public void RemoveLiveEntityProjection(uint srvOid)
    {
        if (srvOid is 0) return;

        using var alteration = CommenceAlterationLot();

        _oidDeletionTemp.Clear();
        foreach (RealmActor proj in _projLocales.Keys)
        {
            if (proj.ServerGuid == srvOid)
                _oidDeletionTemp.Add(proj);
        }
        for (int idx = 0; idx < _oidDeletionTemp.Count; ++idx)
            DropActorFromAllBins(_oidDeletionTemp[idx]);
        _oidDeletionTemp.Clear();

        // A persistent projection may have been rescued by RemoveLandblock and not yet drained by the next
        // GameWindow frame.
        _persistentRescued.RemoveAll(entity => entity.ServerGuid == srvOid);

    }

    public void RemoveLiveEntityProjection(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.ServerGuid is 0) return;

        using var alteration = CommenceAlterationLot();

        DropActorFromAllBins(actor);

        _persistentRescued.RemoveAll(contender => ReferenceEquals(contender, actor));

    }

    public void DropActorsFromLb(uint lbIdent)
    {
        var sunset = UnfastenNearbyStratum(lbIdent);
        if (sunset is null)
            return;

        FreeLbTriMeshReferences(sunset.LandblockId);
        DirtyLbTaxonomy(sunset.LandblockId);
        for (int idx = 0; idx < sunset.Entities.Count; ++idx)
            HaltStaticActorProgram(sunset.Entities[idx]);
    }
    private bool DropFetchedLb(
        uint lbIdent,
        [NotNullWhen(true)]
        out MountedLandblock? lb)
    {
        if (!_fetched.Remove(lbIdent, out lb))
            return false;
        if (!_rasterizeTraversalSocketByLb.Remove(
                lbIdent,
                out int socket)
            || _rasterizeTraversalLbSockets[socket] != lbIdent)
        {
            throw new InvalidOperationException(
                $"Loaded landblock 0x{lbIdent:X8} has inconsistent render traversal state");
        }

        _rasterizeTraversalLbSockets[socket] = 0;
        _releaseRasterizeTraversalLbSockets.Push(socket);
        HousedPaneRev = checked(HousedPaneRev + 1);
        DirtyLbRasterizeViews(listings: true, limits: true);
        return true;
    }

    private void DropPlanarActor(RealmActor actor)
    {
        if (!_planarActorOrdinals.Remove(actor, out int ordinal))
            throw new InvalidOperationException(
                $"Loaded entity 0x{actor.Id:X8} was absent from the flat render view");

        int previousOrdinal = _planarActors.Count - 1;
        if (ordinal != previousOrdinal)
        {
            RealmActor moved = _planarActors[previousOrdinal];
            _planarActors[ordinal] = moved;
            _planarActorOrdinals[moved] = ordinal;
        }
        _planarActors.RemoveAt(previousOrdinal);
        _planarMembershipStale = true;
    }

    private void DropProjLocale(RealmActor actor)
    {
        if (!_projLocales.Remove(actor, out MirrorLocation locale)
            || actor.ServerGuid is 0)
            return;
        if (locale.IsLoaded)
            DropFetchedOnlineOrdinal(locale.LandblockId, actor);

        if (!_liveProjectionByKey.Remove(
                locale.Key,
                out RealmActor? indexed))
        {
            throw new InvalidOperationException(
                $"Live projection {locale.Key} had no exact-key index entry");
        }
        if (!ReferenceEquals(indexed, actor))
        {
            throw new InvalidOperationException(
                $"Runtime projection {locale.Key} resolved a different owner");
        }
    }

    private void DropFetchedOnlineOrdinal(uint lbIdent, RealmActor actor)
    {
        if (!_fetchedOnlineByLb.TryGetValue(
                lbIdent,
                out HashSet<RealmActor>? actors)
            || !actors.Remove(actor))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{actor.Id:X8} is absent from landblock index 0x{lbIdent:X8}.");
        }
        if (actors.Count is 0)
            _fetchedOnlineByLb.Remove(lbIdent);
    }

    private void DropFetchedProj(RealmActor actor)
    {
        if (!_projLocales.TryGetValue(actor, out MirrorLocation locale)
            || !locale.IsLoaded
            || !_fetched.TryGetValue(locale.LandblockId, out MountedLandblock? lb))
        {
            throw new InvalidOperationException(
                $"Live entity 0x{actor.Id:X8} has no loaded projection location");
        }

        var actors = MutableActors(lb);
        int previousOrdinal = actors.Count - 1;
        if ((uint)locale.BucketIndex >= (uint)actors.Count
            || !ReferenceEquals(actors[locale.BucketIndex], actor))
        {
            throw new InvalidOperationException(
                $"Loaded projection index for entity 0x{actor.Id:X8} is stale");
        }
        if (locale.BucketIndex != previousOrdinal)
        {
            RealmActor moved = actors[previousOrdinal];
            actors[locale.BucketIndex] = moved;
            if (moved.ServerGuid is not 0)
            {
                AssignProjLocale(
                    moved,
                    locale.LandblockId,
                    isFetched: true,
                    locale.BucketIndex);
            }
        }
        actors.RemoveAt(previousOrdinal);

        _movingOrdinalByLb.Remove(locale.LandblockId);
        DirtyLbRasterizeViews(listings: true, limits: false);
        DropPlanarActor(actor);
        DropProjLocale(actor);
        TuneShownOnlineProj(locale.Key, actor, -1);
    }

    private void DropFetchedLbFromPlanarLens(MountedLandblock lb)
    {
        foreach (RealmActor actor in lb.Entities)
        {
            DropPlanarActor(actor);
            if (actor.ServerGuid is not 0)
            {
                SimActorKey tag = _projLocales[actor].Key;
                DropProjLocale(actor);
                TuneShownOnlineProj(tag, actor, -1);
            }
        }
    }

    private void DropActorFromAllBins(RealmActor actor)
    {
        if (!_projLocales.TryGetValue(actor, out MirrorLocation locale))
            return;

        if (locale.IsLoaded)
        {
            DropFetchedProj(actor);
            return;
        }

        if (!_queuedByLb.TryGetValue(locale.LandblockId, out List<RealmActor>? queued)
            || (uint)locale.BucketIndex >= (uint)queued.Count
            || !ReferenceEquals(queued[locale.BucketIndex], actor))
        {
            throw new InvalidOperationException(
                $"Indexed live entity 0x{actor.Id:X8} was absent from pending landblock 0x{locale.LandblockId:X8}.");
        }

        int previousOrdinal = queued.Count - 1;
        if (locale.BucketIndex != previousOrdinal)
        {
            RealmActor moved = queued[previousOrdinal];
            queued[locale.BucketIndex] = moved;
            AssignProjLocale(
                moved,
                locale.LandblockId,
                isFetched: false,
                locale.BucketIndex);
        }
        queued.RemoveAt(previousOrdinal);

        DropProjLocale(actor);
        if (queued.Count is 0)
            _queuedByLb.Remove(locale.LandblockId);
    }
}
