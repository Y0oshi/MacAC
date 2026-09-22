using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class CurrentRenderStageOracle
{
    public void Observe(
        uint lbIdent,
        RealmActor actor,
        InteriorActorPartition.ProjClass projectionClass)
    {
        if (!_cycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle received a projection beyond an open frame");
        }
        ArgumentNullException.ThrowIfNull(actor);

        switch (projectionClass)
        {
            case InteriorActorPartition.ProjClass.OutdoorStatic:
                ++_exteriorStaticTally;
                break;
            case InteriorActorPartition.ProjClass.CellStatic:
                ++_chamberStaticTally;
                break;
            case InteriorActorPartition.ProjClass.Dynamic:
                ++_dynamicTally;
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(projectionClass),
                    projectionClass,
                    "Unrecognized current render projection class");
        }

        var fingerprint = BuildFingerprint(
            lbIdent,
            actor,
            projectionClass);
        _projections.Add(fingerprint);
        if (!_projByActor.TryAdd(actor, fingerprint))
        {
            throw new InvalidOperationException(
                $"The current render partition observed entity 0x{actor.Id:X8} more than once");
        }
    }

    public void WatchPLensBin(
        LatestRasterizePLensCourse course,
        int courseOrdinal,
        uint chamberIdent,
        IReadOnlyList<RealmActor> actors)
    {
        if (!_pviewCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle received a PView bucket beyond an open frame");
        }
        ArgumentNullException.ThrowIfNull(actors);

        _pviewDigest.Add((byte)0xB1);
        _pviewDigest.Add((byte)course);
        _pviewDigest.Add(courseOrdinal);
        _pviewDigest.Add(chamberIdent);
        _pviewDigest.Add(actors.Count);
        foreach (RealmActor actor in actors)
        {
            if (!_projByActor.TryGetValue(actor, out var proj))
            {
                throw new InvalidOperationException(
                    $"PView routed entity 0x{actor.Id:X8} that was absent from the current partition");
            }

            var contender = new LatestRasterizePLensContenderFingerprint(
                Sequence: _pviewContenders.Count,
                Route: course,
                RouteIndex: courseOrdinal,
                CellId: chamberIdent,
                Projection: proj);
            _pviewContenders.Add(contender);
            AppendPLensContender(ref _pviewDigest, in contender);
        }
    }

    public void WatchRouterPaint(
        RealmPaintRouter.ActorSet set,
        int actorsWalked,
        IReadOnlyList<(
            RealmActor Entity,
            int MeshRefIndex,
            uint LandblockId)> approved)
    {
        if (!_routerCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle received dispatcher input beyond an open frame");
        }
        ArgumentNullException.ThrowIfNull(approved);

        int paintSeries = _routerPaintTally++;
        _routerActorTally = checked(_routerActorTally + actorsWalked);
        _routerDigest.Add((byte)0xD1);
        _routerDigest.Add(paintSeries);
        _routerDigest.Add((int)set);
        _routerDigest.Add(actorsWalked);
        _routerDigest.Add(approved.Count);

        foreach (var tuple in approved)
        {
            uint stashLbIdent =
                RealmPaintRouter.LocateStashLbHint(
                    tuple.Entity,
                    tuple.LandblockId);
            if (!_routerProjByActor.TryGetValue(
                    tuple.Entity,
                    out CurrentRenderMirrorFingerprint proj))
            {
                if (!_projByActor.TryGetValue(
                        tuple.Entity,
                        out proj))
                {
                    proj = BuildFingerprint(
                        stashLbIdent,
                        tuple.Entity,
                        ProjClassOf(tuple.Entity));
                }
                _routerProjByActor[tuple.Entity] = proj;
            }
            var contender = new CurrentRenderRouterFingerprint(
                Sequence: _routerContenders.Count,
                DrawSequence: paintSeries,
                Set: set,
                MeshRefIndex: tuple.MeshRefIndex,
                TupleLandblockId: tuple.LandblockId,
                CacheLandblockId: stashLbIdent,
                Projection: proj,
                GfxObjId: tuple.Entity.MeshRefs[tuple.MeshRefIndex].GfxObjId,
                PartTransform:
                    tuple.Entity.MeshRefs[tuple.MeshRefIndex].PartTransform,
                SurfaceOverrides: CreateSurfaceOverrideFingerprint(
                    tuple.Entity.MeshRefs[tuple.MeshRefIndex]
                        .CanvasOverrides));
            _routerContenders.Add(contender);
            AppendRouterContender(ref _routerDigest, in contender);
        }

        Snapshot = Snapshot with
        {
            DispatcherFrameSequence = _routerCycleSeries,
            AbortedDispatcherFrames = _abortedRouterCycles,
            DispatcherDrawCount = _routerPaintTally,
            DispatcherEntityCount = _routerActorTally,
            DispatcherMeshRefCount = _routerContenders.Count,
            DispatcherInstanceCount = _routerInstTally,
            DispatcherOpaqueGroupCount = _routerSolidClusterTally,
            DispatcherTransparentGroupCount =
                _routerSeeThruClusterTally,
            DispatcherDigest = _routerDigest.Finish(),
        };
    }

    public void WatchRouterSubmission(
        in CurrentRenderRouterSubmission submission)
    {
        if (!_routerCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle received dispatcher submission beyond an open frame");
        }
        if (_routerPaintTally is 0)
        {
            throw new InvalidOperationException(
                "A dispatcher submission can't precede its accepted candidate set");
        }

        _routerInstTally = checked(
            _routerInstTally + submission.VisibleInstanceCount);
        _routerSubmissions.Add(submission);
        _routerSolidClusterTally = checked(
            _routerSolidClusterTally + submission.OpaqueGroupCount);
        _routerSeeThruClusterTally = checked(
            _routerSeeThruClusterTally
            + submission.TransparentGroupCount);
        _routerDigest.Add((byte)0xD2);
        _routerDigest.Add(_routerPaintTally - 1);
        _routerDigest.Add(submission.VisibleInstanceCount);
        _routerDigest.Add(submission.ImmediateInstanceCount);
        _routerDigest.Add(submission.OpaqueGroupCount);
        _routerDigest.Add(submission.TransparentGroupCount);
        _routerDigest.Add(submission.TransparentDeferred);
        _routerDigest.Add(submission.Digest.Low);
        _routerDigest.Add(submission.Digest.High);

        Snapshot = Snapshot with
        {
            DispatcherInstanceCount = _routerInstTally,
            DispatcherOpaqueGroupCount = _routerSolidClusterTally,
            DispatcherTransparentGroupCount =
                _routerSeeThruClusterTally,
            DispatcherDigest = _routerDigest.Finish(),
        };
    }

    public void WatchPickPiece(
        uint srvOid,
        uint ownActorIdent,
        int pieceOrdinal,
        uint gfxObjRefIdent,
        Matrix4x4 ownToRealm,
        CanonPickMesh triMesh)
    {
        if (!_pickCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle received a selection part beyond an open frame");
        }
        ArgumentNullException.ThrowIfNull(triMesh);

        if (!_pickGeoByGfxObjRef.TryGetValue(
                gfxObjRefIdent,
                out PickingGeometryShelfEntry stashedGeo)
            || !ReferenceEquals(stashedGeo.Mesh, triMesh))
        {
            stashedGeo = new PickingGeometryShelfEntry(
                triMesh,
                FingerprintPickGeo(triMesh));
            _pickGeoByGfxObjRef[gfxObjRefIdent] = stashedGeo;
        }

        var fingerprint = new CurrentRenderPickingFingerprint(
            Sequence: _pickPieces.Count,
            ServerGuid: srvOid,
            LocalEntityId: ownActorIdent,
            PartIndex: pieceOrdinal,
            GfxObjId: gfxObjRefIdent,
            LocalToWorld: ownToRealm,
            Geometry: stashedGeo.Fingerprint);
        _pickPieces.Add(fingerprint);
        _pickDigest.Add(fingerprint.Sequence);
        _pickDigest.Add(fingerprint.ServerGuid);
        _pickDigest.Add(fingerprint.LocalEntityId);
        _pickDigest.Add(fingerprint.PartIndex);
        _pickDigest.Add(fingerprint.GfxObjId);
        _pickDigest.Add(fingerprint.LocalToWorld);
        _pickDigest.Add(fingerprint.Geometry.Low);
        _pickDigest.Add(fingerprint.Geometry.High);
    }
}
