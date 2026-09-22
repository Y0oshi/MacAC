using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Client.Telemetry;

internal sealed class RealmLifespanResourceCaptureSource(
    GpuRealmPhase world,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animations,
    RenderFrameTelemetryDriver renderDiagnostics,
    OnlineActorCore liveEntities,
    PagingDriver streaming,
    MoteSys particles,
    ParticleHookTap particleSink,
    ActorEffectDriver effects,
    OnlineActorLightDriver lights,
    KineticScriptRunner scripts,
    RealmTriMeshBridge? triMeshes,
    BitmapStash textures,
    RealmPaintRouter? router,
    CycleProfiler frameProfiler,
    IDatAccess dats,
    TenancyKeeper residency,
    KineticAssetCache physics,
    ICurrentRenderStageOracleCaptureSource? rasterizeTableauOracle = null,
    IRenderStageShadeCaptureSource? rasterizeTableauShade = null)
{
    private const int LohGenOrdinal = 3;

    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger>
        _anims = animations
            ?? throw new ArgumentNullException(nameof(animations));
    private readonly RenderFrameTelemetryDriver _rasterizeTelemetry = renderDiagnostics
            ?? throw new ArgumentNullException(nameof(renderDiagnostics));
    private readonly OnlineActorCore _onlineActors = liveEntities
            ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly PagingDriver _paging = streaming
            ?? throw new ArgumentNullException(nameof(streaming));
    private readonly MoteSys _motes = particles
            ?? throw new ArgumentNullException(nameof(particles));
    private readonly ParticleHookTap _moteDrain = particleSink
            ?? throw new ArgumentNullException(nameof(particleSink));
    private readonly ActorEffectDriver _fxList = effects ?? throw new ArgumentNullException(nameof(effects));
    private readonly OnlineActorLightDriver _lamps = lights ?? throw new ArgumentNullException(nameof(lights));
    private readonly KineticScriptRunner _programs = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private readonly RealmTriMeshBridge? _triMeshes = triMeshes;
    private readonly BitmapStash _textures = textures ?? throw new ArgumentNullException(nameof(textures));
    private readonly RealmPaintRouter? _router = router;
    private readonly CycleProfiler _cycleProfiler = frameProfiler
            ?? throw new ArgumentNullException(nameof(frameProfiler));
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly TenancyKeeper _residency = residency
            ?? throw new ArgumentNullException(nameof(residency));
    private readonly KineticAssetCache _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly ICurrentRenderStageOracleCaptureSource?
        _rasterizeTableauOracle = rasterizeTableauOracle;
    private readonly IRenderStageShadeCaptureSource?
        _rasterizeTableauShade = rasterizeTableauShade;

    public RealmLifespanResourceCapture Capture(RenderFrameVerdict verdict)
    {
        ThingTriMeshKeeper? triMeshKeeper = _triMeshes is null
            ? null
            : _triMeshes.TriMeshKeeper
                ?? throw new InvalidOperationException(
                    "Lifecycle snapshots require the composed modern mesh manager");
        var triMesh = triMeshKeeper?.Diagnostics ?? default;
        var rasterize = _rasterizeTelemetry.Capture;
        var memory = GC.GetGCMemoryInfo();
        var generations = memory.GenerationInfo;
        long lohByteSize = generations.Length > LohGenOrdinal
            ? generations[LohGenOrdinal].SizeAfterBytes
            : 0L;
        long lohFragmentationOctets = generations.Length > LohGenOrdinal
            ? generations[LohGenOrdinal].FragmentationAfterBytes
            : 0L;

        ShelfStats datObjectStashStats = _datFiles is LiveDatCollection coreDatFiles
            ? coreDatFiles.ObjectStashStats
            : default;
        ShelfStats cpuTriMeshStashStats = triMeshKeeper?.CpuTriMeshStashStats ?? default;
        ShelfStats decodedTextureStashStats =
            triMeshKeeper?.DecodedTextureStashStats ?? default;
        var residency = _residency.SnapCapture();
        long followedGpuOctets = GpuMemoryLedger.AllocatedBytes;
        long residencyGpuOctets = residency.TotalCharges.PhysicalGpuBytes;

        return new RealmLifespanResourceCapture(
            LoadedLandblocks: _world.FetchedLbIdents.Count,
            WorldEntities: _world.Entities.Count,
            AnimatedEntities: _anims.Count,
            VisibleLandblocks: verdict.World.VisibleLandblocks,
            TotalLandblocks: verdict.World.TotalLandblocks,
            LiveEntities: _onlineActors.Count,
            MaterializedLiveEntities: _onlineActors.MaterializedTally,
            RenderSceneOracle: _rasterizeTableauOracle?.Snapshot
                ?? CurrentRenderStageOracleCapture.Disabled,
            RenderSceneShadow:
                _rasterizeTableauShade?.GrabCheckpointCapture()
                ?? RenderStageShadeComparisonCapture.Disabled,
            RenderFrameProduct: RenderFrameProductComparisonCapture.Disabled,
            PendingLiveTeardowns: _onlineActors.PendingTeardownCount,
            PendingLandblockRetirements: _paging.QueuedRetirementCount,
            ParticleEmitters: _motes.EngagedSpoutTally,
            Particles: _motes.EngagedMoteTally,
            ParticleBindings: _moteDrain.EngagedMappingTally,
            ParticleOwners: _moteDrain.FollowedHolderTally,
            EffectOwners: _fxList.PrimedHolderTally,
            LightOwners: _lamps.FollowedHolderTally,
            ScriptOwners: _programs.EngagedOwnerCount,
            ActiveScripts: _programs.EngagedProgramTally,
            MeshRenderData: triMesh.RenderData,
            MeshAtlasArrays: triMesh.AtlasArrays,
            MeshEstimatedBytes: triMesh.EstimatedBytes,
            StagedMeshUploads: _triMeshes?.LinedPushBacklog ?? 0,
            StagedMeshBytes: _triMeshes?.LinedPushOctets ?? 0,
            TrackedGpuBytes: followedGpuOctets,
            ResidencyGpuBytes: residencyGpuOctets,
            GpuTrackerMinusResidencyBytes: checked(
                followedGpuOctets - residencyGpuOctets),
            TrackedGpuBuffers: GpuMemoryLedger.BufTally,
            TrackedGpuTextures: GpuMemoryLedger.TextureTally,
            OwnedCompositeTextures: _textures.PossessedBindlessTextureTally,
            CompositeTextureOwners: _textures.TextureHolderTally,
            ActiveParticleTextures: _textures.EngagedMoteTextureTally,
            ParticleTextureOwners: _textures.MoteTextureHolderTally,
            CompositeWarmupPending:
                _router?.PreviousCompoundWarmupQueuedTally ?? 0,
            ManagedBytes: GC.GetTotalMemory(forceFullCollection: false),
            ManagedCommittedBytes: memory.TotalCommittedBytes,
            LohSizeBytes: lohByteSize,
            LohFragmentationBytes: lohFragmentationOctets,
            ProcessTotalAllocatedBytes: GC.GetTotalAllocatedBytes(precise: false),
            CpuMeshCacheHits: cpuTriMeshStashStats.Hits,
            CpuMeshCacheMisses: cpuTriMeshStashStats.Misses,
            CpuMeshCacheEvictions: cpuTriMeshStashStats.Evictions,
            DecodedTextureCacheHits: decodedTextureStashStats.Hits,
            DecodedTextureCacheMisses: decodedTextureStashStats.Misses,
            DecodedTextureCacheEvictions: decodedTextureStashStats.Evictions,
            DatObjectCacheHits: datObjectStashStats.Hits,
            DatObjectCacheMisses: datObjectStashStats.Misses,
            DatObjectCacheEvictions: datObjectStashStats.Evictions,
            PhysicsGraphGfxObjs: _physics.GraphGfxObjRefTally,
            PhysicsGraphSetups: _physics.GraphRigTally,
            PhysicsGraphCells: _physics.GraphChamberStructTally,
            PhysicsFlatGfxObjs: _physics.PlanarGfxObjRefTally,
            PhysicsFlatSetups: _physics.PlanarRigTally,
            PhysicsFlatCells: _physics.PlanarChamberStructTally,
            PhysicsFlatEnvCells: _physics.PlanarEnvironChamberTally,
            CollisionShadow: _physics.LinkProxyStats,
            StreamingWork: _paging.JobTelemetry,
            Residency: residency,
            Fps: rasterize.Fps,
            FrameMilliseconds: rasterize.FrameMilliseconds,
            LastFrameProfile: _cycleProfiler.LastReport);
    }

}
