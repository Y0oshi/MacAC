namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmTriMeshBridge
{
    public void Tick()
    {
        if (_isUninitialized) return;
        if (_destroyed) return;

        var triMeshKeeper = _meshManager!;
        _visualsDev!.ProcessQueue();
        List<MeshUploadQueueGear>? requeue = null;
        MeshUploadFrameAllowance pushAllowance =
            _destUnveilPushPrecedence
                ? _destUnveilPushAllowance
                : _plainPushAllowance;
        int ceilingUploads =
            _destUnveilPushPrecedence
                ? DestUnveilCeilingUploadsPerCycle
                : CeilingUploadsPerCycle;
        pushAllowance.Reset();
        _mipmapsBudgeted.Clear();
        int staleTossTally = 0;
        bool arenaBackpressured = false;
        var globalBuf = triMeshKeeper.GlobalBuf;

        if (globalBuf?.IsMigrationInHeadway == true)
        {
            arenaBackpressured = TickBranch(globalBuf, pushAllowance);
        }

        while (globalBuf?.IsMigrationInHeadway != true
            && pushAllowance.BufDuplicateOctets is 0
            && pushAllowance.ObjectTally < ceilingUploads)
        {
            staleTossTally += triMeshKeeper.TossUnownedLinedStem(
                CeilingStaleDiscardsPerCycle - staleTossTally);
            if (staleTossTally >= CeilingStaleDiscardsPerCycle)
                break;
            if (!triMeshKeeper.TryGlimpseLinedTriMeshBlob(out MeshUploadQueueGear upcoming))
                break;

            GlobalTriMeshCapOutcome cap;
            GlobalTriMeshMaintenanceHop maintenance;
            try
            {
                cap = triMeshKeeper.SecureGlobalBufCap(upcoming, out maintenance);
            }
            catch (NotSupportedException problem)
            {
                RejectUnsupportedFront(triMeshKeeper, upcoming, problem);
                throw;
            }
            if (cap == GlobalTriMeshCapOutcome.MigrationStarted)
            {
                pushAllowance.CaptureBufMaintenance(
                    maintenance.AllocationBytes,
                    maintenance.CopyBytes,
                    maintenance.NewBufferCount);
                arenaBackpressured = true;
                break;
            }
            if (cap == GlobalTriMeshCapOutcome.MigrationInProgress)
            {
                arenaBackpressured = true;
                break;
            }
            if (cap == GlobalTriMeshCapOutcome.NeedsReclamation)
            {
                // Do not evict another batch while the frame-fence queue is already returning ranges or retiring
                // an old backing store.
                if (globalBuf?.HasQueuedReclamation != true)
                {
                    var reclaimed = triMeshKeeper.RecoverUnusedAssetList(
                        CeilingReclaimedTriMeshesPerCycle,
                        CeilingReclaimedTriMeshOctetsPerCycle,
                        forceArenaReclamation: true);
                    if (reclaimed.Count is 0)
                    {
                        throw new NotSupportedException(
                            $"The live global-mesh working set can't fit the supported "
                            + $"{GlobalTriMeshBuffer.CeilingVertBufOctets + GlobalTriMeshBuffer.CeilingOrdinalBufOctets:N0}-byte arena "
                            + $"(blocked staging generation {upcoming.Generation}, object 0x{upcoming.Data.ObjectId:X10}).");
                    }
                }
                arenaBackpressured = true;
                break;
            }

            TriMeshPushPrice price;
            try
            {
                price = triMeshKeeper.PlanPushPrice(
                    upcoming.Data,
                    _mipmapsBudgeted,
                    upcoming.Generation);
                if (!pushAllowance.TryAdmit(price))
                    break;
            }
            catch (NotSupportedException problem)
            {
                RejectUnsupportedFront(triMeshKeeper, upcoming, problem);
                throw;
            }

            if (!triMeshKeeper.TryDequeueLinedTriMeshBlob(out MeshUploadQueueGear triMeshBlob))
                break;
            if (triMeshBlob.Generation != upcoming.Generation)
                throw new InvalidOperationException("The staged mesh FIFO head changed during render-thread admission");
            if (triMeshKeeper.PushOrRequeue(triMeshBlob))
                (requeue ??= []).Add(triMeshBlob);
            triMeshKeeper.AppendStaleTilesetsTo(_mipmapsBudgeted);
        }
        if (requeue is not null)
            foreach (var gear in requeue)
                triMeshKeeper.RequeueLinedTriMeshBlob(gear);
        triMeshKeeper.AssignArenaBackpressure(arenaBackpressured);

        (PreviousMipmapArrTally, PreviousMipmapOctets) = triMeshKeeper.ProduceMipmaps();
        if (globalBuf?.HasQueuedReclamation != true)
        {
            triMeshKeeper.RecoverUnusedAssetList(
                CeilingReclaimedTriMeshesPerCycle,
                CeilingReclaimedTriMeshOctetsPerCycle);
        }
        triMeshKeeper.EvictOneVacantTileset();

        if (pushAllowance.ObjectTally is 0
            && PreviousMipmapArrTally is 0
            && triMeshKeeper.LinedTriMeshTally is 0
            && globalBuf?.IsMigrationInHeadway == false
            && globalBuf.TryTrimUnusedRear(out GlobalTriMeshMaintenanceHop trim))
        {
            pushAllowance.CaptureBufMaintenance(
                trim.AllocationBytes,
                trim.CopyBytes,
                trim.NewBufferCount);
            triMeshKeeper.AssignArenaBackpressure(true);
        }

        PreviousPushTally = pushAllowance.ObjectTally;
        PreviousPushOctets = pushAllowance.SrcOctets;
        PreviousStaleTossTally = staleTossTally;
        PreviousArrAllocOctets = pushAllowance.ArrAllocOctets;
        PreviousPlannedMipmapOctets = pushAllowance.MipmapOctets;
        PreviousNewArrTally = pushAllowance.NewArrTally;
        PreviousBufPushOctets = pushAllowance.BufPushOctets;
        PreviousBufAllocOctets = pushAllowance.BufAllocOctets;
        PreviousBufDuplicateOctets = pushAllowance.BufDuplicateOctets;
        PreviousNewBufTally = pushAllowance.NewBufTally;
    }

    private bool TickBranch(GlobalTriMeshBuffer globalBuf, MeshUploadFrameAllowance pushAllowance)
    {
        bool arenaBackpressured;
        var maintenance =
                        globalBuf.ProgressMigration(CeilingBufDuplicateOctetsPerCycle);
        pushAllowance.CaptureBufMaintenance(
                        maintenance.AllocationBytes,
                        maintenance.CopyBytes,
                        maintenance.NewBufferCount);
        arenaBackpressured = !maintenance.Completed;
        return arenaBackpressured;
    }
}
