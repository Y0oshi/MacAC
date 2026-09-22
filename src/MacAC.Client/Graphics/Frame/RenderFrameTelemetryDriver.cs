using MacAC.Client.Graphics.Packs;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal readonly record struct RenderFrameTelemetryCapture(
    double Fps,
    double FrameMilliseconds,
    int VisibleLandblocks,
    int TotalLandblocks,
    int EntityCount,
    int AnimatedEntityCount)
{
    public static RenderFrameTelemetryCapture Initial
    {
        get
        {
            return new(
        Fps: 60.0,
        FrameMilliseconds: 16.7,
        VisibleLandblocks: 0,
        TotalLandblocks: 0,
        EntityCount: 0,
        AnimatedEntityCount: 0);
        }
    }
}

internal readonly record struct RasterizeCycleBannerFacts(
    int EntityCount,
    int AnimatedEntityCount,
    DerethDateMoment.Almanac Calendar,
    double DayFraction);

internal interface IRasterizeCycleBannerFactsOrigin
{
    RasterizeCycleBannerFacts Capture();
}

internal interface IRasterizeCycleBannerDrain
{
    void SetTitle(string banner);
}

internal interface IRenderFrameTelemetryLog
{
    void WriteLine(string msg);
}

internal sealed class ConsoleRenderFrameTelemetryLog : IRenderFrameTelemetryLog
{
    public void WriteLine(string msg) => Console.WriteLine(msg);
}

internal interface IRenderFrameResourceTelemetrySource
{
    RenderFrameResourceTelemetryCapture Capture();
}

internal readonly record struct VfxStreamResourceTelemetry(
    int ActiveEmitters,
    int ActiveParticles,
    int ActiveBindings);

internal readonly record struct DynamicBufferResourceTelemetry(
    int WorldBufferSets,
    int EnvCellBufferSets,
    int ParticleBufferSets,
    long ParticleBufferBytes,
    long UiTextBufferBytes,
    long PortalDepthMaskBufferBytes,
    int ClipBufferSets,
    int TerrainIndirectBuffers,
    int SceneLightingBuffers);

internal readonly record struct MeshStreamResourceTelemetry(
    int RenderData,
    int AtlasArrays,
    int UnusedLru,
    long EstimatedBytes,
    long GlobalUploadCount,
    long GlobalUploadedBytes,
    int FrameUploadCount,
    long FrameUploadBytes,
    long FrameArrayAllocationBytes,
    long FramePlannedMipmapBytes,
    int FrameNewArrayCount,
    long FrameBufferUploadBytes,
    long FrameBufferAllocationBytes,
    long FrameBufferCopyBytes,
    int FrameNewBufferCount,
    int StagedUploadBacklog,
    long StagedUploadBytes,
    bool StagingAtHighWater,
    int FrameStaleDiscardCount,
    int CpuMeshCacheCount,
    long CpuMeshCacheBytes,
    int FrameMipmapArrayCount,
    long FrameMipmapBytes,
    long GlobalCapacityBytes,
    long GlobalPhysicalCapacityBytes,
    bool GlobalMigrationInProgress,
    long PreparedProbes,
    long PreparedReads,
    long PreparedLoaded,
    long PreparedMissing,
    long PreparedCorrupt);

internal readonly record struct TextureStreamResourceTelemetry(
    int OwnedBindlessTextures,
    int TextureOwners,
    int ActiveParticleTextures,
    int ParticleTextureOwners,
    int CachedParticleTextures,
    int CachedUnownedParticleTextures,
    long CachedUnownedParticleTextureBytes,
    int CachedCompositeTextures,
    int CachedUnownedComposites,
    long CachedUnownedCompositeBytes,
    int CompositeAtlases,
    long CompositeAtlasBytes,
    int FrameCompositeUploadCount,
    long FrameCompositeUploadBytes,
    int CompositeWarmupPending);

internal readonly record struct ProcessResourceTelemetry(
    long ManagedBytes,
    long ManagedCommittedBytes,
    long TrackedGpuBytes,
    int TrackedGpuBuffers,
    int TrackedGpuTextures);

internal readonly record struct RenderFrameResourceTelemetryCapture(
    VfxStreamResourceTelemetry Vfx,
    DynamicBufferResourceTelemetry DynamicBuffers,
    MeshStreamResourceTelemetry Mesh,
    TextureStreamResourceTelemetry Textures,
    ProcessResourceTelemetry Process);

