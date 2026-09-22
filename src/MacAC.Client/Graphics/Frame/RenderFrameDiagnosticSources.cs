using MacAC.Assets;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics;

internal sealed class EngineRenderFrameTitleFactsSource(
    GpuRealmPhase world,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animations,
    WorldClock time) : IRasterizeCycleBannerFactsOrigin
{
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _anims = animations ?? throw new ArgumentNullException(nameof(animations));
    private readonly WorldClock _moment = time ?? throw new ArgumentNullException(nameof(time));

    public RasterizeCycleBannerFacts Capture()
    {
        return new(
        _world.Entities.Count,
        _anims.Count,
        _moment.LatestCalendar,
        _moment.DayFraction);
    }
}

internal sealed class SilkRasterizeCycleBannerDrain(IWindow window) : IRasterizeCycleBannerDrain
{
    private readonly IWindow _window = window ?? throw new ArgumentNullException(nameof(window));

    public void SetTitle(string banner) => _window.Title = banner;
}

internal sealed class EngineRenderFrameResourceTelemetrySource(
    MoteSys? motes,
    ParticleHookTap? moteMappings,
    RealmPaintRouter? realmRouter,
    Batching.EnvironChamberPainter? surroundingsChambers,
    MotePainter? motePainter,
    PhrasePainter? widgetPhrasePainter,
    GatewayZDepthBitmaskPainter? gatewayZDepthBitmask,
    ClipCycle? clipCycle,
    LandModernPainter? land,
    StageLightingUboWiring? illumination,
    RealmTriMeshBridge? triMeshes,
    BitmapStash? textures,
    IBakedAssetSource? readiedHoldings) :
    IRenderFrameResourceTelemetrySource
{
    private readonly MoteSys? _motes = motes;
    private readonly ParticleHookTap? _moteMappings = moteMappings;
    private readonly RealmPaintRouter? _realmRouter = realmRouter;
    private readonly Batching.EnvironChamberPainter? _surroundingsChambers = surroundingsChambers;
    private readonly MotePainter? _motePainter = motePainter;
    private readonly PhrasePainter? _widgetPhrasePainter = widgetPhrasePainter;
    private readonly GatewayZDepthBitmaskPainter? _portalDepthMask = gatewayZDepthBitmask;
    private readonly ClipCycle? _clipCycle = clipCycle;
    private readonly LandModernPainter? _land = land;
    private readonly StageLightingUboWiring? _illumination = illumination;
    private readonly RealmTriMeshBridge? _triMeshes = triMeshes;
    private readonly BitmapStash? _textures = textures;
    private readonly IBakedAssetSource? _preparedAssets = readiedHoldings;

    public RenderFrameResourceTelemetryCapture Capture()
    {
        (int moteSets, long moteOctets) =
            _motePainter?.DynamicBufTelemetry ?? default;
        ThingTriMeshKeeper? triMeshKeeper = _triMeshes?.TriMeshKeeper;
        var triMesh = triMeshKeeper?.Diagnostics ?? default;
        GlobalTriMeshBuffer? globalTriMesh = triMeshKeeper?.GlobalBuf;
        var cpuTriMesh = _triMeshes?.CpuTriMeshStashTelemetry ?? default;
        BakedAssetSourceStats readied =
            _preparedAssets?.Stats ?? default;
        var info = GC.GetGCMemoryInfo();

        return new RenderFrameResourceTelemetryCapture(
            new VfxStreamResourceTelemetry(
                _motes?.EngagedSpoutTally ?? 0,
                _motes?.EngagedMoteTally ?? 0,
                _moteMappings?.EngagedMappingTally ?? 0),
            new DynamicBufferResourceTelemetry(
                _realmRouter?.DynamicBufSetTally ?? 0,
                _surroundingsChambers?.DynamicBufSetTally ?? 0,
                moteSets,
                moteOctets,
                _widgetPhrasePainter?.DynamicBufCapOctets ?? 0,
                _portalDepthMask?.DynamicBufCapOctets ?? 0,
                _clipCycle?.DynamicBufSetTally ?? 0,
                _land?.DynamicIndirectBufTally ?? 0,
                _illumination?.DynamicBufTally ?? 0),
            new MeshStreamResourceTelemetry(
                triMesh.RenderData,
                triMesh.AtlasArrays,
                triMesh.UnusedLru,
                triMesh.EstimatedBytes,
                globalTriMesh?.PushTally ?? 0,
                globalTriMesh?.UploadedOctets ?? 0,
                _triMeshes?.PreviousPushTally ?? 0,
                _triMeshes?.PreviousPushOctets ?? 0,
                _triMeshes?.PreviousArrAllocOctets ?? 0,
                _triMeshes?.PreviousPlannedMipmapOctets ?? 0,
                _triMeshes?.PreviousNewArrTally ?? 0,
                _triMeshes?.PreviousBufPushOctets ?? 0,
                _triMeshes?.PreviousBufAllocOctets ?? 0,
                _triMeshes?.PreviousBufDuplicateOctets ?? 0,
                _triMeshes?.PreviousNewBufTally ?? 0,
                _triMeshes?.LinedPushBacklog ?? 0,
                _triMeshes?.LinedPushOctets ?? 0,
                _triMeshes?.LoadingAtHiWater ?? false,
                _triMeshes?.PreviousStaleTossTally ?? 0,
                cpuTriMesh.Count,
                cpuTriMesh.Bytes,
                _triMeshes?.PreviousMipmapArrTally ?? 0,
                _triMeshes?.PreviousMipmapOctets ?? 0,
                globalTriMesh?.CapOctets ?? 0,
                globalTriMesh?.PhysicalCapOctets ?? 0,
                globalTriMesh?.IsMigrationInHeadway ?? false,
                readied.Probes,
                readied.Reads,
                readied.Loaded,
                readied.Missing,
                readied.Corrupt),
            new TextureStreamResourceTelemetry(
                _textures?.PossessedBindlessTextureTally ?? 0,
                _textures?.TextureHolderTally ?? 0,
                _textures?.EngagedMoteTextureTally ?? 0,
                _textures?.MoteTextureHolderTally ?? 0,
                _textures?.StashedMoteTextureTally ?? 0,
                _textures?.StashedUnownedMoteTextureTally ?? 0,
                _textures?.StashedUnownedMoteTextureOctets ?? 0,
                _textures?.StashedCompoundTextureTally ?? 0,
                _textures?.StashedUnownedCompoundTally ?? 0,
                _textures?.StashedUnownedCompoundOctets ?? 0,
                _textures?.CompoundTilesetTally ?? 0,
                _textures?.CompoundTilesetOctets ?? 0,
                _textures?.CompoundCyclePushTally ?? 0,
                _textures?.CompoundCyclePushOctets ?? 0,
                _realmRouter?.PreviousCompoundWarmupQueuedTally ?? 0),
            new ProcessResourceTelemetry(
                GC.GetTotalMemory(false),
                info.TotalCommittedBytes,
                GpuMemoryLedger.AllocatedBytes,
                GpuMemoryLedger.BufTally,
                GpuMemoryLedger.TextureTally));
    }
}
