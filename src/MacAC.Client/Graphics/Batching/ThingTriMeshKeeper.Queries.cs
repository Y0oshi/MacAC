using MacAC.Assets;
using MacAC.Client.Graphics.Tenancy;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{
    public bool IsDisposed { get; private set; }

    public (int Arrays, long Bytes) ProduceMipmaps()
    {
        int generatedArrs = 0;
        long generatedOctets = 0;
        foreach (TextureAtlasKeeper tileset in _staleTilesets)
        {
            long octets = tileset.TextureArr.HandleStaleUpdates();
            if (octets > 0)
            {
                ++generatedArrs;
                generatedOctets = checked(generatedOctets + octets);
            }
        }
        _staleTilesets.Clear();
        return (generatedArrs, generatedOctets);
    }

    public (int PendingUpdates, int ArraysWithPending, int TotalArrays) FetchQueuedTextureRefreshStats()
    {
        int queued = 0, arrsWith = 0, sum = 0;
        foreach (var tilesetRoster in _globalTilesets.Values)
        {
            foreach (var tileset in tilesetRoster)
            {
                ++sum;
                int p = tileset.TextureArr.QueuedRefreshTally;
                if (p > 0) { ++arrsWith; queued += p; }
            }
        }
        return (queued, arrsWith, sum);
    }

    public ThingRasterizeBlob? FetchRasterizeBlob(ulong ident)
    {
        if (!_objectReleases.ContainsKey(ident)
            && _renderData.TryGetValue(ident, out var blob))
        {
            IncrementRefCount(ident);

            return blob;
        }
        return null;
    }

    public bool HasRasterizeBlob(ulong ident)
    {
        return !_objectReleases.ContainsKey(ident)
        && _renderData.ContainsKey(ident);
    }

    internal int LinedTriMeshTally => _linedTriMeshBlob.ClaimTally;

    internal long LinedTriMeshOctets => _linedTriMeshBlob.ClaimedOctets;

    internal bool LoadingAtHiWater => _linedTriMeshBlob.IsAtHiWater;

    public ThingRasterizeBlob? TryFetchRasterizeBlob(ulong ident)
    {
        return !_objectReleases.ContainsKey(ident)
            && _renderData.TryGetValue(ident, out var blob)
                ? blob
                : null;
    }

    public void IncrementRefCount(ulong ident)
    {
        lock (_queuedReqs)
        {
            _ownership.Obtain(ident);
            lock (_lruRoster)
            {
                _lruRoster.Remove(ident);
            }
        }
    }

    public void EvictAllUnused()
    {
        ProgressQueuedPushRollbacks(Math.Max(1, _pushUndoFifo.Count));
        ProgressQueuedObjectReleases(
            Math.Max(1, _objectFreeFifo.Count),
            long.MaxValue);

        int contenderAllowance;
        lock (_lruRoster)
            contenderAllowance = _lruRoster.Count;

        for (int attempted = 0; attempted < contenderAllowance; ++attempted)
        {
            ulong identToEvict;
            lock (_lruRoster)
            {
                if (_lruRoster.Count is 0)
                    break;
                identToEvict = _lruRoster.First!.Value;
                _lruRoster.RemoveFirst();
            }

            lock (_queuedReqs)
            {
                if (!_ownership.IsPossessed(identToEvict))

                    TryProceedObjectFree(identToEvict, out _);
            }
        }

        _cpuTriMeshStash.Clear();
    }

    public void DecrementRefCount(ulong ident)
    {
        (PreparationAsk? Pending, PreparationAsk? Active) canceled = default;
        bool finalHolder;
        lock (_queuedReqs)
        {
            finalHolder = _ownership.Count(ident) <= 1;
            if (finalHolder)
                canceled = UnfastenQueuedPrepBolted(ident);
        }

        if (finalHolder)
            AbortDetachedPrep(canceled);

        lock (_queuedReqs)
        {
            int newTally = _ownership.Release(ident);
            if (newTally > 0)
                return;

            _environChamberDescriptors.Remove(ident);
            _terminalPrepMisses.Remove(ident);
            if (_renderData.ContainsKey(ident))
            {
                // Instead of unloading, move resident data to LRU
                lock (_lruRoster)
                {
                    _lruRoster.Remove(ident);
                    _lruRoster.AddLast(ident);
                }
            }
            else
            {
                _ownership.Drop(ident);
            }
        }
    }

    public GlobalTriMeshBuffer? GlobalBuf { get; }

    internal (int RenderData, int AtlasArrays, int UnusedLru, long EstimatedBytes) Diagnostics
    {
        get
        {
            int tilesetArrs = 0;
            foreach (List<TextureAtlasKeeper> tilesets in _globalTilesets.Values)
                tilesetArrs += tilesets.Count;
            long tilesetOctets = DeriveTilesetOctets(_globalTilesets.Values);
            long sunsettingTilesetOctets = DeriveTilesetOctets(_sunsettingTilesets);
            long physicalOctets = DeriveFollowedGpuOctets(
                checked(
                    _latestNonArenaGpuMemory
                    + tilesetOctets
                    + sunsettingTilesetOctets),
                GlobalBuf?.PhysicalCapOctets ?? 0);
            return (_renderData.Count, tilesetArrs, _lruRoster.Count, physicalOctets);
        }
    }

    internal (int Count, long Bytes) CpuStashTelemetry =>
        (_cpuTriMeshStash.Count, _cpuTriMeshStash.HousedOctets);

    public void FreeRasterizeBlob(ulong ident)
    {
        (PreparationAsk? Pending, PreparationAsk? Active) canceled = default;
        lock (_queuedReqs)
        {
            if (_ownership.IsPossessed(ident))
            {
                int newTally = _ownership.Release(ident);
                if (newTally <= 0)
                {
                    _environChamberDescriptors.Remove(ident);
                    _terminalPrepMisses.Remove(ident);
                    canceled = UnfastenQueuedPrepBolted(ident);
                    if (_renderData.ContainsKey(ident))
                    {
                        lock (_lruRoster)
                        {
                            _lruRoster.Remove(ident);
                            _lruRoster.AddLast(ident);
                        }
                    }
                    else
                    {
                        _ownership.Drop(ident);
                    }
                }
            }
        }
        AbortDetachedPrep(canceled);
    }

    internal bool IsPossessed(ulong ident) => _ownership.IsPossessed(ident);

    internal static bool IsWithinGpuStashAllowance(
        long nonArenaOctets,
        long physicalArenaOctets,
        long ceilingOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nonArenaOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(physicalArenaOctets);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingOctets, 1);
        return nonArenaOctets <= ceilingOctets - Math.Min(ceilingOctets, physicalArenaOctets);
    }

    internal bool TryDequeueLinedTriMeshBlob(out MeshUploadQueueGear gear) =>
        _linedTriMeshBlob.TryDequeue(out gear);

    internal bool TryGlimpseLinedTriMeshBlob(out MeshUploadQueueGear gear) =>
        _linedTriMeshBlob.TryGlimpse(out gear);

    internal int TossUnownedLinedStem(int ceiling) =>
        _linedTriMeshBlob.TossUnownedStem(_ownership, ceiling);

    internal ShelfStats CpuTriMeshStashStats => _cpuTriMeshStash.Stats;

    internal ShelfStats DecodedTextureStashStats =>
        _preparedAssets.DecodedTextureStashStats;

    internal void RequeueLinedTriMeshBlob(MeshUploadQueueGear gear) =>
        _linedTriMeshBlob.Requeue(gear);

    internal void RejectUnsupportedLinedPush(
        MeshUploadQueueGear gear,
        NotSupportedException problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        _linedTriMeshBlob.Complete(gear);
        lock (_queuedReqs)
            _terminalPrepMisses.Add(gear.Data.ObjectId);
        _logger.LogError(
            problem,
            "Mesh 0x{Id:X10} generation {Generation} exceeds an explicit GPU upload limit",
            gear.Data.ObjectId,
            gear.Generation);
    }

    internal void AssignArenaBackpressure(bool turnedOn)
    {
        _arenaBackpressured = turnedOn;
        if (!turnedOn)
            ReactivatePrepWorkers();
    }

    internal (int Count, long Bytes) RecoverUnusedAssetList(
        int ceilingTally,
        long ceilingOctets,
        bool forceArenaReclamation = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingTally, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingOctets, 1);
        ReattemptSunsettingTilesetDisposals();
        ReattemptQueuedTilesetRetirements();
        ProgressQueuedPushRollbacks(ceilingTally);

        (int reclaimedTally, long reclaimedOctets) =
            ProgressQueuedObjectReleases(ceilingTally, ceilingOctets);
        int contenderAllowance;
        lock (_lruRoster)
            contenderAllowance = _lruRoster.Count;
        int attemptedContenders = 0;
        long followedTilesetOctets = checked(
            DeriveTilesetOctets(_globalTilesets.Values)
            + DeriveTilesetOctets(_sunsettingTilesets));

        while (reclaimedTally < ceilingTally
            && attemptedContenders < contenderAllowance)
        {
            ulong identToEvict;
            lock (_lruRoster)
            {
                long physicalArenaOctets = GlobalBuf?.PhysicalCapOctets ?? 0;
                long nonArenaAndTilesetOctets = checked(
                    _latestNonArenaGpuMemory
                    + followedTilesetOctets);
                if (!forceArenaReclamation
                    && IsWithinGpuStashAllowance(
                        nonArenaAndTilesetOctets,
                        physicalArenaOctets,
                        _upperGpuMemory)
                    && _lruRoster.Count <= _upperStashedObjects)

                    break;

                if (_lruRoster.Count is 0)
                    break;
                identToEvict = _lruRoster.First!.Value;
                _lruRoster.RemoveFirst();
            }
            ++attemptedContenders;

            lock (_queuedReqs)
            {
                if (!_ownership.IsPossessed(identToEvict))
                {
                    long octets = FetchObjectReclaimableOctets(identToEvict);
                    if (!FitsReclamationAllowance(octets, reclaimedOctets, ceilingOctets))
                    {
                        lock (_lruRoster)
                            _lruRoster.AddLast(identToEvict);
                        continue;
                    }

                    if (TryProceedObjectFree(identToEvict, out long finishedOctets))
                    {
                        reclaimedOctets = checked(reclaimedOctets + finishedOctets);
                        ++reclaimedTally;
                    }
                    // An unfinished release moves from the ordinary LRU to _objectReleases.
                }
            }
        }

        return (reclaimedTally, reclaimedOctets);
    }

    internal static PrepWorkerWakeAct DecidePrepWorkerWake(
        bool isDestroyed,
        bool hasQueuedReqs,
        bool loadingAtHiWater,
        bool arenaBackpressured)
    {
        if (isDestroyed)
            return PrepWorkerWakeAct.Exit;
        return !hasQueuedReqs || loadingAtHiWater || arenaBackpressured
            ? PrepWorkerWakeAct.ResetAndWait
            : PrepWorkerWakeAct.Process;
    }

    internal bool EvictOneVacantTileset()
    {
        ReattemptSunsettingTilesetDisposals();
        if (!_safeVacantTilesets.TryGrabOldestOverAllowance(out TextureAtlasKeeper victim))
            return false;

        var tag = (victim.Width, victim.Height, victim.Format);
        if (_globalTilesets.TryGetValue(tag, out List<TextureAtlasKeeper>? roster))
        {
            roster.Remove(victim);
            if (roster.Count is 0)
                _globalTilesets.Remove(tag);
        }
        _staleTilesets.Remove(victim);
        _sunsettingTilesets.Add(victim);
        victim.Dispose();
        DropFinishedTilesetRetirements();
        return true;
    }

    internal bool EvictOneFormerAsset() =>
        RecoverUnusedAssetList(1, long.MaxValue).Count is not 0;

    public long RasterizeBlobReadinessVer => Volatile.Read(ref _rasterizeBlobReadinessVer);

    internal static bool FitsReclamationAllowance(
        long contenderOctets,
        long alreadyReclaimedOctets,
        long ceilingOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contenderOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(alreadyReclaimedOctets);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingOctets, 1);
        return contenderOctets <= ceilingOctets - Math.Min(alreadyReclaimedOctets, ceilingOctets);
    }

    internal void ReactivatePrepWorkers()
    {
        lock (_queuedReqs)
            BeginPrepWorkersBolted();
    }

    internal bool SecureRasterizeBlobPrimed(ulong ident)
    {
        if (HasRasterizeBlob(ident))
            return true;

        EnvCellGeomAsk? environChamber;
        lock (_queuedReqs)
        {
            if (IsDisposed
                || !_ownership.IsPossessed(ident)
                || _terminalPrepMisses.Contains(ident))

                return false;
            environChamber = _environChamberDescriptors.TryGetValue(ident, out EnvCellGeomAsk descriptor)
                ? descriptor
                : null;
        }

        if (environChamber is { } req)
        {
            _ = PrepareEnvCellGeomMeshDataAsync(
                ident,
                req.SrcChamberIdent,
                req.EnvironmentId,
                req.CellStructure,
                req.Surfaces);
        }
        else if (ident > uint.MaxValue)
        {
            return false;
        }
        else
        {
            _ = ReadyTriMeshBlobAsync(ident, isRig: false);
        }
        return false;
    }

    internal GlobalTriMeshCapOutcome SecureGlobalBufCap(
        MeshUploadQueueGear gear,
        out GlobalTriMeshMaintenanceHop hop)
    {
        if (GlobalBuf is null)
        {
            hop = default;
            return GlobalTriMeshCapOutcome.Ready;
        }
        (int vertTally, int ordinalTally) = FetchGlobalTriMeshElemCounts(gear.Data);
        return GlobalBuf.SecurePushCap(vertTally, ordinalTally, out hop);
    }

    internal TenancyDomainCapture GrabObjectTriMeshResidency()
    {
        var arena = GlobalBuf;
        long nonArenaSunsetting = 0;
        foreach (ThingFreeTicket ticket in _objectReleases.Values)
        {
            nonArenaSunsetting = checked(
                nonArenaSunsetting
                + Math.Max(0, ticket.Data.NonArenaGpuOctets));
        }
        nonArenaSunsetting = Math.Min(
            nonArenaSunsetting,
            _latestNonArenaGpuMemory);

        long arenaHoused = arena?.HousedCapOctets ?? 0;
        long arenaAsked = arena?.AskedMigrationOctets ?? 0;
        long arenaSunsetting = checked(
            (arena?.QueuedSpanSunsetOctets ?? 0)
            + (arena?.RetiredBackingOctets ?? 0));
        long tilesetHoused = DeriveTilesetOctets(_globalTilesets.Values);
        long tilesetSunsetting = DeriveTilesetOctets(_sunsettingTilesets);
        return new TenancyDomainCapture(
            TenancyDomain.ObjectMeshes,
            EntryCount: checked(
                _renderData.Count
                + _globalTilesets.Values.Sum(static tilesets => tilesets.Count)
                + _sunsettingTilesets.Count),
            OwnerCount: _ownership.SumReferenceTally,
            Charges: new TenancyCharges(
                GpuRequestedBytes: arenaAsked,
                GpuResidentBytes: checked(
                    _latestNonArenaGpuMemory
                    - nonArenaSunsetting
                    + arenaHoused
                    + tilesetHoused),
                RetiringBytes: checked(
                    nonArenaSunsetting
                    + arenaSunsetting
                    + tilesetSunsetting)),
            BudgetBytes: _upperGpuMemory);
    }

    internal TenancyDomainCapture GrabReadiedTriMeshResidency()
    {
        ShelfStats stats = _cpuTriMeshStash.Stats;
        return new TenancyDomainCapture(
            TenancyDomain.PreparedMeshCpu,
            EntryCount: _cpuTriMeshStash.Count,
            OwnerCount: 0,
            Charges: new TenancyCharges(
                CpuPreparedBytes: _cpuTriMeshStash.HousedOctets),
            BudgetBytes: _cpuTriMeshStash.ByteCap,
            Hits: stats.Hits,
            Misses: stats.Misses,
            Evictions: stats.Evictions);
    }

    internal TenancyDomainCapture GrabLoadingResidency()
    {
        return new(
            TenancyDomain.MeshStaging,
            EntryCount: _linedTriMeshBlob.ClaimTally,
            OwnerCount: 0,
            Charges: new TenancyCharges(
                StagingBytes: _linedTriMeshBlob.ClaimedOctets),
            BudgetBytes: _linedTriMeshBlob.CeilingOctets);
    }

    private (PreparationAsk? Pending, PreparationAsk? Active) UnfastenQueuedPrepBolted(ulong ident)
    {
        PreparationAsk? queued = null;
        if (_queuedReqByIdent.Remove(ident, out LinkedListNode<PreparationAsk>? joint))
        {
            _queuedReqs.Remove(joint);
            queued = joint.Value;
            if (_prepTasks.TryGetValue(ident, out Task<HarvestedMesh?>? latest)
                && ReferenceEquals(latest, queued.Completion.Task))

                _prepTasks.TryRemove(ident, out _);
        }
        _engagedPrepByIdent.TryGetValue(ident, out PreparationAsk? engaged);
        return (queued, engaged);
    }

    private (int Vertices, int Indices) FetchGlobalTriMeshElemCounts(HarvestedMesh triMeshBlob)
    {
        if (triMeshBlob.IsSetup)
        {
            return triMeshBlob.EnvCellGeometry is { } nested
                && !HasRasterizeBlob(nested.ObjectId)
                ? FetchGlobalTriMeshElemCounts(nested)
                : default;
        }
        if (triMeshBlob.Vertices.Length is 0)
            return default;

        int ordinalTally = 0;
        foreach (List<TextureHarvestBatch> lots in triMeshBlob.TextureBatches.Values)
        {
            foreach (TextureHarvestBatch lot in lots)
                ordinalTally = checked(ordinalTally + lot.Indices.Count);
        }
        return (triMeshBlob.Vertices.Length, ordinalTally);
    }

    private (int Count, long Bytes) ProgressQueuedObjectReleases(
        int ceilingTally,
        long ceilingOctets)
    {
        int tally = 0;
        long octets = 0;
        int attempts = Math.Min(ceilingTally, _objectFreeFifo.Count);
        for (int idx = 0; idx < attempts; ++idx)
        {
            ulong tag = _objectFreeFifo.Dequeue();
            if (!_objectReleases.TryGetValue(tag, out ThingFreeTicket? ticket))
                continue;
            ticket.IsQueued = false;
            if (!FitsReclamationAllowance(
                ticket.ReclaimableOctets,
                octets,
                ceilingOctets))
            {
                ticket.IsQueued = true;
                _objectFreeFifo.Enqueue(tag);
                continue;
            }
            if (!TryProceedObjectFree(tag, out long finishedOctets))
                continue;

            octets = checked(octets + finishedOctets);
            ++tally;
        }
        return (tally, octets);
    }

    private long FetchReclaimableOctets(ThingRasterizeBlob blob)
    {
        return blob.GlobalAlloc is { } alloc
            ? checked(
                (long)alloc.Vertices.Length * VertLocusNormBitmap.Size
                + (long)alloc.Indices.Length * sizeof(ushort))
            : Math.Max(0, blob.NonArenaGpuOctets);
    }

    private long FetchObjectReclaimableOctets(ulong tag)
    {
        return _objectReleases.TryGetValue(tag, out ThingFreeTicket? free)
            ? free.ReclaimableOctets
            : _renderData.TryGetValue(tag, out ThingRasterizeBlob? blob)
            ? FetchReclaimableOctets(blob)
            : 0;
    }

    private ThingFreeTicket? FetchOrBuildObjectFree(ulong tag)
    {
        if (_objectReleases.TryGetValue(tag, out ThingFreeTicket? extant))
            return extant;
        if (!_renderData.TryGetValue(tag, out ThingRasterizeBlob? blob))
            return null;

        var releases = new List<(string Name, Action Release)>();
        if (blob.GlobalAlloc is { } alloc)
        {
            releases.Add((
                "global-index-range",
                () => GlobalBuf!.FreeOrdinalSpan(alloc)));
            releases.Add((
                "global-vertex-range",
                () => GlobalBuf!.FreeVertSpan(alloc)));
        }

        for (int idx = 0; idx < blob.Batches.Count; ++idx)
        {
            int lotOrdinal = idx;
            var lot = blob.Batches[lotOrdinal];
            if (lot.Tileset is null)
                continue;
            releases.Add((
                $"atlas-texture-{lotOrdinal}",
                () => FreeTilesetTexture(
                    blob.Batches[lotOrdinal].Tileset,
                    blob.Batches[lotOrdinal].Key)));
        }

        if (blob.IsSetup)
        {
            for (int idx = 0; idx < blob.SetupParts.Count; ++idx)
            {
                int pieceOrdinal = idx;
                releases.Add((
                    $"setup-part-{pieceOrdinal}",
                    () => DecrementRefCount(blob.SetupParts[pieceOrdinal].GfxObjId)));
            }
        }

        releases.Add((
            "non-arena-memory-accounting",
            () => _latestNonArenaGpuMemory = checked(
                _latestNonArenaGpuMemory - blob.NonArenaGpuOctets)));

        ThingFreeTicket ticket = new ThingFreeTicket(
            tag,
            blob,
            FetchReclaimableOctets(blob),
            new RetryableAssetFreeRegister(releases));
        _objectReleases.Add(tag, ticket);
        MarkRenderDataAvailabilityChanged();
        return ticket;
    }

    private void MarkRenderDataAvailabilityChanged() =>
        Interlocked.Increment(ref _rasterizeBlobReadinessVer);

    private void FlagTilesetEngaged(TextureAtlasKeeper tileset) => _safeVacantTilesets.FlagPossessed(tileset);

    private void ReattemptSunsettingTilesetDisposals()
    {
        for (int idx = 0; idx < _sunsettingTilesets.Count; ++idx)
            _sunsettingTilesets[idx].Dispose();
        DropFinishedTilesetRetirements();
    }

    private void ReattemptQueuedTilesetRetirements()
    {
        List<Exception>? misses = null;
        foreach (List<TextureAtlasKeeper> tilesets in _globalTilesets.Values)
        {
            for (int idx = 0; idx < tilesets.Count; ++idx)
            {
                try { tilesets[idx].ReattemptQueuedRetirements(); }
                catch (Exception problem) { (misses ??= []).Add(problem); }
            }
        }
        if (misses is not null)
        {
            throw new AggregateException(
                "One or more texture-atlas layer retirements could not be published",
                misses);
        }
    }

    private void DropFinishedTilesetRetirements()
    {
        for (int idx = _sunsettingTilesets.Count - 1; idx >= 0; --idx)
        {
            if (!_sunsettingTilesets[idx].IsPhysicalSunsetDone)
                continue;
            _sunsettingTilesets[idx].TextureArr.FreeTextureSockets();
            _sunsettingTilesets.RemoveAt(idx);
        }
    }

    private void OnTilesetGpuSafeVacant(TextureAtlasKeeper tileset)
    {
        if (IsDisposed || !tileset.IsGpuSafeVacant || _safeVacantTilesets.Contains(tileset))
            return;
        _safeVacantTilesets.FlagUnowned(tileset, tileset.AllocatedBytes);
    }

    private void OnPrepWorkerFinished(Task worker)
    {
        if (worker.IsFaulted)
            _logger.LogError(worker.Exception, "Mesh preparation worker terminated unexpectedly");

        lock (_queuedReqs)
        {
            _workerTasks.Remove(worker);
            BeginPrepWorkersBolted();
        }
    }

    private static void FreeTilesetTexture(
        TextureAtlasKeeper tileset,
        BitmapTag tag)
    {
        try
        {
            tileset.FreeTexture(tag);
        }
        catch (Exception problem) when (!tileset.HasTexture(tag))
        {
            throw new TriMeshRefAlterationFault(
                $"Texture {tag} was released from atlas slot {tileset.Slot}, but its retirement callback failed.",
                alterationSealed: true,
                problem);
        }
    }

    private void BeginPrepWorkersBolted()
    {
        if (IsDisposed)
            return;

        while (_workerTasks.Count < UpperParallelLoads)
        {
            Task worker = Task.Run(ProcessQueue);
            _workerTasks.Add(worker);
            _ = worker.ContinueWith(
                OnPrepWorkerFinished,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        if (_queuedReqs.Count is not 0
            && !_linedTriMeshBlob.IsAtHiWater
            && !_arenaBackpressured)
            _prepJobOnHand.Set();
    }
}
