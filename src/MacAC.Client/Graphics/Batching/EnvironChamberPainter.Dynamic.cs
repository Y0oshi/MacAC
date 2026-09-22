using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class EnvironChamberPainter
{
    internal int DynamicBufSetTally => 0;

    // Bumps once per visibility-snapshot rebuild (tests + diagnostics)
    internal int CaptureGen { get; private set; }

    public bool IsDestroyed { get; private set; }

    public PreviousCycleStats Stats => _previousCycleStats;

    public void BeginFrame(int cycleSocket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cycleSocket);
        _dynamicCycleSocket = cycleSocket;
        _dynamicCycleBegun = true;
        if (++_lampCycleGen is 0)
        {
            _chamberLampSetStash.Clear();
            _lampCycleGen = 1;
        }
    }

    public void AssignPtCapture(
        System.Collections.Generic.IReadOnlyList<MacAC.Mechanics.Illumination.LightEmitter>? capture)
        => _ptCapture = capture;

    public static ulong FetchEnvironChamberGeomIdent(uint surroundingsIdent, ushort chamberStructure, List<ushort> canvases)
    {
        return EnvCellLandblockBuildAssembler.CalculateGeoIdent(
                surroundingsIdent,
                chamberStructure,
                canvases);
    }

    public void SealLb(EnvironChamberLandblockAssemble assemble)
    {
        var bulletin = ReadyBulletin(assemble);
        while (!ProgressPrepOne(bulletin))
        {
        }
        SealBulletin(bulletin);
    }

    /// <summary>Removes a landblock from the renderer. Future PrepareRenderBatches will exclude it.</summary>
    public void RemoveLandblock(uint lbIdent)
    {
        _lbs.TryRemove(lbIdent, out _);
        uint chamberStem = lbIdent & 0xFFFF0000u;
        _chamberLampDeletionTemp.Clear();
        foreach (uint chamberIdent in _chamberLampSetStash.Keys)
        {
            if ((chamberIdent & 0xFFFF0000u) == chamberStem)
                _chamberLampDeletionTemp.Add(chamberIdent);
        }
        foreach (uint chamberIdent in _chamberLampDeletionTemp)
            _chamberLampSetStash.Remove(chamberIdent);
        NeedsReady = true;
    }

    public void ReadyRasterizeLots(
        Matrix4x4 lensProj,
        Vector3 camLocus,
        HashSet<uint>? sift = null,
        int? middleLbX = null,
        int? middleLbY = null,
        int? rasterizeRadius = null)
    {
        _previousLensProj = lensProj;

        if (!_initialized || camLocus.Z > 4000) return;

        long triMeshVer = _meshManager is null ? 0L : _meshManager.RasterizeBlobReadinessVer;

        if (sift is { Count: 0 })
        {
            if (_hasReadiedCapture && !_readiedSiftWasNull && _readiedSift.Count is 0)
                return;
            lock (_rasterizeMutex)
            {
                _poolIndex = 0;
                _activeSnapshot = new EnvCellVisibilityCapture();
                _transparentCellIds.Clear();
                NeedsReady = false;
            }
            CaptureReadiedFeeds(lensProj, camLocus, sift, middleLbX, middleLbY, rasterizeRadius, triMeshVer);
            return;
        }

        if (_hasReadiedCapture
            && !NeedsReady
            && triMeshVer == _readiedTriMeshVer
            && _readiedTrim == (middleLbX, middleLbY, rasterizeRadius)
            && SiftUnchanged(sift)
            && CamApproximatelyEqual(
                lensProj, camLocus,
                _readiedLensProj, _readiedCamLocus))

            return;

        lock (_rasterizeMutex) { _poolIndex = 0; }

        var lbs = _readyLbs;
        lbs.Clear();
        foreach (var landblock in _lbs.Values)
        {
            if (middleLbX.HasValue && middleLbY.HasValue && rasterizeRadius.HasValue && (Math.Abs(landblock.GridX - middleLbX.Value) > rasterizeRadius.Value ||
                    Math.Abs(landblock.GridY - middleLbY.Value) > rasterizeRadius.Value))

                continue;

            if (landblock.GpuPrimed && landblock.Instances.Count > 0)
                lbs.Add(landblock);
        }
        if (lbs.Count is 0) return;

        foreach (ReadyTemp temp in _readyTemp.Values)
            temp.Reset();

        ParallelOptions parallelKnobs = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        Parallel.ForEach(lbs, parallelKnobs, lb =>
        {
            lock (lb.Lock)
            {
                FrustumTestOutcome testOutcome = _frustum.AssessBbox(lb.SumEnvironChamberLimits);
                if (testOutcome == FrustumTestOutcome.Outside) return;

                var temp = _readyTemp.Value!;

                // Wholly inside the frustum: take every instance without testing them one by one,
                // so only the caller's own filter still applies.
                if (testOutcome == FrustumTestOutcome.Inside)
                {
                    TakeInstances(temp, lb, accepted: sift);
                    return;
                }

                // Straddling it: test the rooms, then take the instances of the ones in view. The
                // room set already has the caller's filter applied, so it is the only test left.
                var shownChambers = temp.VisibleCells;
                shownChambers.Clear();
                foreach (var kvp in lb.EnvironChamberLimits)
                {
                    uint chamberIdent = kvp.Key;
                    if (sift is not null && !sift.Contains(chamberIdent)) continue;
                    if (_frustum.Intersects(kvp.Value))
                        shownChambers.Add(chamberIdent);
                }

                if (shownChambers.Count > 0)
                    TakeInstances(temp, lb, accepted: shownChambers);
            }
        });

        var newBatchedByChamber = new Dictionary<uint, Dictionary<ulong, List<InstanceData>>>();
        foreach (ReadyTemp temp in _readyTemp.Values)
        {
            foreach (var chamberKvp in temp.BatchedByChamber)
            {
                if (!newBatchedByChamber.TryGetValue(chamberKvp.Key, out var gfxDict))
                {
                    gfxDict = [];
                    newBatchedByChamber[chamberKvp.Key] = gfxDict;
                }
                foreach (var gfxKvp in chamberKvp.Value)
                {
                    if (!gfxDict.TryGetValue(gfxKvp.Key, out var roster))
                    {
                        roster = GetPooledList();
                        gfxDict[gfxKvp.Key] = roster;
                    }
                    roster.AddRange(gfxKvp.Value);
                }
            }
        }

        lock (_rasterizeMutex)
        {
            _activeSnapshot = new EnvCellVisibilityCapture
            {
                BatchedByCell = newBatchedByChamber,
                VisibleLandblocks = lbs,
                PostReadyReservoirOrdinal = _poolIndex,
            };
            ReassembleSeeThruChamberOrdinal(newBatchedByChamber);

            _poolIndex = 0;
            NeedsReady = false;
        }
        CaptureReadiedFeeds(lensProj, camLocus, sift, middleLbX, middleLbY, rasterizeRadius, triMeshVer);
    }

    public void Render(BatchRenderPass rasterizePass)
    {
        PaintCore(rasterizePass, null, null, EnvironChamberSeeThruCourse.All, specificsCanvasEngaged: false);
    }

    public void Render(BatchRenderPass rasterizePass, HashSet<uint>? sift)
    {
        PaintCore(rasterizePass, sift, null, EnvironChamberSeeThruCourse.All, specificsCanvasEngaged: false);
    }

    public void RasterizeSeeThruSequenced(IReadOnlyList<uint> sequencedChamberIdents)
    {
        ArgumentNullException.ThrowIfNull(sequencedChamberIdents);
        PaintCore(
            BatchRenderPass.Transparent,
            null,
            sequencedChamberIdents,
            EnvironChamberSeeThruCourse.All,
            specificsCanvasEngaged: TransparentDetailEnabled);
    }

    public bool ChamberHasSeeThru(uint chamberIdent)
        => _transparentCellIds.Contains(chamberIdent);

    internal EnvironChamberLandblockBulletin ReadyBulletin(
        EnvironChamberLandblockAssemble assemble)
    {
        ArgumentNullException.ThrowIfNull(assemble);
        return new EnvironChamberLandblockBulletin(_bulletinHolder, assemble);
    }

    internal bool ProgressPrepOne(
        EnvironChamberLandblockBulletin bulletin)
    {
        VetBulletin(bulletin);
        if (bulletin.PrepSealed)
            return true;

        if (bulletin.ShellCur < bulletin.Build.Shells.Length)
        {
            AppendShell(
                bulletin.Replacement,
                bulletin.Build.Shells[bulletin.ShellCur]);
            var limits =
                bulletin.Build.Shells[bulletin.ShellCur].WorldBounds;
            bulletin.SumLimits =
                bulletin.ShellCur is 0
                    ? limits
                    : BatchBoundingBox.Union(
                        bulletin.SumLimits,
                        limits);
            bulletin.ShellCur++;
            return false;
        }

        bulletin.Replacement.SumEnvironChamberLimits =
            bulletin.Replacement.EnvironChamberLimits.Count is 0
                ? new BatchBoundingBox(Vector3.Zero, Vector3.Zero)
                : bulletin.SumLimits;
        bulletin.Replacement.InstsPrimed = true;
        bulletin.Replacement.TriMeshBlobPrimed = true;
        bulletin.Replacement.GpuPrimed = true;
        bulletin.PrepSealed = true;
        return true;
    }

    internal void SealBulletin(
        EnvironChamberLandblockBulletin bulletin)
    {
        VetBulletin(bulletin);
        if (!bulletin.PrepSealed)
            throw new InvalidOperationException(
                "EnvCell landblock publication can't commit prior to preparation");
        if (bulletin.BulletinSealed)
            return;

        _lbs[bulletin.Build.LbIdent] = bulletin.Replacement;
        NeedsReady = true;
        bulletin.BulletinSealed = true;
    }

    internal static EnvironChamberLandblock BuildSealedCapture(EnvironChamberLandblockAssemble assemble)
    {
        EnvironChamberLandblock substitute = new EnvironChamberLandblock
        {
            GridX = (int)((assemble.LbIdent >> 24) & 0xFFu),
            GridY = (int)((assemble.LbIdent >> 16) & 0xFFu),
        };

        foreach (var shell in assemble.Shells)
            AppendShell(substitute, shell);

        var sum = new BatchBoundingBox(new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var limits in substitute.EnvironChamberLimits.Values)
            sum = BatchBoundingBox.Union(sum, limits);
        substitute.SumEnvironChamberLimits = substitute.EnvironChamberLimits.Count is 0
            ? new BatchBoundingBox(Vector3.Zero, Vector3.Zero)
            : sum;

        substitute.InstsPrimed = true;
        substitute.TriMeshBlobPrimed = true;
        substitute.GpuPrimed = true;
        return substitute;
    }

    internal static bool CamApproximatelyEqual(
        in Matrix4x4 vpA, Vector3 eyePtA,
        in Matrix4x4 vpB, Vector3 eyePtB)
    {
        const float EyePtEpsilonSq = 1e-3f * 1e-3f;
        return Vector3.DistanceSquared(eyePtA, eyePtB) > EyePtEpsilonSq
            ? false
            : Shut(vpA.M11, vpB.M11) && Shut(vpA.M12, vpB.M12) && Shut(vpA.M13, vpB.M13) && Shut(vpA.M14, vpB.M14)
            && Shut(vpA.M21, vpB.M21) && Shut(vpA.M22, vpB.M22) && Shut(vpA.M23, vpB.M23) && Shut(vpA.M24, vpB.M24)
            && Shut(vpA.M31, vpB.M31) && Shut(vpA.M32, vpB.M32) && Shut(vpA.M33, vpB.M33) && Shut(vpA.M34, vpB.M34);

        static bool Shut(float x, float y)
        {
            const float Rel = 1e-5f;
            return MathF.Abs(x - y) <= Rel * MathF.Max(1f, MathF.Max(MathF.Abs(x), MathF.Abs(y)));
        }
    }

    internal void PaintSeeThruSequenced(
        IReadOnlyList<uint> sequencedChamberIdents,
        EnvironChamberSeeThruCourse course,
        bool specificsCanvasEngaged)
    {
        ArgumentNullException.ThrowIfNull(sequencedChamberIdents);
        PaintCore(
            BatchRenderPass.Transparent,
            null,
            sequencedChamberIdents,
            course,
            specificsCanvasEngaged);
    }

    internal EnvironChamberSeeThruCourse FetchSeeThruCourses(
        uint chamberIdent,
        bool specificsCanvasEngaged)
    {
        lock (_rasterizeMutex)
        {
            if (!_activeSnapshot.BatchedByCell.TryGetValue(chamberIdent, out var clusters))
                return EnvironChamberSeeThruCourse.None;

            var courses = EnvironChamberSeeThruCourse.None;
            foreach ((ulong gfxObjRefIdent, List<InstanceData> xforms) in clusters)
            {
                if (xforms.Count is 0)
                    continue;
                var rasterizeBlob = _meshManager.TryFetchRasterizeBlob(gfxObjRefIdent);
                if (rasterizeBlob is null || rasterizeBlob.IsSetup)
                    continue;
                for (int lotOrdinal = 0; lotOrdinal < rasterizeBlob.Batches.Count; ++lotOrdinal)
                {
                    var lot = rasterizeBlob.Batches[lotOrdinal];
                    if (lot.IsTransparent)
                        courses |= CourseSeeThruLot(lot, specificsCanvasEngaged);
                }
            }
            return courses;
        }
    }

    internal static EnvironChamberSeeThruCourse CourseSeeThruLot(
        ThingRasterizeLot lot,
        bool specificsCanvasEngaged)
    {
        var decision = CanonAlphaMeshRouter.Course(
            currentlyDrawingHeavens: false,
            delayBitmask: CanonAlphaMeshRouter.DefaultDelayBitmask,
            specificsCanvasEngaged: specificsCanvasEngaged,
            multiPassAlpha: false,
            subsetBitmask: lot.RetailSurfaceMask,
            matlHasAlpha: false);
        return decision.Action switch
        {
            CanonAlphaMeshAction.Immediate => EnvironChamberSeeThruCourse.Immediate,
            CanonAlphaMeshAction.Append => decision.List == CanonAlphaList.Clip
                ? EnvironChamberSeeThruCourse.Clip
                : EnvironChamberSeeThruCourse.Alpha,
            CanonAlphaMeshAction.AppendClipAndImmediate =>
                EnvironChamberSeeThruCourse.Clip | EnvironChamberSeeThruCourse.Immediate,
            _ => throw new ArgumentOutOfRangeException(nameof(decision.Action)),
        };
    }

    EnvironChamberLandblockBulletin IEnvCellLandblockHerald.StageBulletin(
        EnvironChamberLandblockAssemble assemble) =>
        ReadyBulletin(assemble);

    bool IEnvCellLandblockHerald.StepPrepOne(
        EnvironChamberLandblockBulletin bulletin) =>
        ProgressPrepOne(bulletin);

    void IEnvCellLandblockHerald.LockBulletin(
        EnvironChamberLandblockBulletin bulletin) =>
        SealBulletin(bulletin);

    private static void AppendShell(
        EnvironChamberLandblock substitute,
        EnvironChamberShellStance shell)
    {
        substitute.Instances.Add(new EnvironChamberSceneryInst
        {
            ObjectId = shell.GeometryId,
            InstIdent = shell.CellId,
            IsStructure = true,
            IsListingChamber = false,
            RealmLocus = shell.WorldPosition,
            OwnLocus = Vector3.Zero,
            Spin = shell.Rotation,
            Scale = Vector3.One,
            Transform = shell.Transform,
            OwnBoundingBbox = shell.LocalBounds,
            BoundingBox = shell.WorldBounds,
        });
        substitute.EnvironChamberLimits[shell.CellId] = shell.WorldBounds;

        if (!substitute.StructurePieceClusters.TryGetValue(
                shell.GeometryId,
                out var insts))
        {
            insts = [];
            substitute.StructurePieceClusters[shell.GeometryId] = insts;
        }
        insts.Add(new InstanceData
        {
            Transform = shell.Transform,
            CellId = shell.CellId,
            Flags = 0,
        });
    }

    private void VetBulletin(
        EnvironChamberLandblockBulletin publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!ReferenceEquals(publication.Owner, _bulletinHolder))
        {
            throw new ArgumentException(
                "The EnvCell publication receipt belongs to another renderer",
                nameof(publication));
        }
    }

    private bool SiftUnchanged(HashSet<uint>? sift)
    {
        if (sift is null) return _readiedSiftWasNull;
        return _readiedSiftWasNull ? false : sift.Count == _readiedSift.Count && _readiedSift.SetEquals(sift);
    }

    private void ReassembleSeeThruChamberOrdinal(
        Dictionary<uint, Dictionary<ulong, List<InstanceData>>> batchedByChamber)
    {
        _transparentCellIds.Clear();
        foreach ((uint chamberIdent, Dictionary<ulong, List<InstanceData>> clusters) in batchedByChamber)
        {
            foreach ((ulong gfxObjRefIdent, List<InstanceData> xforms) in clusters)
            {
                if (xforms.Count is 0)
                    continue;
                var rasterizeBlob = _meshManager.TryFetchRasterizeBlob(gfxObjRefIdent);
                if (rasterizeBlob is null)
                    continue;
                for (int lotOrdinal = 0; lotOrdinal < rasterizeBlob.Batches.Count; ++lotOrdinal)
                {
                    if (!rasterizeBlob.Batches[lotOrdinal].IsTransparent)
                        continue;
                    _transparentCellIds.Add(chamberIdent);
                    goto NextCell;
                }
            }
        NextCell:;
        }
    }

    /// <summary>
    /// Takes a landblock's instances into the thread's scratch, keeping those whose room is in
    /// <paramref name="accepted"/>; a null set accepts every room.
    ///
    /// Both kinds of scenery walk the same way; the only thing the two frustum outcomes change is
    /// which rooms are allowed.
    /// </summary>
    private static void TakeInstances(
        ReadyTemp temp,
        EnvironChamberLandblock lb,
        HashSet<uint>? accepted)
    {
        TakeFrom(temp, lb.StructurePieceClusters, accepted);
        TakeFrom(temp, lb.StaticPieceClusters, accepted);
    }

    private static void TakeFrom(
        ReadyTemp temp,
        Dictionary<ulong, List<InstanceData>> clusters,
        HashSet<uint>? accepted)
    {
        foreach ((ulong gfxObjRefIdent, List<InstanceData> insts) in clusters)
        {
            foreach (InstanceData instBlob in insts)
            {
                if (accepted is not null && !accepted.Contains(instBlob.CellId))
                    continue;
                AppendToClusters(temp, instBlob.CellId, gfxObjRefIdent, instBlob);
            }
        }
    }

    private static void AppendToClusters(
        ReadyTemp temp,
        uint chamberIdent,
        ulong gfxObjRefIdent,
        InstanceData blob)
    {
        var batchedByChamber = temp.BatchedByChamber;
        if (!batchedByChamber.TryGetValue(chamberIdent, out var gfxDict))
        {
            gfxDict = temp.RentGfxDictionary();
            batchedByChamber[chamberIdent] = gfxDict;
        }
        if (!gfxDict.TryGetValue(gfxObjRefIdent, out var roster))
        {
            roster = temp.RentRoster();
            batchedByChamber[chamberIdent][gfxObjRefIdent] = roster;
        }
        roster.Add(blob);
    }

    private void PaintCore(
        BatchRenderPass rasterizePass,
        HashSet<uint>? sift,
        IReadOnlyList<uint>? sequencedChamberIdents,
        EnvironChamberSeeThruCourse seeThruCourse,
        bool specificsCanvasEngaged)
    {
        if (!_initialized) return;

        lock (_rasterizeMutex)
        {
            EnvCellVisibilityCapture capture = _activeSnapshot;
            _poolIndex = capture.PostReadyReservoirOrdinal;

            var allInsts = _rasterizeInsts;
            var paintCalls =
                _rasterizePaintCalls;
            allInsts.Clear();
            paintCalls.Clear();
            _paintCallSpans.Clear();

            if (sequencedChamberIdents is not null)
            {
                for (int chamberOrdinal = 0; chamberOrdinal < sequencedChamberIdents.Count; ++chamberOrdinal)
                {
                    uint chamberIdent = sequencedChamberIdents[chamberOrdinal];
                    if (!capture.BatchedByCell.TryGetValue(chamberIdent, out var chamberClusters))
                        continue;

                    int leadPaintCall = paintCalls.Count;
                    foreach ((ulong gfxObjRefIdent, List<InstanceData> xforms) in chamberClusters)
                    {
                        if (xforms.Count is 0)
                            continue;
                        var rasterizeBlob = _meshManager.TryFetchRasterizeBlob(gfxObjRefIdent);
                        if (rasterizeBlob is null || rasterizeBlob.IsSetup)
                            continue;
                        paintCalls.Add((renderData: rasterizeBlob, gfxObjId: gfxObjRefIdent, xforms.Count, allInsts.Count));
                        allInsts.AddRange(xforms);
                    }
                    int paintCallTally = paintCalls.Count - leadPaintCall;
                    if (paintCallTally > 0)
                        _paintCallSpans.Add(new PaintCallSpan(leadPaintCall, paintCallTally));
                }
            }
            else if (sift is null)
            {
                ReassembleUnfilteredClusters(capture);
                // Scenery shared across the whole world, grouped once rather than per landblock.
                foreach (var gfxObjRefIdent in _engagedCaptureGlobalGfxObjRefIdents)
                {
                    if (_engagedCaptureGlobalClusters.TryGetValue(gfxObjRefIdent, out var xforms))
                    {
                        ThingRasterizeBlob? rasterizeBlob = _meshManager.TryFetchRasterizeBlob(gfxObjRefIdent);
                        if (rasterizeBlob is not null && !rasterizeBlob.IsSetup)
                        {
                            paintCalls.Add((renderData: rasterizeBlob, gfxObjId: gfxObjRefIdent, xforms.Count, allInsts.Count));
                            allInsts.AddRange(xforms);
                        }
                    }
                }
            }
            else
            {
                var filteredClusters = _filteredClusters;
                var possessedRosters = _filteredPossessedRosters;
                filteredClusters.Clear();
                possessedRosters.Clear();

                foreach (var chamberIdent in sift)
                {
                    if (!capture.BatchedByCell.TryGetValue(chamberIdent, out var gfxDict)) continue;
                    foreach (var (gfxObjRefIdent, xforms) in gfxDict)
                    {
                        if (xforms.Count is 0) continue;
                        if (!filteredClusters.TryGetValue(gfxObjRefIdent, out var roster))
                        {
                            roster = xforms; // Optimization: just use the first list
                            filteredClusters[gfxObjRefIdent] = roster;
                        }
                        else
                        {
                            if (roster == xforms) continue;

                            if (!possessedRosters.Contains(roster))
                            {
                                List<InstanceData> newRoster = GetPooledList();
                                newRoster.AddRange(roster);
                                roster = newRoster;
                                filteredClusters[gfxObjRefIdent] = roster;
                                possessedRosters.Add(roster);
                            }
                            roster.AddRange(xforms);
                        }
                    }
                }

                foreach (var (gfxObjRefIdent, xforms) in filteredClusters)
                {
                    ThingRasterizeBlob? rasterizeBlob = _meshManager.TryFetchRasterizeBlob(gfxObjRefIdent);
                    if (rasterizeBlob is not null && !rasterizeBlob.IsSetup)
                    {
                        paintCalls.Add((renderData: rasterizeBlob, gfxObjId: gfxObjRefIdent, xforms.Count, allInsts.Count));
                        allInsts.AddRange(xforms);
                    }
                }
            }

            if (allInsts.Count > 0)
            {
                if (_paintCallSpans.Count is 0 && paintCalls.Count > 0)
                    _paintCallSpans.Add(new PaintCallSpan(0, paintCalls.Count));
                PaintModernMDIInternal(
                    paintCalls,
                    allInsts,
                    _paintCallSpans,
                    rasterizePass,
                    seeThruCourse,
                    specificsCanvasEngaged);
            }

            // No selection or hover highlighting here: that belonged to the editor, not the client.

            _previousCycleStats.ChambersRendered = sequencedChamberIdents?.Count
                ?? sift?.Count
                ?? capture.BatchedByCell.Count;
            _previousCycleStats.TrianglesDrawn = 0;
            foreach (var dc in paintCalls)
                _previousCycleStats.TrianglesDrawn += (dc.renderData.Batches.Count > 0
                    ? dc.renderData.Batches[0].OrdinalTally / 3
                    : 0) * dc.count;
        }
    }

    private static bool FitsSeeThruCourse(
        ThingRasterizeLot lot,
        EnvironChamberSeeThruCourse course,
        bool specificsCanvasEngaged) =>
        (CourseSeeThruLot(lot, specificsCanvasEngaged) & course) != 0;

    private int[] FetchChamberLampSet(uint chamberIdent)
    {
        if (!_chamberLampSetStash.TryGetValue(chamberIdent, out ShelvedCellLightSet? stashed))
        {
            stashed = new ShelvedCellLightSet();
            _chamberLampSetStash.Add(chamberIdent, stashed);
        }
        if (stashed.CycleGen == _lampCycleGen)
            return stashed.Indices;

        int[] set = stashed.Indices;
        System.Array.Fill(set, -1);

        var snap = _ptCapture;
        if (snap is { Count: > 0 })
            MacAC.Mechanics.Illumination.LightKeeper.PickForChamber(snap, set);
        stashed.CycleGen = _lampCycleGen;
        return set;
    }

    private void ReassembleUnfilteredClusters(EnvCellVisibilityCapture capture)
    {
        foreach (List<InstanceData> insts in _engagedCaptureGlobalClusters.Values)
            insts.Clear();
        _engagedCaptureGlobalGfxObjRefIdents.Clear();

        foreach (Dictionary<ulong, List<InstanceData>> chamberClusters in capture.BatchedByCell.Values)
        {
            foreach ((ulong gfxObjRefIdent, List<InstanceData> xforms) in chamberClusters)
            {
                if (!_engagedCaptureGlobalClusters.TryGetValue(gfxObjRefIdent, out List<InstanceData>? combined))
                {
                    combined = new List<InstanceData>(xforms.Count);
                    _engagedCaptureGlobalClusters.Add(gfxObjRefIdent, combined);
                }
                if (combined.Count is 0)
                    _engagedCaptureGlobalGfxObjRefIdents.Add(gfxObjRefIdent);
                combined.AddRange(xforms);
            }
        }
    }

    private void PaintModernMDIInternal(
        List<(ThingRasterizeBlob renderData, ulong gfxObjId, int count, int offset)> paintCalls,
        List<InstanceData> allInsts,
        IReadOnlyList<PaintCallSpan> paintCallSpans,
        BatchRenderPass rasterizePass,
        EnvironChamberSeeThruCourse seeThruCourse,
        bool specificsCanvasEngaged)
    {
        if (paintCalls.Count is 0 || allInsts.Count is 0) return;

        int passIndex = (int)rasterizePass;
        if (passIndex is < 0 or > 2) return;

        if (_meshManager.GlobalBuf is not { HasStores: true })
            return;

        int sumDraws = 0;
        for (int spanOrdinal = 0; spanOrdinal < paintCallSpans.Count; ++spanOrdinal)
        {
            var span = paintCallSpans[spanOrdinal];
            int spanFinish = Math.Min(span.First + span.Count, paintCalls.Count);
            for (int callOrdinal = span.First; callOrdinal < spanFinish; ++callOrdinal)
            {
                var call = paintCalls[callOrdinal];
                foreach (var lot in call.renderData.Batches)
                {
                    if (!LotBelongsToPass(lot, rasterizePass))
                        continue;

                    if (rasterizePass == BatchRenderPass.Transparent
                        && seeThruCourse != EnvironChamberSeeThruCourse.All
                        && !FitsSeeThruCourse(lot, seeThruCourse, specificsCanvasEngaged))

                        continue;

                    ++sumDraws;
                }
            }
        }

        if (sumDraws is 0) return;
        int uniqueInstTally = allInsts.Count;

        if (!_dynamicCycleBegun)
            throw new InvalidOperationException("BeginFrame has to be called prior to drawing EnvCells");

        // Grow the scratch arrays to fit this frame, doubling so it settles after a few frames.
        if (_commands.Length < sumDraws)
            Array.Resize(ref _commands, Math.Max(_commands.Length * 2, sumDraws));
        if (_modernBatches.Length < sumDraws)
            Array.Resize(ref _modernBatches, Math.Max(_modernBatches.Length * 2, sumDraws));

        _mdiDrawRanges.Clear();
        int cmdOrdinal = 0;
        for (int spanOrdinal = 0; spanOrdinal < paintCallSpans.Count; ++spanOrdinal)
        {
            _engagedPruneClusters.Clear();
            for (int clusterOrdinal = 0; clusterOrdinal < _lotsByPruneCluster.Length; ++clusterOrdinal)
                _lotsByPruneCluster[clusterOrdinal].Clear();

            var span = paintCallSpans[spanOrdinal];
            int spanFinish = Math.Min(span.First + span.Count, paintCalls.Count);
            for (int callOrdinal = span.First; callOrdinal < spanFinish; ++callOrdinal)
            {
                var call = paintCalls[callOrdinal];
                foreach (var lot in call.renderData.Batches)
                {
                    if (!LotBelongsToPass(lot, rasterizePass))
                        continue;

                    // Its texture upload has not landed yet, so there is nothing to draw it with.
                    // Skipping the call costs a frame of the part being absent instead of a frame
                    // of it sampling an unbound descriptor slot.
                    if (!lot.TextureSlot.IsAssigned)
                        continue;

                    if (rasterizePass == BatchRenderPass.Transparent
                        && seeThruCourse != EnvironChamberSeeThruCourse.All
                        && !FitsSeeThruCourse(lot, seeThruCourse, specificsCanvasEngaged))

                        continue;

                    int clusterOrdinal = LocateLotClusterOrdinal(
                        lot,
                        rasterizePass);
                    var cluster =
                        _lotsByPruneCluster[clusterOrdinal];
                    if (cluster.Count is 0)
                        _engagedPruneClusters.Add(clusterOrdinal);
                    cluster.Add((batch: lot, call.count, call.offset));
                }
            }

            for (int engagedOrdinal = 0; engagedOrdinal < _engagedPruneClusters.Count; ++engagedOrdinal)
            {
                int clusterOrdinal = _engagedPruneClusters[engagedOrdinal];
                var cluster =
                    _lotsByPruneCluster[clusterOrdinal];
                foreach (var gear in cluster)
                {
                    _modernBatches[cmdOrdinal] = new ModernLotBlob
                    {
                        TextureChartOrdinal = gear.batch.TextureSlot.Index,
                        SurfaceOpacity = gear.batch.SurfaceOpacity,
                        TextureIndex = (uint)gear.batch.TextureIndex,
                        Flags = 1u,
                    };

                    _commands[cmdOrdinal] = new DrawElementsIndirectDirective
                    {
                        Count = (uint)gear.batch.OrdinalTally,
                        InstTally = (uint)gear.instanceCount,
                        LeadOrdinal = gear.batch.LeadIdx,
                        BaseVert = (int)gear.batch.BaseVertex,
                        BaseInst = (uint)gear.instanceOffset,
                    };
                    AffixMdiPaintSpan(
                        _mdiDrawRanges,
                        clusterOrdinal,
                        cmdOrdinal,
                        1,
                        gear.batch.MaterialState);
                    ++cmdOrdinal;
                }
            }
        }

        SubmitRhi(allInsts, rasterizePass, sumDraws, uniqueInstTally);
    }
}
