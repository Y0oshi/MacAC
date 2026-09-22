using System.Diagnostics;
using System.Numerics;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmPaintRouter
{
    public PaintStats PreviousPaintStats { get; private set; }

    internal int PreviousCompoundWarmupQueuedTally { get; private set; }

    public void DirtyCompoundWarmupReadiness()
    {
        CompoundTexturesPrimed = false;
        PreviousCompoundWarmupQueuedTally = 1;
        _compoundWarmupFifo.Clear();
        _compoundWarmupFollowed.Clear();
        _compoundWarmupSrc = null;
        _compoundWarmupSrcGen = 0;
        _compoundWarmupDestChamber = 0;
        _compoundWarmupRadius = 0;
        _compoundWarmupScanOrdinal = 0;
        _compoundWarmupScanDone = true;
    }

    public void ReadyCompoundTextures(
        IReadOnlyList<RealmActor> actors,
        ulong actorGen,
        uint destChamber,
        int radius)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (RequiresCompoundWarmupReassemble(
                _compoundWarmupSrc,
                _compoundWarmupDestChamber,
                _compoundWarmupRadius,
                actors,
                destChamber,
                radius))
        {
            ReassembleCompoundWarmupFifo(
                actors,
                actorGen,
                destChamber,
                radius);
        }
        else if (ShouldCommenceCompoundWarmupRescan(
                     _compoundWarmupScanDone,
                     _compoundWarmupSrcGen,
                     actorGen))
        {
            CommenceCompoundWarmupRescan(actors.Count, actorGen);
        }
        if (CompoundTexturesPrimed)
            return;

        _compoundWarmupScanOrdinal =
            Math.Min(_compoundWarmupScanOrdinal, actors.Count);
        int scanFinish = CompoundWarmupScanFinish(
            _compoundWarmupScanOrdinal,
            actors.Count);
        for (; _compoundWarmupScanOrdinal < scanFinish; _compoundWarmupScanOrdinal++)
        {
            RealmActor actor = actors[_compoundWarmupScanOrdinal];
            if (IsCompoundWarmupContender(actor, destChamber, radius)
                && _compoundWarmupFollowed.Add(actor))
            {
                _compoundWarmupFifo.Enqueue(actor);
            }
        }
        _compoundWarmupScanDone = _compoundWarmupScanOrdinal == actors.Count;
        if (ShouldCommenceCompoundWarmupRescan(
                _compoundWarmupScanDone,
                _compoundWarmupSrcGen,
                actorGen))
        {
            CommenceCompoundWarmupRescan(actors.Count, actorGen);
        }

        int contendersThisPass = Math.Min(
            _compoundWarmupFifo.Count,
            CeilingCompoundWarmupReadyActorsPerCycle);
        for (int idx = 0; idx < contendersThisPass; idx++)
        {
            RealmActor actor = _compoundWarmupFifo.Dequeue();
            CompoundWarmupOutcome outcome = ReadyCompoundActor(actor);
            if (outcome != CompoundWarmupOutcome.Complete)
                _compoundWarmupFifo.Enqueue(actor);
            if (outcome == CompoundWarmupOutcome.UploadBudgetBlocked)
                break;
        }

        PreviousCompoundWarmupQueuedTally = _compoundWarmupFifo.Count
            + (_compoundWarmupScanDone ? 0 : actors.Count - _compoundWarmupScanOrdinal);
        CompoundTexturesPrimed = _compoundWarmupScanDone
            && _compoundWarmupFifo.Count == 0;
        if (!CompoundTexturesPrimed
            && MacAC.Wire.WireTelemetry.SensorUnveil)
        {
            InspectUnveilWarmupStall();
        }
    }

    private void InspectUnveilWarmupStall()
    {
        long instant = Stopwatch.GetTimestamp();
        if (_sensorWarmupPreviousEmitTs != 0
            && instant - _sensorWarmupPreviousEmitTs < Stopwatch.Frequency)
        {
            return;
        }
        _sensorWarmupPreviousEmitTs = instant;

        var queuedIdents = new System.Text.StringBuilder();
        int listed = 0;
        foreach (RealmActor actor in _compoundWarmupFifo)
        {
            if (listed >= 4)
                break;
            ulong gfxObjRefIdent = actor.MeshRefs.Count > 0
                ? actor.MeshRefs[0].GfxObjId
                : 0;
            if (listed > 0)
                queuedIdents.Append(',');
            queuedIdents.Append($"0x{gfxObjRefIdent:X8}");
            listed++;
        }
        Console.WriteLine(
            $"[composite-warmup] STALL pending={PreviousCompoundWarmupQueuedTally}"
            + $" queue={_compoundWarmupFifo.Count}"
            + $" scanComplete={_compoundWarmupScanDone}"
            + $" uploadOpen={_textures.CanBeginCompoundPush}"
            + $" firstPending=[{queuedIdents}]");
    }

    internal static bool RequiresCompoundWarmupReassemble(
        IReadOnlyList<RealmActor>? latestSrc,
        uint latestDestChamber,
        int latestRadius,
        IReadOnlyList<RealmActor> upcomingSrc,
        uint upcomingDestChamber,
        int upcomingRadius) =>
        !ReferenceEquals(latestSrc, upcomingSrc)
        || latestDestChamber != upcomingDestChamber
        || latestRadius != upcomingRadius;

    internal static bool ShouldCommenceCompoundWarmupRescan(
        bool scanDone,
        ulong scanGen,
        ulong actorGen) =>
        scanDone && scanGen != actorGen;

    internal static int CompoundWarmupScanFinish(
        int scanOrdinal,
        int actorTally)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scanOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegative(actorTally);
        return Math.Min(
            actorTally,
            scanOrdinal + CeilingCompoundWarmupScanActorsPerCycle);
    }

    private void ReassembleCompoundWarmupFifo(
        IReadOnlyList<RealmActor> actors,
        ulong actorGen,
        uint destChamber,
        int radius)
    {
        _compoundWarmupFifo.Clear();
        _compoundWarmupFollowed.Clear();
        _compoundWarmupSrc = actors;
        _compoundWarmupSrcGen = actorGen;
        _compoundWarmupDestChamber = destChamber;
        _compoundWarmupRadius = radius;
        _compoundWarmupScanOrdinal = 0;
        _compoundWarmupScanDone = actors.Count == 0;
        PreviousCompoundWarmupQueuedTally = actors.Count;
        CompoundTexturesPrimed = _compoundWarmupScanDone;
    }

    private void CommenceCompoundWarmupRescan(
        int actorTally,
        ulong actorGen)
    {
        _compoundWarmupSrcGen = actorGen;
        _compoundWarmupScanOrdinal = 0;
        _compoundWarmupScanDone = actorTally == 0;
        CompoundTexturesPrimed =
            _compoundWarmupScanDone && _compoundWarmupFifo.Count == 0;
    }

    internal static bool IsCompoundWarmupContender(
        RealmActor actor,
        uint destChamber,
        int radius)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        if (destChamber != 0)
        {
            if (!TryFetchActorChamber(actor, out uint actorChamber)
                || !IsWithinLbRadius(actorChamber, destChamber, radius))
            {
                return false;
            }
        }

        if (actor.SwatchOverride is not null)
            return true;

        for (int triMeshOrdinal = 0; triMeshOrdinal < actor.MeshRefs.Count; triMeshOrdinal++)
        {
            if (actor.MeshRefs[triMeshOrdinal].CanvasOverrides is { Count: > 0 })
                return true;
        }

        return false;
    }

    private CompoundWarmupOutcome ReadyCompoundActor(RealmActor actor)
    {
        bool queued = false;
        SwatchCompoundPersona swatchPersona = actor.SwatchOverride is not null
            ? BitmapStash.FetchSwatchPersona(actor.SwatchOverride)
            : default;
        for (int triMeshOrdinal = 0; triMeshOrdinal < actor.MeshRefs.Count; triMeshOrdinal++)
        {
            TriMeshRef triMeshRef = actor.MeshRefs[triMeshOrdinal];
            ThingRasterizeBlob? rasterizeBlob = _triMeshBridge.TryFetchRenderData(triMeshRef.GfxObjId);
            if (rasterizeBlob is null)
            {
                _triMeshBridge.SecureFetched(triMeshRef.GfxObjId);
                queued = true;
                continue;
            }

            if (rasterizeBlob.IsSetup && rasterizeBlob.SetupParts.Count > 0)
            {
                for (int pieceOrdinal = 0; pieceOrdinal < rasterizeBlob.SetupParts.Count; pieceOrdinal++)
                {
                    ulong pieceIdent = rasterizeBlob.SetupParts[pieceOrdinal].GfxObjId;
                    ThingRasterizeBlob? pieceBlob = _triMeshBridge.TryFetchRenderData(pieceIdent);
                    if (pieceBlob is null)
                    {
                        _triMeshBridge.SecureFetched(pieceIdent);
                        queued = true;
                        continue;
                    }
                    if (!ReadyCompoundLots(actor, triMeshRef, pieceBlob, swatchPersona))
                        queued = true;
                    if (!_textures.CanBeginCompoundPush && queued)
                        return CompoundWarmupOutcome.UploadBudgetBlocked;
                }
            }
            else
            {
                if (!ReadyCompoundLots(actor, triMeshRef, rasterizeBlob, swatchPersona))
                    queued = true;
                if (!_textures.CanBeginCompoundPush && queued)
                    return CompoundWarmupOutcome.UploadBudgetBlocked;
            }
        }
        return queued ? CompoundWarmupOutcome.Pending : CompoundWarmupOutcome.Complete;
    }

    private bool ReadyCompoundLots(
        RealmActor actor,
        TriMeshRef triMeshRef,
        ThingRasterizeBlob rasterizeBlob,
        SwatchCompoundPersona swatchPersona)
    {
        bool done = true;
        for (int lotOrdinal = 0; lotOrdinal < rasterizeBlob.Batches.Count; lotOrdinal++)
        {
            _ = LocateTexture(
                actor,
                triMeshRef,
                rasterizeBlob.Batches[lotOrdinal],
                swatchPersona,
                out bool compoundQueued);
            if (compoundQueued)
                done = false;
            if (compoundQueued && !_textures.CanBeginCompoundPush)
                break;
        }
        return done;
    }

    private static bool TryFetchActorChamber(RealmActor actor, out uint chamber)
    {
        if (actor.VisChamberIdent is uint settled)
        {
            chamber = settled;
            return true;
        }
        chamber = 0;
        return false;
    }

    private static bool IsWithinLbRadius(uint chamber, uint middle, int radius)
    {
        int x = (int)(chamber >> 24);
        int y = (int)((chamber >> 16) & 0xFFu);
        int middleX = (int)(middle >> 24);
        int middleY = (int)((middle >> 16) & 0xFFu);
        return Math.Abs(x - middleX) <= radius && Math.Abs(y - middleY) <= radius;
    }

    public void BeginFrame(int cycleSocket)
    {
        _ = cycleSocket;
        RestartRealmXformCycle();
        if (_clusterCycle == long.MaxValue)
            throw new InvalidOperationException("Instance-group frame identity was exhausted");

        ApplyScratchRetention(_tempPeakUnits);
        _tempPeakUnits = 0;
        _clusterCycle++;
        PruneInstClustersUnusedPriorCycle(
            _clusters,
            _retiredClusterTags,
            _clusterCycle - 1);
        _dynamicCycleBegun = true;
        _latestRasterizeTableauWatcher?.CommenceRouterCycle();
    }

    internal void AssignLatestRasterizeTableauWatcher(
        ICurrentRenderRouterWatcher? watcher) =>
        _latestRasterizeTableauWatcher = watcher;

    internal void CancelLatestRasterizeTableauWatcherCycle() =>
        _latestRasterizeTableauWatcher?.CancelRouterCycle();

    public void AssignTableauLamps(IReadOnlyList<LightEmitter>? ptCapture)
        => _ptCapture = ptCapture;

    public void AssignClipZoneSsbo(uint sharedClipZoneSsbo)
        => _sharedClipZoneSsbo = sharedClipZoneSsbo;

    internal static (uint Slot, bool Culled) LocateSocketForCycle() => (0u, false);

    public static Matrix4x4 ConstructPieceRealmMatrix(
        Matrix4x4 actorRealm,
        Matrix4x4 animOverride,
        Matrix4x4 restPosture)
        => restPosture * animOverride * actorRealm;

    internal static StrideResult TraverseActors(
        IEnumerable<LandblockListing> lbListings,
        FrustumFacets? frustum,
        uint? neverPruneLbIdent,
        HashSet<uint>? shownChamberIdents,
        HashSet<uint>? movingActorIdents)
    {
        var temp = new List<(RealmActor Entity, int MeshRefIndex, uint LandblockId)>();
        var outcome = new StrideResult { ToPaint = temp };
        TraverseActorsInto(
            lbListings, frustum, neverPruneLbIdent,
            shownChamberIdents, movingActorIdents, temp, ref outcome);
        return outcome;
    }

    internal static uint LocateStashLbHint(RealmActor actor, uint tupleLbIdent)
        => actor.ParentCellId is uint pc ? ((pc & 0xFFFF0000u) | 0xFFFFu) : tupleLbIdent;

    internal static uint LocateStashLbHint(
        in RasterizeInstContender actor) =>
        actor.StashLbIdent;

    private static void AssembleLatestContenderTuples(
        List<(RealmActor Entity, int MeshRefIndex, uint LandblockId)> src,
        HashSet<uint>? movingActorIdents,
        List<RasterizeInstTuple> dest)
    {
        dest.Clear();
        if (dest.Capacity < src.Count)
            dest.Capacity = src.Count;

        int ordinal = 0;
        while (ordinal < src.Count)
        {
            (RealmActor actor, _, uint tupleLbIdent) = src[ordinal];
            int finish = ordinal + 1;
            while (finish < src.Count
                   && ReferenceEquals(src[finish].Entity, actor)
                   && src[finish].LandblockId == tupleLbIdent)
            {
                finish++;
            }

            int triMeshPieceTally = finish - ordinal;
            var contender =
                RasterizeInstContender.FromRealmActor(
                    actor,
                    movingActorIdents?.Contains(actor.Id) == true,
                    triMeshPieceTally,
                    tupleLbIdent);
            for (; ordinal < finish; ordinal++)
            {
                int triMeshRefOrdinal = src[ordinal].MeshRefIndex;
                dest.Add(new RasterizeInstTuple(
                    contender,
                    triMeshRefOrdinal,
                    actor.MeshRefs[triMeshRefOrdinal]));
            }
        }
    }

    internal bool ReadyPrivateActorAssetList(
        IReadOnlyList<RealmActor> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        bool done = true;
        for (int idx = 0; idx < actors.Count; idx++)
        {
            if (ReadyCompoundActor(actors[idx]) != CompoundWarmupOutcome.Complete)
                done = false;
        }
        return done;
    }

    private float ActorDensity(uint srvOid)
    {
        float seeThrough = _hierarchicalSeeThrough?.Invoke(srvOid) ?? 0f;
        return 1f - Math.Clamp(seeThrough, 0f, 1f);
    }

    // Whether there is a mesh source to draw from
    private bool TriMeshSrcPrimed() =>
        _triMeshBridge.TriMeshKeeper?.GlobalBuf is { HasStores: true };

    private bool CommenceActorRelay(
        IClientCamera cam,
        out Matrix4x4 lensProj,
        out Vector3 camRealmLocus)
    {
        _pickIllumination?.PulseIllumination();
        lensProj = cam.View * cam.Projection;
        _missRequested.Clear();

        bool telemetryTurnedOn = string.Equals(
            Environment.GetEnvironmentVariable("MACAC_WB_DIAG"),
            "1",
            StringComparison.Ordinal);

        _cpuStopwatch.Restart();
        camRealmLocus = Vector3.Zero;
        if (Matrix4x4.Invert(cam.View, out Matrix4x4 invLens))
            camRealmLocus = invLens.Translation;
        return telemetryTurnedOn;
    }

    private void BroadcastStashedPickPieces(
        ActorShelfEntry stashedListing,
        in RasterizeInstContender actor,
        Matrix4x4 actorRealm)
    {
        foreach (ShelvedPickingPart piece in stashedListing.PickPieces)
        {
            _pickDrain!.AddVisiblePart(
                actor.ServerGuid,
                actor.LocalEntityId,
                piece.PartIndex,
                piece.GfxObjId,
                piece.RestPose * actorRealm);
        }
    }

    private static IndirectClusterFeed ToFeed(InstCluster g) => new(
        IndexCount: g.IdxTally,
        FirstIndex: g.LeadIndex,
        BaseVertex: g.BaseVertex,
        InstanceCount: g.InstCount,
        FirstInstance: g.LeadInst,
        TextureIndex: g.TextureSlot.Index,
        TextureLayer: g.TextureStratum,
        Translucency: g.Translucency,
        MaterialState: g.MaterialState,
        SurfaceOpacity: g.SurfaceOpacity,
        CullMode: g.CullMode,
        FoliageFlags: g.FoliageFlagSet);

    internal static InstanceArrangementCounts PartitionInstanceGroups(
        IEnumerable<InstCluster> clusters,
        bool deferSeeThru,
        Vector3 camRealmLocus,
        List<InstCluster> solid,
        List<InstCluster> seeThru)
    {
        solid.Clear();
        seeThru.Clear();

        int shownInsts = 0;
        int immediateInsts = 0;
        foreach (InstCluster cluster in clusters)
        {
            int tally = cluster.Instances.Count;
            if (tally == 0)
                continue;

            cluster.InstCount = tally;
            Matrix4x4 lead = cluster.Instances.Model(0);
            var clusterLocus = new Vector3(lead.M41, lead.M42, lead.M43);
            cluster.SortDistance = Vector3.DistanceSquared(camRealmLocus, clusterLocus);
            shownInsts += tally;

            if (IsSolid(cluster.Translucency))
            {
                solid.Add(cluster);
                immediateInsts += tally;
            }
            else
            {
                seeThru.Add(cluster);
                if (!deferSeeThru)
                    immediateInsts += tally;
            }
        }

        return new InstanceArrangementCounts(shownInsts, immediateInsts);
    }

    private void JunctureImmediateCluster(InstCluster cluster, ref int cur)
    {
        cluster.LeadInst = cur;
        for (int idx = 0; idx < cluster.Instances.Count; idx++)
        {
            _stage.Write(cur++, cluster.Instances[idx]);
        }
    }

    private static ClusterTag ToTag(InstCluster g) => new(
        g.LeadIndex,
        g.BaseVertex,
        g.IdxTally,
        g.TextureSlot,
        g.TextureStratum,
        g.Translucency,
        g.MaterialState,
        g.FoliageFlagSet,
        g.SurfaceOpacity,
        g.CullMode);

    internal static InstCluster BuildClusterFromTag(
        ClusterTag tag,
        long enrollment,
        long cycle) => new(enrollment)
        {
            LeadIndex = tag.FirstIndex,
            BaseVertex = tag.BaseVertex,
            IdxTally = tag.IndexCount,
            TextureSlot = tag.TextureSlot,
            TextureStratum = tag.TextureLayer,
            Translucency = tag.Translucency,
            MaterialState = tag.MaterialState,
            CullMode = tag.CullMode,
            FoliageFlagSet = tag.FoliageFlags,
            SurfaceOpacity = tag.SurfaceOpacity,
            PreviousConsumedCycle = cycle,
        };

    private void ObserveCurrentDispatcherSubmission(
        int shownInstTally,
        int immediateInstTally,
        bool deferSeeThru)
    {
        ICurrentRenderRouterWatcher? watcher =
            _latestRasterizeTableauWatcher;
        if (watcher is null)
            return;

        CurrentRenderRouterSubmission submission =
            CreateDispatcherSubmission(
                shownInstTally,
                immediateInstTally,
                deferSeeThru,
                _solidDraws,
                _translucentDraws,
                _alphaFingerprintTemp);
        watcher.WatchRouterSubmission(in submission);
    }

    private void ObserveClassifiedDispatcherSubmission(
        bool watchLatestTrail,
        int shownInstTally,
        int immediateInstTally,
        bool deferSeeThru)
    {
        if (watchLatestTrail)
        {
            ObserveCurrentDispatcherSubmission(
                shownInstTally,
                immediateInstTally,
                deferSeeThru);
        }
    }

    private static RenderStageHash128 AssembleSolidSubmissionDigest(
        IReadOnlyList<InstCluster> clusters)
    {
        ulong xorLo = 0;
        ulong xorHi = 0;
        ulong totalLo = 0;
        ulong totalHi = 0;
        for (int ordinal = 0; ordinal < clusters.Count; ordinal++)
        {
            var clusterDigest = StableRasterizeHash128.Create();
            AppendSolidSubmissionCluster(
                ref clusterDigest,
                clusters[ordinal]);
            RenderStageHash128 digest = clusterDigest.Finish();
            xorLo ^= digest.Low;
            xorHi ^= digest.High;
            totalLo = unchecked(totalLo + digest.Low);
            totalHi = unchecked(totalHi + digest.High);
        }

        var hash = StableRasterizeHash128.Create();
        hash.Add(clusters.Count);
        hash.Add(xorLo);
        hash.Add(xorHi);
        hash.Add(totalLo);
        hash.Add(totalHi);
        return hash.Finish();
    }

    private static RenderStageHash128 BuildTransparentSubmissionDigest(
        IReadOnlyList<InstCluster> clusters,
        List<ClientAlphaFingerprint> temp)
    {
        temp.Clear();
        for (int clusterOrdinal = 0;
             clusterOrdinal < clusters.Count;
             clusterOrdinal++)
        {
            InstCluster cluster = clusters[clusterOrdinal];
            for (int instOrdinal = 0;
                 instOrdinal < cluster.Instances.Count;
                 instOrdinal++)
            {
                temp.Add(new ClientAlphaFingerprint(
                    cluster,
                    instOrdinal,
                    cluster.Instances.SubmissionOrder(instOrdinal)));
            }
        }
        temp.Sort(AlphaSubmissionOrderingComparer.Instance);

        var digest = StableRasterizeHash128.Create();
        digest.Add(temp.Count);
        for (int ordinal = 0; ordinal < temp.Count; ordinal++)
        {
            ClientAlphaFingerprint listing = temp[ordinal];
            ClusterTag tag = ToTag(listing.Group);
            digest.Add(tag.FirstIndex);
            digest.Add(tag.BaseVertex);
            digest.Add(tag.IndexCount);
            digest.Add(tag.TextureSlot.Index);
            digest.Add(tag.TextureLayer);
            digest.Add((int)tag.Translucency);
            digest.Add((int)tag.CullMode);
            digest.Add(tag.FoliageFlags);
            digest.Add((int)tag.MaterialState.ToDenseByte());
            digest.Add(tag.SurfaceOpacity);
            AppendSubmissionInst(
                ref digest,
                listing.Group,
                listing.InstanceIndex);
        }
        return digest.Finish();
    }

    private static void AppendSolidSubmissionCluster(
        ref StableRasterizeHash128 hash,
        InstCluster cluster)
    {
        ClusterTag tag = ToTag(cluster);
        hash.Add(tag.FirstIndex);
        hash.Add(tag.BaseVertex);
        hash.Add(tag.IndexCount);
        hash.Add(tag.TextureSlot.Index);
        hash.Add(tag.TextureLayer);
        hash.Add((int)tag.Translucency);
        hash.Add((int)tag.CullMode);
        hash.Add(tag.FoliageFlags);
        // The surface state and authored opacity are part of the draw identity, so a digest that
        // skipped them would call two differently-drawn frames identical.
        hash.Add((int)tag.MaterialState.ToDenseByte());
        hash.Add(tag.SurfaceOpacity);
        hash.Add(cluster.Instances.Count);

        ulong xorLo = 0;
        ulong xorHi = 0;
        ulong totalLo = 0;
        ulong totalHi = 0;
        for (int ordinal = 0;
             ordinal < cluster.Instances.Count;
             ordinal++)
        {
            var instDigest =
                StableRasterizeHash128.Create();
            AppendSubmissionInst(
                ref instDigest,
                cluster,
                ordinal);
            RenderStageHash128 digest = instDigest.Finish();
            xorLo ^= digest.Low;
            xorHi ^= digest.High;
            totalLo = unchecked(totalLo + digest.Low);
            totalHi = unchecked(totalHi + digest.High);
        }

        hash.Add(xorLo);
        hash.Add(xorHi);
        hash.Add(totalLo);
        hash.Add(totalHi);
    }

    private static void AppendSubmissionInst(
        ref StableRasterizeHash128 digest,
        InstCluster cluster,
        int ordinal)
    {
        digest.Add(cluster.Instances.Model(ordinal));
        digest.Add(cluster.Instances.ClipSlot(ordinal));
        InstLampGroup lamps = cluster.Instances.Lamps(ordinal);
        for (int lampOrdinal = 0;
             lampOrdinal < LightKeeper.UpperLightsPerObject;
             lampOrdinal++)
        {
            digest.Add(lamps[lampOrdinal]);
        }
        digest.Add(cluster.Instances.Inside(ordinal));
        digest.Add(cluster.Instances.SpecificsBucket(ordinal));
        digest.Add(cluster.Instances.Opacity(ordinal));
        digest.Add(cluster.Instances.PickIllumination(ordinal));
    }
}
