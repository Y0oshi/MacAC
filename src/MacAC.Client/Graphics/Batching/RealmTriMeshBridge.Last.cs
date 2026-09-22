using MacAC.Assets;
using MacAC.Client.Graphics.Tenancy;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmTriMeshBridge
{
    internal int PreviousPushTally { get; private set; }

    internal long PreviousPushOctets { get; private set; }

    internal int PreviousStaleTossTally { get; private set; }

    internal long PreviousArrAllocOctets { get; private set; }

    internal long PreviousPlannedMipmapOctets { get; private set; }

    internal int PreviousNewArrTally { get; private set; }

    internal long PreviousBufPushOctets { get; private set; }

    internal long PreviousBufAllocOctets { get; private set; }

    internal long PreviousBufDuplicateOctets { get; private set; }

    internal int PreviousNewBufTally { get; private set; }

    internal int PreviousMipmapArrTally { get; private set; }

    internal long PreviousMipmapOctets { get; private set; }

    internal int LinedPushBacklog => _meshManager?.LinedTriMeshTally ?? 0;

    internal long LinedPushOctets => _meshManager?.LinedTriMeshOctets ?? 0;

    internal bool LoadingAtHiWater => _meshManager?.LoadingAtHiWater ?? false;

    internal (int Count, long Bytes) CpuTriMeshStashTelemetry =>
        _meshManager?.CpuStashTelemetry ?? default;

    public bool IsRasterizeBlobPrimed(ulong ident)
    {
        return _isUninitialized || (_meshManager?.SecureRasterizeBlobPrimed(ident) ?? false);
    }

    public static RealmTriMeshBridge BuildUninitialized() => new();
    public ThingRasterizeBlob? ObtainRasterizeBlob(ulong ident)
    {
        return _isUninitialized || _meshManager is null ? null : _meshManager.FetchRasterizeBlob(ident);
    }

    public ThingRasterizeBlob? TryFetchRenderData(ulong ident)
    {
        return _isUninitialized || _meshManager is null ? null : _meshManager.TryFetchRasterizeBlob(ident);
    }

    public void IncrementRefTally(ulong ident)
    {
        if (_isUninitialized || _meshManager is null) return;
        _meshManager.IncrementRefCount(ident);

        try
        {
            if (_meshManager.TryFetchRasterizeBlob(ident) is null)
                _meshManager.ReadyTriMeshBlobAsync(ident, isRig: false);
        }
        catch (Exception obtainMiss)
        {
            try
            {
                _meshManager.DecrementRefCount(ident);
            }
            catch (Exception undoMiss)
            {
                throw new TriMeshRefAlterationFault(
                    $"Mesh 0x{ident:X10} reference acquisition failed and its committed increment could not be rolled back.",
                    alterationSealed: true,
                    new AggregateException(obtainMiss, undoMiss));
            }

            throw;
        }
    }

    public void DecrementRefTally(ulong ident)
    {
        if (_isUninitialized || _meshManager is null) return;
        _meshManager.DecrementRefCount(ident);
    }

    public void PinReadiedRasterizeBlob(ulong ident)
    {
        if (_isUninitialized || _meshManager is null) return;
        _meshManager.IncrementRefCount(ident);
    }

    private readonly ThingTriMeshKeeper? _meshManager;

    public ThingTriMeshKeeper? TriMeshKeeper => _meshManager;

    public void SecureFetched(ulong ident)
    {
        if (_isUninitialized || _meshManager is null) return;
        _meshManager.ReadyTriMeshBlobAsync(ident, isRig: false);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;

        _teardown ??= new MacAC.Client.Graphics.SequencedAssetTeardown(
            () =>
            {
                if (_assetSunset is MacAC.Client.Graphics.Gpu.Vulkan.VkFrameFlightDriver cycleFlights)
                    cycleFlights.PauseForSubmittedJob();
            },
            () => _meshManager?.Dispose(),
            () => _possessedReadiedHoldings?.Dispose(),
            () => EmptyVisualsFifo("publishing mesh resource retirements"),
            () =>
            {
                if (_assetSunset is MacAC.Client.Graphics.Gpu.Vulkan.VkFrameFlightDriver cycleFlights)
                    cycleFlights.PauseForSubmittedJob();
            },
            () => EmptyVisualsFifo("releasing retired mesh resources"),
            () => _visualsDev?.Dispose(),
            () => EmptyVisualsFifo("deleting mesh graphics-device resources"));

        _teardown.Advance();
        _destroyed = _teardown.IsComplete;
    }

    internal bool IsCoreConcealedMarker(uint gfxObjRefIdent) =>
        _coreConcealedMarker(gfxObjRefIdent);

    internal void AssignDestUnveilPushPrecedence(bool turnedOn) =>
        _destUnveilPushPrecedence = turnedOn;

    internal static RealmTriMeshBridge BuildWithOnlineDatReadiedHoldings(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        IDatAccess datFiles,
        ILogger<RealmTriMeshBridge> logger,
        MacAC.Client.Graphics.IGpuAssetSunsetFifo assetSunset)
    {
        return new(
            gpuDev,
            datFiles,
            readiedHoldings: null,
            logger,
            assetSunset,
            ownsReadiedHoldings: true,
            TenancyAllowanceKnobs.Default);
    }

    internal void EnrollResidencySrcs(TenancyKeeper keeper)
    {
        ArgumentNullException.ThrowIfNull(keeper);
        ThingTriMeshKeeper triMeshKeeper = _meshManager
            ?? throw new InvalidOperationException(
                "An initialized mesh adapter is needed for residency diagnostics");
        keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
            TenancyDomain.ObjectMeshes,
            triMeshKeeper.GrabObjectTriMeshResidency));
        EnrollResidencySrcsRest(keeper, triMeshKeeper);
    }

    private void EnrollResidencySrcsRest(TenancyKeeper keeper, ThingTriMeshKeeper triMeshKeeper)
    {
        keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                TenancyDomain.PreparedMeshCpu,
                triMeshKeeper.GrabReadiedTriMeshResidency));
        keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                    TenancyDomain.MeshStaging,
                    triMeshKeeper.GrabLoadingResidency));
        keeper.EnrollDomainSrc(new DelegateTenancyDomainSource(
                    TenancyDomain.GlobalMeshArena,
                    triMeshKeeper.GrabGlobalArenaResidency));
    }

    private static MeshUploadFrameAllowance BuildPushAllowance(
        int ceilingObjects)
    {
        return new(new MeshUploadAllowanceLimits(
            ceilingObjects,
            CeilingPushOctetsPerCycle,
            CeilingArrAllocOctetsPerCycle,
            CeilingMipmapOctetsPerCycle,
            CeilingNewArrsPerCycle,
            CeilingBufPushOctetsPerCycle,
            CeilingBufAllocOctetsPerCycle,
            CeilingBufDuplicateOctetsPerCycle,
            CeilingNewBufsPerCycle,
            CeilingSinglePushOctets,
            CeilingSingleArrAllocOctets,
            CeilingSingleMipmapOctets,
            CeilingSingleNewArrs,
            CeilingSingleBufPushOctets));
    }

    private static void RejectUnsupportedFront(
        ThingTriMeshKeeper triMeshKeeper,
        MeshUploadQueueGear anticipated,
        NotSupportedException problem)
    {
        if (!triMeshKeeper.TryDequeueLinedTriMeshBlob(out MeshUploadQueueGear rejected)
            || rejected.Generation != anticipated.Generation)
        {
            throw new InvalidOperationException(
                "The staged mesh FIFO head changed while rejecting an not supported generation",
                problem);
        }
        triMeshKeeper.RejectUnsupportedLinedPush(rejected, problem);
    }

    private void EmptyVisualsFifo(string op)
    {
        if (_visualsDev is null)
            return;

        _visualsDev.ProcessQueue();
        if (_visualsDev.HasPendingWork)
        {
            throw new InvalidOperationException(
                $"OpenGL work remains pending after {op}; retry adapter disposal to continue the exact stage");
        }
    }
}
