using System.Numerics;
using System.Runtime.CompilerServices;
using MacAC.Dat;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmPaintRouter
{

    public void Dispose()
    {
        if (_destroyed || _disposing) return;
        _disposing = true;
        try
        {
            if (_teardownAssetList is null)
            {
                var releases = new List<(string Name, Action Release)>();
                TeardownRhiAssetList();
                _teardownAssetList = new RetryableAssetFreeRegister(releases);
            }

            var attempt = _teardownAssetList.Advance();
            if (!_teardownAssetList.IsComplete)
            {
                throw attempt.ToException(
                    "One or more entity renderer resources could not be released.");
            }

            ConcludeTeardown();
            _teardownAssetList = null;
            _destroyed = true;

            if (attempt.HasMisses)
            {
                throw attempt.ToException(
                    "Entity renderer resources released with exceptional committed outcomes.");
            }
        }
        finally
        {
            _disposing = false;
        }
    }

    public static IndirectArrangementResult AssembleIndirectArrs(
        IReadOnlyList<IndirectClusterFeed> clusters,
        DrawElementsIndirectDirective[] indirectTemp,
        LotBlobPublic[] lotTemp,
        FaceCulling[]? pruneTemp = null)
    {
        int solidTally = 0;
        int seeThruTally = 0;

        foreach (var input in clusters)
        {
            if (IsSolid(input.Translucency)) ++solidTally;
            else ++seeThruTally;
        }

        int oi = 0;
        int ti = solidTally;

        foreach (var input in clusters)
        {
            var dec = new DrawElementsIndirectDirective
            {
                Count = (uint)input.IndexCount,
                InstTally = (uint)input.InstanceCount,
                LeadOrdinal = input.FirstIndex,
                BaseVert = input.BaseVertex,
                BaseInst = (uint)input.FirstInstance,
            };
            LotBlobPublic bd = new LotBlobPublic
            {
                TextureIndex = input.TextureIndex,
                SurfaceOpacity = input.SurfaceOpacity,
                TextureLayer = input.TextureLayer,
                Flags = 1u | input.FoliageFlags,
            };

            if (IsSolid(input.Translucency))
            {
                indirectTemp[oi] = dec;
                lotTemp[oi] = bd;
                pruneTemp?[oi] = input.CullMode;
                ++oi;
            }
            else
            {
                indirectTemp[ti] = dec;
                lotTemp[ti] = bd;
                pruneTemp?[ti] = input.CullMode;
                ++ti;
            }
        }

        return new IndirectArrangementResult(solidTally, seeThruTally, solidTally * PaintDirectiveStride);
    }

    public static bool IsSolidPublic(SeeThroughKind kind) => IsSolid(kind);

    internal static void ImposeStashStrike(
        ActorShelfEntry listing,
        Matrix4x4 actorRealm,
        Action<ClusterTag, Matrix4x4> affixInst)
    {
        foreach (var stashed in listing.Batches)
        {
            affixInst(
                stashed.Key,
                stashed.RestPose * actorRealm);
        }
    }

    internal static bool TryLocateStashedCluster(
        ShelvedBatch stashed,
        out InstCluster? cluster)
    {
        cluster = stashed.Group;
        return stashed.Ticket.StillNames(cluster);
    }

    internal static int PruneInstClustersUnusedPriorCycle(
        Dictionary<ClusterTag, InstCluster> clusters,
        List<ClusterTag> retiredTags,
        long oldestOnlineCycle)
    {
        retiredTags.Clear();
        foreach ((ClusterTag tag, InstCluster cluster) in clusters)
        {
            if (cluster.PreviousConsumedCycle < oldestOnlineCycle)
            {
                PruneInstClustersUnusedPriorCycleBranch(cluster, retiredTags, tag);
            }
        }

        foreach (ClusterTag tag in retiredTags)
            clusters.Remove(tag);

        int retiredTally = retiredTags.Count;
        retiredTags.Clear();
        return retiredTally;
    }

    private static void PruneInstClustersUnusedPriorCycleBranch(InstCluster cluster, List<ClusterTag> retiredTags, ClusterTag tag)
    {
        cluster.Retire();
        retiredTags.Add(tag);
    }

    internal static void FinalDrainFill(
        uint? fillActorIdent,
        uint fillLbIdent,
        ActorTaxonomyStash stash,
        List<ShelvedBatch> fillTemp,
        List<ShelvedPickingPart>? pickTemp = null)
    {
        if (fillActorIdent.HasValue && fillTemp.Count > 0)
        {
            stash.Fill(
                fillActorIdent.Value,
                fillLbIdent,
                [.. fillTemp],
                pickTemp?.ToArray());
            fillTemp.Clear();
        }
        pickTemp?.Clear();
    }

    internal static bool InsideObjectReceivesTorches(uint? ancestorChamberIdent)
    {
        return ancestorChamberIdent.HasValue
               && (ancestorChamberIdent.Value & 0xFFFFu) >= 0x0100u
               && (ancestorChamberIdent.Value & 0xFFFFu) != 0xFFFFu;
    }

    internal static bool ActorPasssShownChamberLatch(
        RealmActor actor,
        HashSet<uint>? shownChamberIdents,
        ActorSet set)
    {
        if (shownChamberIdents is null)
            return true;

        return actor.ParentCellId.HasValue ? shownChamberIdents.Contains(actor.ParentCellId.Value) : false;
    }

    internal static bool TryFetchUpcomingSpecificsDirectiveExec(
        ReadOnlySpan<DrawElementsIndirectDirective> directives,
        ReadOnlySpan<uint> specificsBuckets,
        int searchStart,
        int exclusiveFinish,
        out DetailDirectiveRun exec)
    {
        if (searchStart < 0
            || exclusiveFinish < searchStart
            || exclusiveFinish > directives.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchStart),
                "The requested detail-command search range is not valid");
        }

        int lead = searchStart;
        while (lead < exclusiveFinish
            && !DirectiveContainsSpecificsBucket(
                directives[lead],
                specificsBuckets))
        {
            ++lead;
        }
        if (lead == exclusiveFinish)
        {
            exec = default;
            return false;
        }

        int finish = lead + 1;
        while (finish < exclusiveFinish
            && DirectiveContainsSpecificsBucket(
                directives[finish],
                specificsBuckets))
        {
            ++finish;
        }
        exec = new DetailDirectiveRun(lead, finish - lead);
        return true;
    }

    internal static bool DirectiveContainsSpecificsBucket(
        DrawElementsIndirectDirective command,
        ReadOnlySpan<uint> specificsBuckets)
    {
        ulong lead = command.BaseInst;
        ulong finish = lead + command.InstTally;
        if (finish > (ulong)specificsBuckets.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "The indirect instance range exceeds the detail-category buffer");
        }

        for (ulong ordinal = lead; ordinal < finish; ++ordinal)
        {
            if (specificsBuckets[(int)ordinal] is not 0u)
                return true;
        }
        return false;
    }
    private void DeferTransparentGroups(Matrix4x4 lensProj)
    {
        var fifo = _alphaFifo!;
        if (_deferredAlpha.Count is 0)
            _postponedAlphaLensProj = lensProj;
        else if (_postponedAlphaLensProj != lensProj)
            throw new InvalidOperationException(
                "One retail alpha scope can't combine different view-projection matrices");

        _alphaFingerprintTemp.Clear();
        foreach (InstCluster cluster in _translucentDraws)
        {
            for (int idx = 0; idx < cluster.Instances.Count; ++idx)
            {
                _alphaFingerprintTemp.Add(new ClientAlphaFingerprint(
                    cluster,
                    idx,
                    cluster.Instances.SubmissionOrder(idx)));
            }
        }
        _alphaFingerprintTemp.Sort(
            AlphaSubmissionOrderingComparer.Instance);

        foreach (ClientAlphaFingerprint listing in _alphaFingerprintTemp)
        {
            var cluster = listing.Group;
            int idx = listing.InstanceIndex;
            var contender = new PostponedAlphaInst(ToTag(cluster), cluster.Instances[idx]);
            SubmitToAlphaFifo(
                fifo, cluster.Translucency, in contender, contender.Inst.SpecificsBucket is 1u, lensProj);
        }
    }

    private void SubmitToAlphaFifo(
        CanonAlphaFifo fifo,
        SeeThroughKind sort,
        in PostponedAlphaInst contender,
        bool isStructureShell,
        Matrix4x4 lensProj)
    {
        byte bitmask = CanonAlphaMeshRouter.ConcealFromSeeThroughSort(sort);
        bool specificsCanvasEngaged = isStructureShell
            && CanonDetailTextureContract.ShouldRasterize(_structureSpecificsTurnedOn(), _structureSpecifics)
            && _structureSpecifics.Tiling != 0f;
        var decision = CanonAlphaMeshRouter.Course(
            currentlyDrawingHeavens: false,
            delayBitmask: CanonAlphaMeshRouter.DefaultDelayBitmask,
            specificsCanvasEngaged: specificsCanvasEngaged,
            multiPassAlpha: false,
            subsetBitmask: bitmask,
            matlHasAlpha: false);

        if (decision.Action == CanonAlphaMeshAction.Immediate)
        {
            PaintImmediateAlphaInst(in contender, lensProj);
            return;
        }

        int ticket = _deferredAlpha.Count;
        _deferredAlpha.Add(contender);
        fifo.TryAffix(decision.List, _alphaSrc, ticket, decision.OverrideClipmap);

        if (decision.Action == CanonAlphaMeshAction.AppendClipAndImmediate)

            PaintImmediateAlphaInst(in contender, lensProj);

    }

    private void EmitPostponedAlphaListingSocket(int socket, in PostponedAlphaInst listing)
    {
        _stage.Write(socket, listing.Inst);

        ClusterTag tag = listing.Key;
        _lotBlob[socket] = BatchData.For(tag);
        _indirectDirectives[socket] =
            DrawElementsIndirectDirective.For(tag, firstInstance: socket, instanceCount: 1);
        _drawCullModes[socket] = tag.CullMode;
        _postponedAlphaSorts[socket] = tag.Translucency;
    }

    private void ReadyPostponedAlphaDraws(ReadOnlySpan<int> tickets)
    {
        if (tickets.Length is 0)
            return;

        var global = _triMeshBridge.TriMeshKeeper?.GlobalBuf;
        if (global is null || !TriMeshSrcPrimed())
            return;

        int tally = tickets.Length;
        SecurePostponedAlphaCap(tally);
        for (int idx = 0; idx < tally; ++idx)
            EmitPostponedAlphaListingSocket(idx, _deferredAlpha[tickets[idx]]);

        ReadyRhiAlphaSections(tally);
    }

    private void PaintImmediateAlphaInst(in PostponedAlphaInst listing, Matrix4x4 lensProj)
    {
        var global = _triMeshBridge.TriMeshKeeper?.GlobalBuf;
        if (global is null || !TriMeshSrcPrimed())
            return;

        SecurePostponedAlphaCap(1);
        EmitPostponedAlphaListingSocket(0, in listing);
        ReadyRhiAlphaSections(1);
        PaintImmediateAlphaInstRhi(
            global,
            listing.Key.Translucency,
            listing.Key.MaterialState,
            lensProj);
    }

    private void PaintReadiedAlphaLot(int firstPreparedDraw, int paintTally)
    {
        if (paintTally <= 0)
            return;
        if (firstPreparedDraw < 0
            || firstPreparedDraw > _deferredAlpha.Count - paintTally)
            throw new ArgumentOutOfRangeException(nameof(firstPreparedDraw));

        var global = _triMeshBridge.TriMeshKeeper?.GlobalBuf;
        if (global is null || !TriMeshSrcPrimed())
            return;

        PaintReadiedAlphaLotRhi(global, firstPreparedDraw, paintTally);
    }

    private void SecurePostponedAlphaCap(int tally)
    {
        FollowTempDemand(tally);
        _stage.EnsureRoom(tally);
        if (_lotBlob.Length < tally)
            _lotBlob = new BatchData[tally + 64];
        if (_indirectDirectives.Length < tally)
            _indirectDirectives = new DrawElementsIndirectDirective[tally + 64];
        if (_drawCullModes.Length < tally)
            _drawCullModes = new FaceCulling[tally + 64];
        if (_postponedAlphaSorts.Length < tally)
            _postponedAlphaSorts = new SeeThroughKind[tally + 64];
    }

    private void RestartPostponedAlphaSubmissions() => _deferredAlpha.Clear();

    private void FollowTempDemand(int units)
    {
        if (units > _tempPeakUnits)
            _tempPeakUnits = units;
    }

    private void ApplyScratchRetention(int observedUnits)
    {
        int latestCap = Math.Max(
            _stage.Capacity,
            Math.Max(
                _deferredAlpha.Capacity,
                Math.Max(_lotBlob.Length, _indirectDirectives.Length)));
        int octetsPerUnit = checked(
            16 * sizeof(float)
            + sizeof(uint)
            + sizeof(uint)
            + LightKeeper.UpperLightsPerObject * sizeof(int)
            + sizeof(uint)
            + sizeof(float)
            + Unsafe.SizeOf<Vector2>()
            + Unsafe.SizeOf<BatchData>()
            + Unsafe.SizeOf<DrawElementsIndirectDirective>()
            + Unsafe.SizeOf<FaceCulling>()
            + Unsafe.SizeOf<LotBlobPublic>()
            + Unsafe.SizeOf<SeeThroughKind>()
            + Unsafe.SizeOf<PostponedAlphaInst>());
        int markCap = _alphaTempRule.WatchAndPickCap(
            latestCap,
            observedUnits,
            octetsPerUnit,
            floorCap: 256,
            growthQuantum: 256);
        if (markCap >= latestCap)
            return;

        _stage.Reset(markCap);
        _lotBlob = new BatchData[markCap];
        _indirectDirectives = new DrawElementsIndirectDirective[markCap];
        _drawCullModes = new FaceCulling[markCap];
        _lotPublicTemp = new LotBlobPublic[markCap];
        _postponedAlphaSorts = new SeeThroughKind[markCap];
        _deferredAlpha.Capacity = markCap;
    }

    private static int ContrastSolidSubmissionOrdering(InstCluster a, InstCluster b) =>
        DrawOrdering.ByCullThenNearest(a.CullMode, a.SortDistance, b.CullMode, b.SortDistance);

    private static int ContrastSeeThruSubmissionOrdering(InstCluster a, InstCluster b) =>
        DrawOrdering.ByCullThenFarthest(a.CullMode, a.SortDistance, b.CullMode, b.SortDistance);

    private static int TallyPruneExecutions(FaceCulling[] manners, int beginDirective, int directiveTally)
    {
        if (directiveTally <= 0) return 0;

        int finish = beginDirective + directiveTally;
        int executions = 1;
        FaceCulling earlier = manners[beginDirective];
        for (int idx = beginDirective + 1; idx < finish; ++idx)
        {
            FaceCulling latest = manners[idx];
            if (latest == earlier) continue;
            ++executions;
            earlier = latest;
        }
        return executions;
    }

    private void MaybeDrainDiag()
    {
        long instant = Environment.TickCount64;
        if (instant - _previousTraceBeat > 5000)
        {
            long cpuMed = MedianMicros(_cpuSpecimens);
            long cpuP95 = Percentile95Micros(_cpuSpecimens);
            long gpuMed = MedianMicros(_gpuSpecimens);
            long gpuP95 = Percentile95Micros(_gpuSpecimens);
            const long AllowanceUs = 2000;
            string allowanceBit = cpuMed > AllowanceUs ? " BUDGET_OVER" : "";
            Console.WriteLine(
                $"[WB-DIAG]{allowanceBit} entSeen={_actorsObserved} entDrawn={_actorsDrawn} meshMissing={_triMeshesAbsent} drawsIssued={_drawsIssued} instances={_instsIssued} groups={_clusters.Count} " +
                $"cpu_us={cpuMed}m/{cpuP95}p95 gpu_us={gpuMed}m/{gpuP95}p95");
            _actorsObserved = _actorsDrawn = _triMeshesAbsent = _drawsIssued = _instsIssued = 0;
            _previousTraceBeat = instant;
        }
    }

    private static long MedianMicros(long[] specimens)
    {
        long[] duplicate = (long[])specimens.Clone();
        Array.Sort(duplicate);
        int nz = 0;
        foreach (var v in duplicate) if (v > 0) ++nz;
        return nz is 0 ? 0 : duplicate[duplicate.Length - 1 - (nz - 1) / 2];
    }

    private static long Percentile95Micros(long[] specimens)
    {
        long[] duplicate = (long[])specimens.Clone();
        Array.Sort(duplicate);
        int nz = 0;
        foreach (var v in duplicate) if (v > 0) ++nz;
        if (nz is 0) return 0;
        int index = duplicate.Length - 1 - (int)(nz * 0.05);
        return duplicate[index];
    }

    private void ImposeStashStrikeStraight(ActorShelfEntry listing, Matrix4x4 actorRealm)
    {
        for (int idx = 0; idx < listing.Batches.Length; ++idx)
        {
            var stashed = listing.Batches[idx];
            Matrix4x4 model = stashed.RestPose * actorRealm;
            if (!TryLocateStashedCluster(stashed, out InstCluster? cluster))
            {
                cluster = FetchOrBuildInstCluster(stashed.Key);
                listing.Batches[idx] = stashed with
                {
                    Group = cluster,
                    Ticket = cluster.Ticket,
                };
            }
            AffixInstToCluster(cluster!, model);
        }
    }

    private void AffixInstToCluster(
        ClusterTag tag,
        Matrix4x4 model)
    {
        var grp = FetchOrBuildInstCluster(tag);
        AffixInstToCluster(grp, model);
    }

    private InstCluster FetchOrBuildInstCluster(ClusterTag tag)
    {
        if (_clusters.TryGetValue(tag, out InstCluster? cluster))
        {
            cluster.PreviousConsumedCycle = _clusterCycle;
            return cluster;
        }

        if (_upcomingClusterEnrollment == long.MaxValue)
        {
            throw new InvalidOperationException(
                "Instance-group registration space was exhausted prior to a safe identity could be assigned");
        }

        cluster = BuildClusterFromTag(
            tag,
            enrollment: _upcomingClusterEnrollment++,
            cycle: _clusterCycle);
        _clusters.Add(tag, cluster);
        return cluster;
    }

    private void AffixInstToCluster(
        InstCluster grp,
        Matrix4x4 model)
    {
        grp.PreviousConsumedCycle = _clusterCycle;
        grp.Instances.Append(LatestInstance(model, opacity: 1.0f));
    }

    private void CalculateActorLampSet(
        in RasterizeInstContender actor)
    {
        _latestActorInside =
            InsideObjectReceivesTorches(actor.AncestorChamber);

        _latestActorLampSet = InstLampGroup.Disabled;
        var snap = _ptCapture;
        if (snap is null || snap.Count is 0) return;

        if (!_latestActorInside) return;

        Vector3 middle =
            (actor.Bounds.Minimum + actor.Bounds.Maximum) * 0.5f;
        float radius =
            (actor.Bounds.Maximum - actor.Bounds.Minimum).Length() * 0.5f;
        Array.Fill(_latestActorLampSetTemp, -1);
        LightKeeper.PickForObject(snap, middle, radius, _latestActorLampSetTemp);
        _latestActorLampSet = InstLampGroup.From(_latestActorLampSetTemp);
    }

    /// <summary>
    /// One instance of the actor being classified right now: the model matrix and opacity the caller
    /// supplies, and the rest taken from the actor state the classify pass has just computed.
    /// </summary>
    private InstFacts LatestInstance(in Matrix4x4 model, float opacity) => new(
        model,
        _upcomingInstSubmissionOrdering++,
        _latestActorSocket,
        _latestActorLampSet,
        _latestActorInside ? 1u : 0u,
        _latestActorStructureSpecifics ? 1u : 0u,
        opacity,
        _latestActorPickIllumination);

    private SettledBitmap LocateTexture(
        in RasterizeInstContender actor,
        TriMeshRef triMeshRef,
        ThingRasterizeLot lot,
        SwatchCompoundPersona swatchPersona,
        out bool compoundQueued)
    {
        return LocateTexture(
            actor.LocalEntityId,
            actor.PaletteOverride,
            triMeshRef,
            lot,
            swatchPersona,
            out compoundQueued);
    }

    private SettledBitmap LocateTexture(
        RealmActor actor,
        TriMeshRef triMeshRef,
        ThingRasterizeLot lot,
        SwatchCompoundPersona swatchPersona,
        out bool compoundQueued)
    {
        return LocateTexture(
            actor.Id,
            actor.SwatchOverride,
            triMeshRef,
            lot,
            swatchPersona,
            out compoundQueued);
    }

    private SettledBitmap LocateTexture(
        uint ownActorIdent,
        SwatchOverride? swatchOverride,
        TriMeshRef triMeshRef,
        ThingRasterizeLot lot,
        SwatchCompoundPersona swatchPersona,
        out bool compoundQueued)
    {
        compoundQueued = false;
        uint canvasIdent = lot.Key.SurfaceId;
        if (canvasIdent is 0 or 0xFFFFFFFF)
            return default;

        uint overrideOrigBmp = 0;
        bool hasOrigBmpOverride = triMeshRef.CanvasOverrides is not null
            && triMeshRef.CanvasOverrides.TryGetValue(canvasIdent, out overrideOrigBmp)
            && overrideOrigBmp is not 0;
        uint? origBmpOverride = hasOrigBmpOverride ? overrideOrigBmp : (uint?)null;

        bool srcIsSwatchIndexed = swatchOverride is not null
            && _textures.IsSwatchIndexed(canvasIdent, origBmpOverride);
        var resolution = BatchTextureResolutionRule.Select(
            hasOrigBmpOverride,
            swatchOverride is not null,
            srcIsSwatchIndexed);

        switch (resolution)
        {
            case BatchTextureResolutionKind.PaletteComposite:
                {
                    var texture =
                        _textures.FetchOrPushWithSwatchOverrideBindless(
                            ownActorIdent,
                            canvasIdent,
                            origBmpOverride,
                            swatchOverride!,
                            swatchPersona);
                    compoundQueued = !texture.IsSettled;
                    return new SettledBitmap(texture.SettleSocket(lot.HasWrappingUVs), texture.Layer);
                }

            case BatchTextureResolutionKind.OriginalTextureOverride:
                {
                    var texture =
                        _textures.FetchOrPushWithOrigTextureOverrideBindless(
                            ownActorIdent,
                            canvasIdent,
                            overrideOrigBmp);
                    compoundQueued = !texture.IsSettled;
                    return new SettledBitmap(texture.SettleSocket(lot.HasWrappingUVs), texture.Layer);
                }

            case BatchTextureResolutionKind.SharedAtlas:
                return new SettledBitmap(
                    lot.TextureSlot,
                    checked((uint)lot.TextureIndex));

            default:
                throw new ArgumentOutOfRangeException(nameof(resolution));
        }
    }

    private static void EmitMatrix(float[] buffer, int shift, in Matrix4x4 m)
    {
        buffer[shift + 0] = m.M11; buffer[shift + 1] = m.M12; buffer[shift + 2] = m.M13; buffer[shift + 3] = m.M14;
        buffer[shift + 4] = m.M21; buffer[shift + 5] = m.M22; buffer[shift + 6] = m.M23; buffer[shift + 7] = m.M24;
        buffer[shift + 8] = m.M31; buffer[shift + 9] = m.M32; buffer[shift + 10] = m.M33; buffer[shift + 11] = m.M34;
        buffer[shift + 12] = m.M41; buffer[shift + 13] = m.M42; buffer[shift + 14] = m.M43; buffer[shift + 15] = m.M44;
    }

    private static bool ActorFitsSet(RealmActor actor, ActorSet set) => true;

    private static bool IsShellScopedSet(ActorSet set) => false;

    private void ConcludeTeardown() => _dynamicCycleBegun = false;
}
