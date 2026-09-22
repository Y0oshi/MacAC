using System.Threading.Channels;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Paging;

public sealed partial class LandblockStreamer
{
    public static int DefaultWorkerTally =>
        Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 8));

    public static LandblockStreamer CreateForRequests(
        Func<LandblockBuildAsk, LandblockAssemble?> pullLb,
        Func<uint, MountedLandblock?, MacAC.Mechanics.Landscape.LandblockTessellationData?>? assembleTriMeshOrNull = null,
        int? workerTally = null)
    {
        return new(pullLb, assembleTriMeshOrNull, supportsReqOrigin: true, workerTally);
    }

    public void Start()
    {
        lock (_teardownLatch)
        {
            if (System.Threading.Volatile.Read(ref _destroyed) is not 0)
                throw new ObjectDisposedException(nameof(LandblockStreamer));
            if (_workers is not null)
                return;

            Thread[] workers = new Thread[_lanes.Length];
            System.Threading.Volatile.Write(ref _engagedWorkers, workers.Length);
            for (int idx = 0; idx < workers.Length; ++idx)
            {
                int lane = idx;
                workers[idx] = new Thread(() => WorkerLoop(lane))
                {
                    IsBackground = true,
                    Name = workers.Length is 1
                        ? "macac.streaming.worker"
                        : $"macac.streaming.worker.{lane}",
                };
            }
            foreach (Thread worker in workers)
                worker.Start();
            _workers = workers;
        }
    }

    public void EnqueueLoad(
        uint lbIdent,
        LandblockFlowJobFlavor sort = LandblockFlowJobFlavor.LoadNear,
        ulong gen = 0)
    {
        if (_supportsReqOrigin)
        {
            throw new InvalidOperationException(
                "Request-aware landblock loaders require an explicit captured origin");
        }
        EnqueueLoad(new LandblockBuildAsk(
            lbIdent,
            sort,
            gen,
            default));
    }

    public void EnqueueLoad(LandblockBuildAsk req)
    {
        if (System.Threading.Volatile.Read(ref _destroyed) is not 0)
            throw new ObjectDisposedException(nameof(LandblockStreamer));
        if (_supportsReqOrigin && !req.Origin.IsSpecified)
        {
            throw new InvalidOperationException(
                "Request-aware landblock loaders require a specified captured origin");
        }
        if (!_supportsReqOrigin && req.Origin.IsSpecified)
        {
            throw new InvalidOperationException(
                "This compatibility landblock loader can't consume a non-default build origin. " +
                "Use LandblockStreamer.CreateForRequests");
        }
        MacAC.Mechanics.Kinetics.KineticTelemetry.TraceWarp(
            "ENQ",
            req.LandblockId,
            $"kind={req.Kind} origin=({req.Origin.CenterX:X2},{req.Origin.CenterY:X2})");
        EmitJob(new LandblockFlowJob.Pull(
            req.LandblockId,
            req.Kind,
            req.Generation,
            req.Origin));
    }

    public void QueueUnload(uint lbIdent, ulong gen = 0) => EmitJob(new LandblockFlowJob.Drop(lbIdent, gen));

    public void WipeQueuedLoads() => EmitJob(new LandblockFlowJob.WipeLoads());

    public IReadOnlyList<LandblockFlowOutcome> EmptyCompletions(int upperLotDims = DefaultEmptyLotDims)
    {
        var lot = new List<LandblockFlowOutcome>(upperLotDims);
        while (lot.Count < upperLotDims && TryRead(out var outcome))
        {
            if (outcome is null)
                throw new InvalidOperationException(
                    "The completion channel returned a null result");
            lot.Add(outcome);
        }
        return lot;
    }

    public bool TryPeek(out LandblockFlowOutcome? outcome) =>
        _outbox.Reader.TryPeek(out outcome);

    public bool TryRead(out LandblockFlowOutcome? outcome)
    {
        if (!_outbox.Reader.TryRead(out outcome))
            return false;
        System.Threading.Interlocked.Decrement(ref _wrapUpBacklog);
        return true;
    }

    public void Dispose()
    {
        lock (_teardownLatch)
        {
            if (_teardownFinished)
                return;

            System.Threading.Interlocked.Exchange(ref _destroyed, 1);
            _abort.Cancel();
            lock (_inboxLatch)
            {
                foreach (Channel<LandblockFlowJob> lane in _lanes)
                    lane.Writer.TryComplete();
            }
            if (_workers is { } workers)
            {
                foreach (Thread worker in workers)
                    worker.Join();
            }
            _abort.Dispose();
            _teardownFinished = true;
        }
    }

    public int BacklogTally
    {
        get
        {
            return Math.Max(
        0,
        System.Threading.Volatile.Read(ref _wrapUpBacklog));
        }
    }

    internal int LaneFor(uint lbIdent)
    {
        if (_lanes.Length is 1)
            return 0;
        uint mixed = (lbIdent >> 16) * 2654435761u;
        return (int)(mixed % (uint)_lanes.Length);
    }

    private static void QueuePrioritized(
        LandblockFlowJob job,
        Queue<LandblockFlowJob> hiPrecedence,
        Queue<LandblockFlowJob> loPrecedence)
    {
        if (job is LandblockFlowJob.Pull
            {
                Kind: LandblockFlowJobFlavor.LoadNear or LandblockFlowJobFlavor.PromoteToNear
            } hi)
        {
            DropLoPrecedenceJobsForLb(
                loPrecedence,
                hi.LandblockId,
                dropPullFaraway: true,
                dropUnload: true);
            hiPrecedence.Enqueue(job);
            return;
        }

        loPrecedence.Enqueue(job);
    }

    private void EmitJob(LandblockFlowJob job)
    {
        lock (_inboxLatch)
        {
            if (System.Threading.Volatile.Read(ref _destroyed) is not 0)
                throw new ObjectDisposedException(nameof(LandblockStreamer));
            if (_workerMiss is { } miss)
                throw new InvalidOperationException("A landblock streaming worker has terminated", miss);
            if (job is LandblockFlowJob.WipeLoads)
            {
                foreach (Channel<LandblockFlowJob> lane in _lanes)
                {
                    if (!lane.Writer.TryWrite(job))
                        throw new InvalidOperationException("The landblock streaming inbox is no longer accepting work");
                }
                return;
            }
            if (!_lanes[LaneFor(job.LandblockId)].Writer.TryWrite(job))
                throw new InvalidOperationException("The landblock streaming inbox is no longer accepting work");
        }
    }

    private void BroadcastOutcome(LandblockFlowOutcome outcome)
    {
        if (_outbox.Writer.TryWrite(outcome))
            System.Threading.Interlocked.Increment(ref _wrapUpBacklog);
    }

    private void WorkerLoop(int laneOrdinal)
    {
        var inbox = _lanes[laneOrdinal].Reader;
        var hiPrecedence = new Queue<LandblockFlowJob>();
        var loPrecedence = new Queue<LandblockFlowJob>();

        try
        {
            while (!_abort.Token.IsCancellationRequested)
            {
                if (hiPrecedence.Count is 0 &&
                    loPrecedence.Count is 0 &&
                    !inbox.WaitToReadAsync(_abort.Token).AsTask().GetAwaiter().GetResult())

                    break;

                while (inbox.TryRead(out var job))
                {
                    if (job is LandblockFlowJob.WipeLoads)
                    {
                        DiscardPullJobs(hiPrecedence);
                        DiscardPullJobs(loPrecedence);
                        continue;
                    }
                    QueuePrioritized(job, hiPrecedence, loPrecedence);
                }

                if (hiPrecedence.Count is 0 && loPrecedence.Count is 0)
                    continue;

                if (_abort.Token.IsCancellationRequested) return;
                LandblockFlowJob upcoming = hiPrecedence.Count > 0
                    ? hiPrecedence.Dequeue()
                    : loPrecedence.Dequeue();
                ProcessJob(upcoming);
            }
        }
        catch (OperationCanceledException) { /* graceful shutdown */ }
        catch (Exception exc)
        {
            bool cascade;
            lock (_inboxLatch)
            {
                cascade = _workerMiss is not null && exc is ChannelClosedException;
                _workerMiss ??= exc;
                foreach (Channel<LandblockFlowJob> lane in _lanes)
                    lane.Writer.TryComplete(exc);
            }
            if (!cascade)
            {
                BroadcastOutcome(new LandblockFlowOutcome.ClientWorkerCrashed(
                    _lanes.Length is 1
                        ? exc.ToString()
                        : $"worker {laneOrdinal}: {exc}"));
                _abort.Cancel();
            }
        }
        finally
        {
            if (Interlocked.Decrement(ref _engagedWorkers) is 0)
                _outbox.Writer.TryComplete();
        }
    }

    private static void DiscardPullJobs(Queue<LandblockFlowJob> fifo)
    {
        int tally = fifo.Count;
        for (int idx = 0; idx < tally; ++idx)
        {
            LandblockFlowJob job = fifo.Dequeue();
            if (job is not LandblockFlowJob.Pull)
                fifo.Enqueue(job);
        }
    }

    private static void DropLoPrecedenceJobsForLb(
        Queue<LandblockFlowJob> fifo,
        uint lbIdent,
        bool dropPullFaraway,
        bool dropUnload)
    {
        int tally = fifo.Count;
        for (int idx = 0; idx < tally; ++idx)
        {
            LandblockFlowJob job = fifo.Dequeue();
            bool drop = job.LandblockId == lbIdent && job switch
            {
                LandblockFlowJob.Pull { Kind: LandblockFlowJobFlavor.LoadFar } => dropPullFaraway,
                LandblockFlowJob.Drop => dropUnload,
                _ => false
            };
            if (!drop)
                fifo.Enqueue(job);
        }
    }

    private void ProcessJob(LandblockFlowJob job)
    {
        switch (job)
        {
            case LandblockFlowJob.Pull pull:
                try
                {
                    var assemble = _pullLb(pull.Request);
                    if (assemble is null)
                    {
                        BroadcastOutcome(new LandblockFlowOutcome.Botched(
                            pull.LandblockId, "LandblockReader.Load returned null", pull.Generation));
                        break;
                    }
                    if (assemble.Origin != pull.Origin)
                    {
                        BroadcastOutcome(new LandblockFlowOutcome.Botched(
                            pull.LandblockId,
                            $"Landblock build origin {assemble.Origin} did not match request origin {pull.Origin}",
                            pull.Generation));
                        break;
                    }
                    var landblock = assemble.Landblock;
                    if (pull.Kind == LandblockFlowJobFlavor.PromoteToNear)
                    {
                        var promotedTriMesh = _assembleTriMeshOrNull(pull.LandblockId, landblock);
                        if (promotedTriMesh is null)
                        {
                            BroadcastOutcome(new LandblockFlowOutcome.Botched(
                                pull.LandblockId, "buildMeshOrNull returned null", pull.Generation));
                            break;
                        }
                        BroadcastOutcome(new LandblockFlowOutcome.Elevated(
                            pull.LandblockId, assemble, promotedTriMesh, pull.Generation));
                        break;
                    }
                    var triMesh = _assembleTriMeshOrNull(pull.LandblockId, landblock);
                    if (triMesh is null)
                    {
                        BroadcastOutcome(new LandblockFlowOutcome.Botched(
                            pull.LandblockId, "buildMeshOrNull returned null", pull.Generation));
                        break;
                    }
                    LandblockFlowTier tier = pull.Kind == LandblockFlowJobFlavor.LoadFar
                        ? LandblockFlowTier.Far : LandblockFlowTier.Near;
                    if (tier == LandblockFlowTier.Far)
                    {
                        bool hasNearbyCargo =
                            landblock.Entities.Count > 0 ||
                            assemble.EnvCells is not null ||
                            landblock.PhysicsDats is { } kineticsDatFiles &&
                            (kineticsDatFiles.Info is not null ||
                             kineticsDatFiles.EnvCells.Count > 0 ||
                             kineticsDatFiles.Environments.Count > 0 ||
                             kineticsDatFiles.Setups.Count > 0 ||
                             kineticsDatFiles.GfxObjs.Count > 0);
                        System.Diagnostics.Debug.Assert(
                            !hasNearbyCargo,
                            $"Far-tier factory returned Near payload for LB 0x{pull.LandblockId:X8}");
                        landblock = new MountedLandblock(
                            landblock.LandblockId,
                            landblock.Heightmap,
                            System.Array.Empty<MacAC.Mechanics.Realm.RealmActor>(),
                            KineticDatBundle.Empty);
                        assemble = new LandblockAssemble(
                            landblock,
                            Origin: assemble.Origin,
                            TerrainBounds: assemble.TerrainBounds);
                    }
                    BroadcastOutcome(new LandblockFlowOutcome.Fetched(
                        pull.LandblockId, tier, assemble, triMesh, pull.Generation));
                }
                catch (Exception exc)
                {
                    BroadcastOutcome(new LandblockFlowOutcome.Botched(
                        pull.LandblockId, exc.ToString(), pull.Generation));
                }
                break;

            case LandblockFlowJob.Drop unload:
                BroadcastOutcome(new LandblockFlowOutcome.Dropped(
                    unload.LandblockId,
                    unload.Generation));
                break;
        }
    }
}
