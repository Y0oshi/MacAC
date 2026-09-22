namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{
    public void Dispose()
    {
        lock (_teardownLatch)
        {
            if (_teardownFinished)
                return;
            if (_teardownRunning)
                return;
            _teardownRunning = true;
            try
            {
                TeardownCore();
                _teardownFinished = true;
            }
            finally
            {
                _teardownRunning = false;
            }
        }
    }

    private void TeardownCore()
    {
        List<Exception>? misses = null;
        static void Grab(ref List<Exception>? failures, Action act)
        {
            try { act(); }
            catch (Exception exc) { (failures ??= []).Add(exc); }
        }
        if (!_workersQuiesced)
        {
            PreparationAsk[] queuedToAbort;
            PreparationAsk[] engagedToAbort;
            lock (_queuedReqs)
            {
                IsDisposed = true;
                queuedToAbort = [.. _queuedReqs];
                engagedToAbort = [.. _engagedPrepByIdent.Values];
                foreach (PreparationAsk req in queuedToAbort)
                    _prepTasks.TryRemove(req.Id, out _);
                _queuedReqs.Clear();
                _queuedReqByIdent.Clear();
                _environChamberDescriptors.Clear();
                _terminalPrepMisses.Clear();
            }
            foreach (PreparationAsk req in queuedToAbort)
            {
                Grab(ref misses, req.Cancel);
                req.Completion.TrySetCanceled(req.Abort.Token);
                Grab(ref misses, req.TeardownAbort);
            }
            foreach (PreparationAsk req in engagedToAbort)
                Grab(ref misses, req.Cancel);

            Grab(ref misses, _prepJobOnHand.Set);
            Task[] workers;
            lock (_queuedReqs)
                workers = [.. _workerTasks];
            Grab(ref misses, () => Task.WhenAll(workers).GetAwaiter().GetResult());
            lock (_queuedReqs)
            {
                _workerTasks.RemoveWhere(worker => worker.IsCompleted);
                if (_workerTasks.Count is not 0)
                {
                    (misses ??= []).Add(
                        new InvalidOperationException("Mesh workers remained live after their join completed"));
                }
                else
                {
                    _workersQuiesced = true;
                }
            }
        }

        ulong[] objectIdents = [.. _renderData.Keys
            .Concat(_objectReleases.Keys)
            .Distinct()];
        for (int idx = 0; idx < objectIdents.Length; ++idx)
        {
            ulong ident = objectIdents[idx];
            try
            {
                if (!TryProceedObjectFree(ident, out _))
                    (misses ??= []).Add(new InvalidOperationException(
                        $"Mesh 0x{ident:X10} still has unfinished resource-release stages"));
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        ulong[] undoIdents = [.. _pushRollbacks.Keys];
        for (int idx = 0; idx < undoIdents.Length; ++idx)
        {
            ulong ident = undoIdents[idx];
            try
            {
                if (!TryProceedPushUndo(ident))
                    (misses ??= []).Add(new InvalidOperationException(
                        $"Mesh 0x{ident:X10} still has unfinished upload-rollback stages"));
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        if (_objectReleases.Count is not 0 || _pushRollbacks.Count is not 0)
        {
            throw new AggregateException(
                "One or more mesh resource transactions remain unfinished",
                misses ?? [new InvalidOperationException("Mesh resource teardown didn't converge")]);
        }

        Grab(ref misses, ReattemptQueuedTilesetRetirements);
        Grab(ref misses, ReattemptSunsettingTilesetDisposals);

        bool tilesetMiss = false;
        foreach (List<TextureAtlasKeeper> tilesetRoster in _globalTilesets.Values)
        {
            foreach (TextureAtlasKeeper tileset in tilesetRoster)
            {
                try { tileset.Dispose(); }
                catch (Exception problem)
                {
                    tilesetMiss = true;
                    (misses ??= []).Add(problem);
                }
            }
        }
        if (tilesetMiss)
            throw new AggregateException(
                "One or more texture atlases could not be disposed",
                misses!);

        if (GlobalBuf is not null)
            Grab(ref misses, GlobalBuf.Dispose);

        if (misses is not null)
            throw new AggregateException("One or more mesh-manager teardown operations failed", misses);

        _renderData.Clear();
        _objectReleases.Clear();
        MarkRenderDataAvailabilityChanged();
        _objectFreeFifo.Clear();
        _pushRollbacks.Clear();
        _pushUndoFifo.Clear();
        _globalTilesets.Clear();
        _sunsettingTilesets.Clear();
        _staleTilesets.Clear();
        _safeVacantTilesets.Clear();
        _latestNonArenaGpuMemory = 0;
        _cpuTriMeshStash.Clear();
        lock (_lruRoster)
            _lruRoster.Clear();

        if (!_jobSignalDestroyed)
        {
            _prepJobOnHand.Dispose();
            _jobSignalDestroyed = true;
        }

    }
}
