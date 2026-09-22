using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Stage;

internal sealed partial class CurrentRenderStageOracle
{
    internal IReadOnlyList<CurrentRenderMirrorFingerprint> Projections =>
        _projections;

    internal IReadOnlyList<LatestRasterizePLensContenderFingerprint>
        PLensContenders => _pviewContenders;

    internal IReadOnlyList<CurrentRenderRouterFingerprint>
        RouterContenders => _routerContenders;

    internal IReadOnlyList<CurrentRenderRouterSubmission>
        RouterSubmissions => _routerSubmissions;

    internal IReadOnlyList<CurrentRenderPickingFingerprint>
        PickPieces => _pickPieces;

    public void ScrapCycle()
    {
        if (!_cycleOpen)
            return;

        _cycleOpen = false;
        _projections.Clear();
        _exteriorStaticTally = 0;
        _chamberStaticTally = 0;
        _dynamicTally = 0;
        _abortedCycles = checked(_abortedCycles + 1);
        Snapshot = Snapshot with { AbortedFrames = _abortedCycles };
    }

    public void CancelPLensCycle()
    {
        if (!_pviewCycleOpen)
            return;

        _pviewCycleOpen = false;
        _pviewContenders.Clear();
        _abortedPLensCycles = checked(_abortedPLensCycles + 1);
        Snapshot = Snapshot with
        {
            AbortedPViewFrames = _abortedPLensCycles,
        };
    }

    public void CancelRouterCycle()
    {
        if (!_routerCycleOpen)
            return;

        _routerCycleOpen = false;
        _routerContenders.Clear();
        _routerSubmissions.Clear();
        _routerProjByActor.Clear();
        _abortedRouterCycles = checked(_abortedRouterCycles + 1);
        Snapshot = Snapshot with
        {
            AbortedDispatcherFrames = _abortedRouterCycles,
        };
    }

    public void CancelPickCycle()
    {
        if (!_pickCycleOpen)
            return;

        _pickCycleOpen = false;
        _pickPieces.Clear();
        _abortedPickCycles = checked(_abortedPickCycles + 1);
        Snapshot = Snapshot with
        {
            AbortedSelectionFrames = _abortedPickCycles,
        };
    }

    public void BeginFrame()
    {
        if (_cycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't begin a second frame prior to completing or aborting the first");
        }

        _cycleOpen = true;
        _projections.Clear();
        _projByActor.Clear();
        _exteriorStaticTally = 0;
        _chamberStaticTally = 0;
        _dynamicTally = 0;
    }

    public void CommencePLensCycle()
    {
        if (_pviewCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't begin a second PView frame prior to completing or aborting the first");
        }

        _pviewCycleOpen = true;
        _pviewContenders.Clear();
        _pviewDigest = StableRasterizeHash128.Create();
    }

    public void CommenceRouterCycle()
    {
        _routerCycleOpen = true;
        _routerContenders.Clear();
        _routerSubmissions.Clear();
        _routerProjByActor.Clear();
        _routerPaintTally = 0;
        _routerActorTally = 0;
        _routerInstTally = 0;
        _routerSolidClusterTally = 0;
        _routerSeeThruClusterTally = 0;
        _routerDigest = StableRasterizeHash128.Create();
        Snapshot = Snapshot with
        {
            DispatcherFrameSequence = checked(++_routerCycleSeries),
            AbortedDispatcherFrames = _abortedRouterCycles,
            DispatcherDrawCount = 0,
            DispatcherEntityCount = 0,
            DispatcherMeshRefCount = 0,
            DispatcherInstanceCount = 0,
            DispatcherOpaqueGroupCount = 0,
            DispatcherTransparentGroupCount = 0,
            DispatcherDigest = _routerDigest.Finish(),
        };
    }

    public void CommencePickCycle()
    {
        if (_pickCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't begin a second selection frame prior to completing or aborting the first");
        }

        _pickCycleOpen = true;
        _pickPieces.Clear();
        _pickDigest = StableRasterizeHash128.Create();
    }

