using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Graphics.Tenancy;

namespace MacAC.Client.Graphics.Batching;

public sealed unsafe partial class RealmPaintRouter
{
    private readonly IClientGpuDevice? _device;

    private readonly ILatestGpuCycleOrigin? _cycles;

    private readonly IRealmPassScope? _ambit;

    internal sealed record TriMeshPipeGroup(
        int SampleCount,
        IGpuPipe Opaque,
        IGpuPipe OpaqueAlphaToCoverage,
        IGpuPipe AlphaBlend,
        IGpuPipe AlphaBlendDepthWrite,
        IGpuPipe AlphaAdditive,
        IGpuPipe AlphaAdditiveDepthWrite,
        IGpuPipe RawAdditive,
        IGpuPipe RawAdditiveDepthWrite,
        IGpuPipe AlphaInverse,
        IGpuPipe AlphaInverseDepthWrite,
        IGpuPipe InverseAdditive,
        IGpuPipe InverseAdditiveDepthWrite);

    private TriMeshPipeGroup? _backbufferPipes;

    private TriMeshPipeGroup? _offscreenPipes;

    private const string SolidTickerAmbit = "wb-entities-opaque";

    private const string SeeThruTickerAmbit = "wb-entities-transparent";

    private readonly LandTileset.CanonDetailTextureWiring _structureSpecifics;

    private readonly Func<bool> _structureSpecificsTurnedOn;

    private readonly record struct RhiSegment(
        IClientGpuBuffer? Buffer,
        uint OffsetBytes,
        uint SizeBytes);

    private readonly RealmTransformFrameArena _realmXformCycles = new();

    private long _plainXformDemandCycleSerialNo = -1;

    private uint _plainXformDemandThisCycle;

    private uint _plainXformDemandHiWater;

    private RhiSegment _alphaInsts;

    private RhiSegment _alphaLots;

    private RhiSegment _alphaClipSockets;

    private RhiSegment _alphaGlobalLamps;

    private RhiSegment _alphaLampSets;

    private RhiSegment _alphaInside;

    private RhiSegment _alphaDensity;

    private RhiSegment _alphaPickIllumination;

    private RhiSegment _alphaSpecificsBucket;

    private RhiSegment _alphaDirectives;

    private int _readiedAlphaInstTally;

    private uint _alphaXformBaseInst;

    internal RealmPaintRouter(
        IClientGpuDevice device,
        ILatestGpuCycleOrigin frames,
        IRealmPassScope scope,
        BitmapStash textures,
        RealmTriMeshBridge meshAdapter,
        ActorSummonBridge entitySpawnAdapter,
        ActorTaxonomyStash classificationCache,
        MacAC.Mechanics.Drawing.SeeThroughFadeKeeper translucencyFades,
        ICanonPickingRenderSink? pickDrain = null,
        CanonAlphaFifo? alphaFifo = null,
        long? alphaTempAllowanceOctets = null,
        LandTileset.CanonDetailTextureWiring structureSpecifics = default,
        Func<bool>? structureSpecificsTurnedOn = null,
        Func<uint, float>? hierarchicalSeeThrough = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
        _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
        _textures = textures ?? throw new ArgumentNullException(nameof(textures));
        _triMeshBridge = meshAdapter ?? throw new ArgumentNullException(nameof(meshAdapter));
        _actorSummonBridge = entitySpawnAdapter
            ?? throw new ArgumentNullException(nameof(entitySpawnAdapter));
        _stash = classificationCache
            ?? throw new ArgumentNullException(nameof(classificationCache));
        _seeThroughFades = translucencyFades
            ?? throw new ArgumentNullException(nameof(translucencyFades));
        _pickDrain = pickDrain;
        _pickIllumination = pickDrain as ICanonPickingLightingSource;
        _alphaFifo = alphaFifo;
        _hierarchicalSeeThrough = hierarchicalSeeThrough;
        _alphaSrc = new AlphaPaintOrigin(this);
        _structureSpecifics = structureSpecifics;
        _structureSpecificsTurnedOn = structureSpecificsTurnedOn ?? DeactivateSpecificsTextures;
        long tempAllowance = alphaTempAllowanceOctets
            ?? AlphaScratchAllowanceProfile.Create(
                TenancyAllowanceKnobs.Default.AlphaScratchBytes)
                .DispatcherBytes;
        _alphaTempRule = new RetainedScratchCapacityRule(tempAllowance);

        int specimens = scope.SampleCount;
        try
        {
            _backbufferPipes = BuildTriMeshPipeSet(device, specimens);
            _offscreenPipes = specimens is 1
                ? _backbufferPipes
                : BuildTriMeshPipeSet(device, 1);
        }
        catch
        {
            TeardownRhiAssetList();
            throw;
        }
    }

    private static RhiSegment EmitLoopSection<T>(
        IGpuCycle cycle,
        ReadOnlySpan<T> blob,
        GpuLoopPurpose usage = GpuLoopPurpose.Storage)
        where T : unmanaged
    {
        int elemOctets = sizeof(T);
        int byteTally = Math.Max(blob.Length * elemOctets, elemOctets);
        var alloc = cycle.ReserveLoop(byteTally, usage);
        if (!blob.IsEmpty)
            blob.CopyTo(alloc.AsSpan<T>());
        return new RhiSegment(alloc.Buffer, alloc.ShiftOctets, (uint)byteTally);
    }

    internal bool UpcomingClassicPaintIsPrivatePass;

    private sealed class NullRhiTickerAmbit : IDisposable
    {
        internal static NullRhiTickerAmbit Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
