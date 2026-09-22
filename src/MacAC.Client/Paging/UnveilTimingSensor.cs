using System.Diagnostics;
using MacAC.Client.Graphics;
using MacAC.Sim;

namespace MacAC.Client.Paging;

internal sealed class UnveilTimingSensor(
    Func<int>? fetchedLbTally,
    IRenderFrameResourceTelemetrySource? rasterizeAssetList = null)
{
    private readonly Func<int>? _fetchedLbTally = fetchedLbTally;
    private readonly IRenderFrameResourceTelemetrySource? _rasterizeAssetList = rasterizeAssetList;
    private readonly Stopwatch _clock = new();
    private long _gen;
    private string _sort = "";
    private int _paneLbs;
    private bool _rasterize;
    private bool _composites;
    private bool _impact;
    private bool _latchPrimed;
    private bool _materialized;
    private bool _summarized;
    private long _rasterizeMsec = -1;
    private long _compositesMsec = -1;
    private long _impactMsec = -1;
    private long _latchPrimedMsec = -1;
    private long _materializedMsec = -1;
    private long _previousHeadwayMsec;
    private int _cyclesSinceHeadway;

    public void Begin(
        string sort,
        long gen,
        uint destChamber,
        in PagingRevealWindow pane)
    {
        _gen = gen;
        _sort = sort;
        int flank = pane.FarRadius * 2 + 1;
        _paneLbs = flank * flank;
        _rasterize = false;
        _composites = false;
        _impact = false;
        _latchPrimed = false;
        _materialized = false;
        _summarized = false;
        _rasterizeMsec = -1;
        _compositesMsec = -1;
        _impactMsec = -1;
        _latchPrimedMsec = -1;
        _materializedMsec = -1;
        _previousHeadwayMsec = 0;
        _clock.Restart();
        Console.WriteLine(
            $"[reveal-timing] event=begin kind={sort} gen={gen} "
            + $"cell=0x{destChamber:X8} window={pane.NearRadius}/"
            + $"{pane.FarRadius} landblocks={_paneLbs} "
            + $"loaded={_fetchedLbTally?.Invoke() ?? -1}");
        WriteAssetCapture("begin", passedMillis: 0);
    }

    public void Observe(
        in RealmRevealReadinessCapture readiness,
        in SimPortalCapture gateway)
    {
        if (_gen is 0 || gateway.Generation != _gen)
            return;

        ++_cyclesSinceHeadway;
        long passed = _clock.ElapsedMilliseconds;
        string? assetCheckpoint = null;
        if (!_rasterize && readiness.IsRenderNeighborhoodReady)
        {
            _rasterize = true;
            _rasterizeMsec = passed;
            Edge("render-ready", passed);
            assetCheckpoint = AffixCheckpoint(
                assetCheckpoint,
                "render-ready");
        }
        if (!_composites && readiness.AreCompositeTexturesReady)
        {
            _composites = true;
            _compositesMsec = passed;
            Edge("composites-ready", passed);
            assetCheckpoint = AffixCheckpoint(
                assetCheckpoint,
                "composites-ready");
        }
        if (!_impact && readiness.IsCollisionReady)
        {
            _impact = true;
            _impactMsec = passed;
            Edge("collision-ready", passed);
            assetCheckpoint = AffixCheckpoint(
                assetCheckpoint,
                "collision-ready");
        }
        if (!_latchPrimed && readiness.IsPrimed)
        {
            _latchPrimed = true;
            _latchPrimedMsec = passed;
            Edge("gate-ready", passed);
            assetCheckpoint = AffixCheckpoint(
                assetCheckpoint,
                "gate-ready");
        }
        if (!_materialized && gateway.Materialized)
        {
            _materialized = true;
            _materializedMsec = passed;
            Edge("materialized", passed);
            assetCheckpoint = AffixCheckpoint(
                assetCheckpoint,
                "materialized");
        }

        if (assetCheckpoint is not null)
            WriteAssetCapture(assetCheckpoint, passed);

        if (!_summarized && gateway.WorldViewportObserved)
        {
            _summarized = true;
            Console.WriteLine(
                $"[reveal-timing] SUMMARY kind={_sort} gen={_gen} "
                + $"totalMs={passed} renderMs={_rasterizeMsec} "
                + $"compositesMs={_compositesMsec} collisionMs={_impactMsec} "
                + $"gateReadyMs={_latchPrimedMsec} "
                + $"materializedMs={_materializedMsec} "
                + $"landblocks={_paneLbs}");
            WriteAssetCapture("summary", passed);
            return;
        }

        if (!_summarized && passed - _previousHeadwayMsec >= 1000)
        {
            _previousHeadwayMsec = passed;
            Console.WriteLine(
                $"[reveal-timing] elapsedMs={passed} "
                + $"render={(_rasterize ? 1 : 0)} "
                + $"composites={(_composites ? 1 : 0)} "
                + $"collision={(_impact ? 1 : 0)} "
                + $"loaded={_fetchedLbTally?.Invoke() ?? -1}"
                + $"/{_paneLbs} "
                + $"frames={_cyclesSinceHeadway}");
            WriteAssetCapture("progress", passed);
            BulletinTimingSensor.WritePagingBeatPane();
            _cyclesSinceHeadway = 0;
        }
    }

    internal static string ComposeAssetStroke(
        string checkpoint,
        string sort,
        long gen,
        long passedMillis,
        in RenderFrameResourceTelemetryCapture capture)
    {
        var triMesh = capture.Mesh;
        var textures = capture.Textures;
        var proc = capture.Process;
        return $"[reveal-resource] checkpoint={checkpoint} kind={sort} "
            + $"gen={gen} elapsedMs={passedMillis} "
            + $"meshData={triMesh.RenderData} meshAtlases={triMesh.AtlasArrays} "
            + $"meshUnusedLru={triMesh.UnusedLru} meshBytes={triMesh.EstimatedBytes} "
            + $"globalUploads={triMesh.GlobalUploadCount} "
            + $"globalUploadBytes={triMesh.GlobalUploadedBytes} "
            + $"frameUploads={triMesh.FrameUploadCount} "
            + $"frameUploadBytes={triMesh.FrameUploadBytes} "
            + $"frameArrayBytes={triMesh.FrameArrayAllocationBytes} "
            + $"frameMipmapBytes={triMesh.FrameMipmapBytes} "
            + $"frameBufferUploadBytes={triMesh.FrameBufferUploadBytes} "
            + $"frameBufferAllocationBytes={triMesh.FrameBufferAllocationBytes} "
            + $"frameBufferCopyBytes={triMesh.FrameBufferCopyBytes} "
            + $"frameNewArrays={triMesh.FrameNewArrayCount} "
            + $"frameNewBuffers={triMesh.FrameNewBufferCount} "
            + $"frameStaleDiscards={triMesh.FrameStaleDiscardCount} "
            + $"frameMipmapArrays={triMesh.FrameMipmapArrayCount} "
            + $"staged={triMesh.StagedUploadBacklog} "
            + $"stagedBytes={triMesh.StagedUploadBytes} "
            + $"stagingHighWater={(triMesh.StagingAtHighWater ? 1 : 0)} "
            + $"cpuMeshCache={triMesh.CpuMeshCacheCount} "
            + $"cpuMeshCacheBytes={triMesh.CpuMeshCacheBytes} "
            + $"arenaCapacityBytes={triMesh.GlobalCapacityBytes} "
            + $"arenaPhysicalBytes={triMesh.GlobalPhysicalCapacityBytes} "
            + $"arenaMigrating={(triMesh.GlobalMigrationInProgress ? 1 : 0)} "
            + $"prepared={triMesh.PreparedProbes}/{triMesh.PreparedReads}/"
            + $"{triMesh.PreparedLoaded}/{triMesh.PreparedMissing}/"
            + $"{triMesh.PreparedCorrupt} "
            + $"ownedTextures={textures.OwnedBindlessTextures} "
            + $"textureOwners={textures.TextureOwners} "
            + $"composites={textures.CachedCompositeTextures} "
            + $"unownedComposites={textures.CachedUnownedComposites} "
            + $"unownedCompositeBytes={textures.CachedUnownedCompositeBytes} "
            + $"compositeAtlases={textures.CompositeAtlases} "
            + $"compositeAtlasBytes={textures.CompositeAtlasBytes} "
            + $"compositePending={textures.CompositeWarmupPending} "
            + $"frameCompositeUploads={textures.FrameCompositeUploadCount} "
            + $"frameCompositeUploadBytes={textures.FrameCompositeUploadBytes} "
            + $"managedBytes={proc.ManagedBytes} "
            + $"managedCommittedBytes={proc.ManagedCommittedBytes} "
            + $"trackedGpuBytes={proc.TrackedGpuBytes} "
            + $"trackedGpuBuffers={proc.TrackedGpuBuffers} "
            + $"trackedGpuTextures={proc.TrackedGpuTextures}";
    }

    private void Edge(string label, long passed)
    {
        Console.WriteLine(
            $"[reveal-timing] event={label} kind={_sort} gen={_gen} "
            + $"elapsedMs={passed} "
            + $"loaded={_fetchedLbTally?.Invoke() ?? -1}"
            + $"/{_paneLbs}");
    }

    private void WriteAssetCapture(string checkpoint, long passedMillis)
    {
        if (_rasterizeAssetList is null)
            return;

        var capture =
            _rasterizeAssetList.Capture();
        Console.WriteLine(ComposeAssetStroke(
            checkpoint,
            _sort,
            _gen,
            passedMillis,
            capture));
    }

    private static string AffixCheckpoint(string? latest, string upcoming) =>
        latest is null ? upcoming : latest + "+" + upcoming;
}