internal sealed class RenderFrameTelemetryDriver(
    IRasterizeCycleBannerFactsOrigin titleFacts,
    IRasterizeCycleBannerDrain titleSink,
    IRenderFrameTelemetryLog log,
    bool broadcastAssetTelemetry,
    IRenderFrameResourceTelemetrySource? resources = null,
    IRenderPackTelemetryCaptureSource? rasterizeBundle = null) :
    IRenderFrameTelemetryPhase,
    IRenderFrameTelemetryCaptureSource
{
    internal const double BulletinIntervalSecs = 0.5;

    private readonly IRasterizeCycleBannerFactsOrigin _bannerFacts = titleFacts ?? throw new ArgumentNullException(nameof(titleFacts));
    private readonly IRasterizeCycleBannerDrain _bannerDrain = titleSink ?? throw new ArgumentNullException(nameof(titleSink));
    private readonly IRenderFrameResourceTelemetrySource? _assetList = broadcastAssetTelemetry
            ? resources ?? throw new ArgumentNullException(nameof(resources))
            : resources;
    private readonly IRenderFrameTelemetryLog _trace = log ?? throw new ArgumentNullException(nameof(log));
    private readonly bool _broadcastAssetTelemetry = broadcastAssetTelemetry;
    private readonly IRenderPackTelemetryCaptureSource? _rasterizeBundle = rasterizeBundle;

    private double _elapsedSeconds;
    private int _cycleTally;

    public RenderFrameTelemetryCapture Capture { get; private set; } =
        RenderFrameTelemetryCapture.Initial;

    public void Publish(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
        _elapsedSeconds += feed.DeltaSeconds;
        ++_cycleTally;
        if (_elapsedSeconds < BulletinIntervalSecs)
            return;

        double averageCycleMillis = _elapsedSeconds / _cycleTally * 1000.0;
        double fps = _cycleTally / _elapsedSeconds;
        var facts = _bannerFacts.Capture();

        _bannerDrain.SetTitle(ComposeBanner(
            fps,
            averageCycleMillis,
            verdict.World.VisibleLandblocks,
            verdict.World.TotalLandblocks,
            facts));

        if (_broadcastAssetTelemetry)
        {
            var assetList = _assetList!.Capture();
            _trace.WriteLine(ComposeGpuFlow(assetList));
            if (_rasterizeBundle is not null)
            {
                _trace.WriteLine(RenderPackTelemetryComposer.Format(
                    _rasterizeBundle.SnapTelemetry()));
            }
        }

        Capture = new RenderFrameTelemetryCapture(
            fps,
            averageCycleMillis,
            verdict.World.VisibleLandblocks,
            verdict.World.TotalLandblocks,
            facts.EntityCount,
            facts.AnimatedEntityCount);
        _elapsedSeconds = 0;
        _cycleTally = 0;
    }

    internal static string ComposeBanner(
        double fps,
        double averageCycleMillis,
        int shownLbs,
        int sumLbs,
        RasterizeCycleBannerFacts facts) =>
        $"MacAC  ·  {fps:F0} fps  ·  {averageCycleMillis:F1} ms";

    internal static string ComposeGpuFlow(RenderFrameResourceTelemetryCapture capture)
    {
        var vfx = capture.Vfx;
        var bufs = capture.DynamicBuffers;
        var triMesh = capture.Mesh;
        var textures = capture.Textures;
        var proc = capture.Process;
        return
            $"[gpu-stream] emit={vfx.ActiveEmitters} "
            + $"particles={vfx.ActiveParticles} "
            + $"bindings={vfx.ActiveBindings} "
            + $"wbSets={bufs.WorldBufferSets} "
            + $"cellSets={bufs.EnvCellBufferSets} "
            + $"particleSets={bufs.ParticleBufferSets}/{bufs.ParticleBufferBytes}B "
            + $"uiBytes={bufs.UiTextBufferBytes} "
            + $"portalBytes={bufs.PortalDepthMaskBufferBytes} "
            + $"clipSets={bufs.ClipBufferSets} "
            + $"terrainBuffers={bufs.TerrainIndirectBuffers} "
            + $"lightBuffers={bufs.SceneLightingBuffers} "
            + $"mesh={triMesh.RenderData}/{triMesh.AtlasArrays}/{triMesh.UnusedLru} "
            + $"meshEst={triMesh.EstimatedBytes} "
            + $"meshUpload={triMesh.GlobalUploadCount}/{triMesh.GlobalUploadedBytes} "
            + $"uploadFrame={triMesh.FrameUploadCount}/{triMesh.FrameUploadBytes}/"
            + $"{triMesh.FrameArrayAllocationBytes}/{triMesh.FramePlannedMipmapBytes}/"
            + $"{triMesh.FrameNewArrayCount} "
            + $"bufferFrame={triMesh.FrameBufferUploadBytes}/{triMesh.FrameBufferAllocationBytes}/"
            + $"{triMesh.FrameBufferCopyBytes}/{triMesh.FrameNewBufferCount} "
            + $"staging={triMesh.StagedUploadBacklog}/{triMesh.StagedUploadBytes}/"
            + $"{triMesh.StagingAtHighWater}/{triMesh.FrameStaleDiscardCount} "
            + $"cpuMesh={triMesh.CpuMeshCacheCount}/{triMesh.CpuMeshCacheBytes} "
            + $"mipmapFrame={triMesh.FrameMipmapArrayCount}/{triMesh.FrameMipmapBytes} "
            + $"meshCap={triMesh.GlobalCapacityBytes} "
            + $"meshPhys={triMesh.GlobalPhysicalCapacityBytes}/{triMesh.GlobalMigrationInProgress} "
            + $"prepared={triMesh.PreparedProbes}/{triMesh.PreparedReads}/"
            + $"{triMesh.PreparedLoaded}/{triMesh.PreparedMissing}/{triMesh.PreparedCorrupt} "
            + $"gpuTrack={proc.TrackedGpuBytes}/{proc.TrackedGpuBuffers}/"
            + $"{proc.TrackedGpuTextures} "
            + $"managed={proc.ManagedBytes}/{proc.ManagedCommittedBytes} "
            + $"ownedTextures={textures.OwnedBindlessTextures}/{textures.TextureOwners} "
            + $"particleTextures={textures.ActiveParticleTextures}/"
            + $"{textures.ParticleTextureOwners}/{textures.CachedParticleTextures}/"
            + $"{textures.CachedUnownedParticleTextures}/"
            + $"{textures.CachedUnownedParticleTextureBytes} "
            + $"compositeCache={textures.CachedCompositeTextures}/"
            + $"{textures.CachedUnownedComposites}/{textures.CachedUnownedCompositeBytes} "
            + $"compositeAtlas={textures.CompositeAtlases}/{textures.CompositeAtlasBytes} "
            + $"compositeUpload={textures.FrameCompositeUploadCount}/"
            + $"{textures.FrameCompositeUploadBytes}/{textures.CompositeWarmupPending}";
    }
}