    public void Complete(InteriorActorPartition.ClientResult outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (!_cycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't complete without an open frame");
        }

        int chamberStaticTally = 0;
        foreach (List<RealmActor> actors in outcome.ByChamber.Values)
            chamberStaticTally = checked(chamberStaticTally + actors.Count);
        if (outcome.OutdoorStatic.Count != _exteriorStaticTally
            || chamberStaticTally != _chamberStaticTally
            || outcome.Dynamics.Count != _dynamicTally
            || _projections.Count
                != checked(_exteriorStaticTally + _chamberStaticTally + _dynamicTally))
        {
            throw new InvalidOperationException(
                "The current render-scene oracle diverged from the partition it observed");
        }

        _projections.Sort(CurrentRenderMirrorFingerprintComparer.Instance);
        var aggregate = StableRasterizeHash128.Create();
        aggregate.Add(_projections.Count);
        foreach (CurrentRenderMirrorFingerprint proj in _projections)
            AppendProj(ref aggregate, in proj);

        _cycleOpen = false;
        Snapshot = new CurrentRenderStageOracleCapture(
            Enabled: true,
            CompletedFrameSequence: checked(++_cycleSeries),
            AbortedFrames: _abortedCycles,
            ProjectionCount: _projections.Count,
            OutdoorStaticCount: _exteriorStaticTally,
            CellStaticCount: _chamberStaticTally,
            DynamicCount: _dynamicTally,
            CellBucketCount: outcome.ByChamber.Count,
            Digest: aggregate.Finish(),
            CompletedPViewFrameSequence: Snapshot.CompletedPViewFrameSequence,
            AbortedPViewFrames: _abortedPLensCycles,
            PViewCandidateCount: Snapshot.PViewCandidateCount,
            PViewDigest: Snapshot.PViewDigest,
            DispatcherFrameSequence: Snapshot.DispatcherFrameSequence,
            AbortedDispatcherFrames: _abortedRouterCycles,
            DispatcherDrawCount: Snapshot.DispatcherDrawCount,
            DispatcherEntityCount: Snapshot.DispatcherEntityCount,
            DispatcherMeshRefCount: Snapshot.DispatcherMeshRefCount,
            DispatcherInstanceCount: Snapshot.DispatcherInstanceCount,
            DispatcherOpaqueGroupCount: Snapshot.DispatcherOpaqueGroupCount,
            DispatcherTransparentGroupCount:
                Snapshot.DispatcherTransparentGroupCount,
            DispatcherDigest: Snapshot.DispatcherDigest,
            CompletedSelectionFrameSequence:
                Snapshot.CompletedSelectionFrameSequence,
            AbortedSelectionFrames: _abortedPickCycles,
            SelectionPartCount: Snapshot.SelectionPartCount,
            SelectionDigest: Snapshot.SelectionDigest);
    }

    public void ConcludePLensCycle()
    {
        if (!_pviewCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't complete PView without an open frame");
        }

        _pviewCycleOpen = false;
        Snapshot = Snapshot with
        {
            CompletedPViewFrameSequence = checked(++_pviewCycleSeries),
            AbortedPViewFrames = _abortedPLensCycles,
            PViewCandidateCount = _pviewContenders.Count,
            PViewDigest = _pviewDigest.Finish(),
        };
    }

    public void ConcludePickCycle()
    {
        if (!_pickCycleOpen)
        {
            throw new InvalidOperationException(
                "The current render-scene oracle can't complete selection without an open frame");
        }

        _pickCycleOpen = false;
        Snapshot = Snapshot with
        {
            CompletedSelectionFrameSequence =
                checked(++_pickCycleSeries),
            AbortedSelectionFrames = _abortedPickCycles,
            SelectionPartCount = _pickPieces.Count,
            SelectionDigest = _pickDigest.Finish(),
        };
    }

