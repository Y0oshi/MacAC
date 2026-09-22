using MacAC.Assets;

namespace MacAC.Client.Graphics.Batching;

public partial class ThingTriMeshKeeper
{
    public Task<HarvestedMesh?> PrepareEnvCellGeomMeshDataAsync(
        ulong geomIdent,
        uint srcChamberIdent,
        uint surroundingsIdent,
        ushort chamberStructure,
        List<ushort> canvases,
        CancellationToken token = default)
    {
        if (IsDisposed || HasRasterizeBlob(geomIdent)) return Task.FromResult<HarvestedMesh?>(null);

        EnvCellGeomAsk environChamber = new EnvCellGeomAsk
        {
            SrcChamberIdent = srcChamberIdent,
            EnvironmentId = surroundingsIdent,
            CellStructure = chamberStructure,
            Surfaces = canvases
        };
        lock (_queuedReqs)
        {
            _environChamberDescriptors[geomIdent] = environChamber;
            _terminalPrepMisses.Remove(geomIdent);
        }

        HarvestedMesh? postponedStashedBlob = null;
        if (_cpuTriMeshStash.TryFetchAndJuncturePossessed(
                geomIdent,
                _linedTriMeshBlob,
                _ownership,
                out HarvestedMesh? stashedBlob,
                out TriMeshJunctureOutcome stashJuncture))
        {
            if (stashJuncture != TriMeshJunctureOutcome.HighWater)
                return Task.FromResult(stashedBlob);
            postponedStashedBlob = stashedBlob;
        }

        lock (_queuedReqs)
        {
            if (_prepTasks.TryGetValue(geomIdent, out Task<HarvestedMesh?>? extant)
                && !extant.IsFaulted
                && !extant.IsCanceled)
            {
                bool canceledEngagedGen =
                    _engagedPrepByIdent.TryGetValue(geomIdent, out PreparationAsk? engaged)
                    && engaged.Abort.IsCancellationRequested
                    && ReferenceEquals(extant, engaged.Completion.Task);
                if (!canceledEngagedGen)
                    return extant;
            }
            _prepTasks.TryRemove(geomIdent, out _);

            var tcs = new TaskCompletionSource<HarvestedMesh?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;
            if (IsDisposed)
            {
                tcs.TrySetCanceled();
                return task;
            }
            CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(token);
            _prepTasks[geomIdent] = task;
            PreparationAsk req = new PreparationAsk(
                BakedAssetRequest.EnvCellGeometry(
                    srcChamberIdent,
                    geomIdent,
                    surroundingsIdent,
                    chamberStructure,
                    canvases),
                postponedStashedBlob,
                tcs,
                abort);
            _queuedReqByIdent.Add(geomIdent, _queuedReqs.AddLast(req));
            BeginPrepWorkersBolted();
            return task;
        }
    }

    public Task<HarvestedMesh?> ReadyTriMeshBlobAsync(ulong ident, bool isRig, CancellationToken token = default)
    {
        if (IsDisposed || HasRasterizeBlob(ident)) return Task.FromResult<HarvestedMesh?>(null);

        lock (_queuedReqs)
            _terminalPrepMisses.Remove(ident);

        HarvestedMesh? postponedStashedBlob = null;
        if (_cpuTriMeshStash.TryFetchAndJuncturePossessed(
                ident,
                _linedTriMeshBlob,
                _ownership,
                out HarvestedMesh? stashedBlob,
                out TriMeshJunctureOutcome stashJuncture))
        {
            if (stashJuncture != TriMeshJunctureOutcome.HighWater)
                return Task.FromResult(stashedBlob);
            postponedStashedBlob = stashedBlob;
        }

        lock (_queuedReqs)
        {
            if (_prepTasks.TryGetValue(ident, out Task<HarvestedMesh?>? extant)
                && !extant.IsFaulted
                && !extant.IsCanceled)
            {
                bool canceledEngagedGen =
                    _engagedPrepByIdent.TryGetValue(ident, out PreparationAsk? engaged)
                    && engaged.Abort.IsCancellationRequested
                    && ReferenceEquals(extant, engaged.Completion.Task);
                if (!canceledEngagedGen)
                    return extant;
            }
            _prepTasks.TryRemove(ident, out _);

            var tcs = new TaskCompletionSource<HarvestedMesh?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var task = tcs.Task;
            if (IsDisposed)
            {
                tcs.TrySetCanceled();
                return task;
            }
            CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(token);
            _prepTasks[ident] = task;
            EnvCellGeomAsk? environChamber = _environChamberDescriptors.TryGetValue(ident, out EnvCellGeomAsk descriptor)
                ? descriptor
                : null;
            if (environChamber is null && ident > uint.MaxValue)
            {
                var confused = new InvalidOperationException(
                    $"Packed EnvCell geometry id 0x{ident:X16} reached the "
                    + "Setup/GfxObj preparation arm without a registered "
                    + "descriptor. Callers must route packed ids "
                    + "through PrepareEnvCellGeomMeshDataAsync or treat "
                    + "them as not-ready until the scheduler registers "
                    + "them.");
                tcs.TrySetException(confused);
                _terminalPrepMisses.Add(ident);
                return task;
            }
            BakedAssetRequest asset = environChamber is { } environ
                ? BakedAssetRequest.EnvCellGeometry(
                    environ.SrcChamberIdent,
                    ident,
                    environ.EnvironmentId,
                    environ.CellStructure,
                    environ.Surfaces)
                : isRig
                    ? BakedAssetRequest.Setup(checked((uint)ident))
                    : BakedAssetRequest.GfxObj(checked((uint)ident));
            PreparationAsk req = new PreparationAsk(
                asset,
                postponedStashedBlob,
                tcs,
                abort);
            _queuedReqByIdent.Add(ident, _queuedReqs.AddLast(req));
            BeginPrepWorkersBolted();
            return task;
        }
    }

    public HarvestedMesh? ReadyTriMeshBlob(ulong ident, bool isRig, CancellationToken token = default)
    {
        BakedAssetRequest req = isRig
            ? BakedAssetRequest.Setup(checked((uint)ident))
            : BakedAssetRequest.GfxObj(checked((uint)ident));
        return _preparedAssets.Read(req, token).Data;
    }
}