    internal static RenderStageHash128 FingerprintPickGeo(
        CanonPickMesh triMesh)
    {
        StableRasterizeHash128 geo = StableRasterizeHash128.Create();
        geo.Add(triMesh.SphereCenter);
        geo.Add(triMesh.SphereRadius);
        geo.Add(triMesh.Polygons.Count);
        for (int polygOrdinal = 0;
             polygOrdinal < triMesh.Polygons.Count;
             ++polygOrdinal)
        {
            geo = FingerprintPickGeoLoop(triMesh, polygOrdinal, geo);
        }

        return geo.Finish();
    }

    private static StableRasterizeHash128 FingerprintPickGeoLoop(CanonPickMesh triMesh, int polygOrdinal, StableRasterizeHash128 geo)
    {
        var polyg = triMesh.Polygons[polygOrdinal];
        geo.Add(polyg.SingleSided);
        geo.Add(polyg.Vertices.Count);
        for (int vertOrdinal = 0;
                         vertOrdinal < polyg.Vertices.Count;
                         ++vertOrdinal)
        {
            geo.Add(polyg.Vertices[vertOrdinal]);
        }

        return geo;
    }

    private static InteriorActorPartition.ProjClass ProjClassOf(
        RealmActor actor)
    {
        return actor.ServerGuid is not 0
            ? InteriorActorPartition.ProjClass.Dynamic
            : InteriorActorPartition.IsIndoorCellId(actor.ParentCellId)
                ? InteriorActorPartition.ProjClass.CellStatic
                : InteriorActorPartition.ProjClass.OutdoorStatic;
    }

    private static void AppendCanvasSubstitutions(
        ref StableRasterizeHash128 geo,
        IReadOnlyDictionary<uint, uint>? substitutions)
    {
        var fingerprint =
            CreateSurfaceOverrideFingerprint(substitutions);
        geo.Add(fingerprint.Low);
        geo.Add(fingerprint.High);
    }

    private static void AppendPLensContender(
        ref StableRasterizeHash128 digest,
        in LatestRasterizePLensContenderFingerprint contender)
    {
        digest.Add(contender.Sequence);
        digest.Add((byte)contender.Route);
        digest.Add(contender.RouteIndex);
        digest.Add(contender.CellId);
        var proj = contender.Projection;
        AppendProj(ref digest, in proj);
    }

    private static void AppendRouterContender(
        ref StableRasterizeHash128 digest,
        in CurrentRenderRouterFingerprint contender)
    {
        digest.Add(contender.Sequence);
        digest.Add(contender.DrawSequence);
        digest.Add((int)contender.Set);
        digest.Add(contender.MeshRefIndex);
        digest.Add(contender.TupleLandblockId);
        digest.Add(contender.CacheLandblockId);
        digest.Add(contender.GfxObjId);
        digest.Add(contender.PartTransform);
        digest.Add(contender.SurfaceOverrides.Low);
        digest.Add(contender.SurfaceOverrides.High);
        var proj = contender.Projection;
        AppendProj(ref digest, in proj);
    }

    private static void AppendProj(
        ref StableRasterizeHash128 digest,
        in CurrentRenderMirrorFingerprint proj)
    {
        digest.Add((byte)proj.ProjectionClass);
        digest.Add(proj.LandblockId);
        digest.Add(proj.EntityId);
        digest.Add(proj.ServerGuid);
        digest.Add(proj.SourceId);
        digest.Add(proj.ParentCellId);
        digest.Add(proj.EffectCellId);
        digest.Add(proj.BuildingShellAnchorCellId);
        digest.Add(proj.Flags);
        digest.Add(proj.MeshCount);
        digest.Add(proj.Transform.Low);
        digest.Add(proj.Transform.High);
        digest.Add(proj.Geometry.Low);
        digest.Add(proj.Geometry.High);
        digest.Add(proj.Appearance.Low);
        digest.Add(proj.Appearance.High);
    }
}
