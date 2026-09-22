using MacAC.Assets;
using MacAC.Client.Graphics.Tenancy;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class RealmTriMeshBridge
    : IDisposable,
      IBatchMeshBridge
{
    internal const int CeilingUploadsPerCycle = 8;

    internal const int DestUnveilCeilingUploadsPerCycle = 64;

    internal const long CeilingPushOctetsPerCycle = 8L * 1024 * 1024;

    internal const long CeilingArrAllocOctetsPerCycle = 8L * 1024 * 1024;

    internal const long CeilingMipmapOctetsPerCycle = 8L * 1024 * 1024;

    internal const int CeilingNewArrsPerCycle = 1;

    internal const long CeilingBufPushOctetsPerCycle = 8L * 1024 * 1024;

    internal const long CeilingBufAllocOctetsPerCycle =
        GlobalTriMeshBuffer.CeilingVertBufOctets;

    internal const long CeilingBufDuplicateOctetsPerCycle = 32L * 1024 * 1024;

    internal const int CeilingNewBufsPerCycle = 1;

    internal const long CeilingSinglePushOctets = 128L * 1024 * 1024;

    internal const long CeilingSingleArrAllocOctets = 128L * 1024 * 1024;

    internal const long CeilingSingleMipmapOctets = 128L * 1024 * 1024;

    internal const int CeilingSingleNewArrs = 16;

    internal const long CeilingSingleBufPushOctets = 32L * 1024 * 1024;

    internal const int CeilingReclaimedTriMeshesPerCycle = CeilingUploadsPerCycle;

    internal const long CeilingReclaimedTriMeshOctetsPerCycle = 64L * 1024 * 1024;

    internal const int CeilingStaleDiscardsPerCycle = 64;

    private readonly ITriMeshPipeDevice? _visualsDev;
    private readonly MacAC.Client.Graphics.IGpuAssetSunsetFifo? _assetSunset;

    private readonly Func<uint, bool> _coreConcealedMarker;

    private readonly IBakedAssetSource? _possessedReadiedHoldings;

    private readonly MeshUploadFrameAllowance _plainPushAllowance =
        BuildPushAllowance(CeilingUploadsPerCycle);

    private readonly MeshUploadFrameAllowance _destUnveilPushAllowance =
        BuildPushAllowance(DestUnveilCeilingUploadsPerCycle);

    private readonly HashSet<TextureAtlasKeeper> _mipmapsBudgeted = [];

    private bool _destUnveilPushPrecedence;

    private readonly bool _isUninitialized;

    private bool _destroyed;

    private MacAC.Client.Graphics.SequencedAssetTeardown? _teardown;

    internal RealmTriMeshBridge(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        IDatAccess datFiles,
        ILogger<RealmTriMeshBridge> logger)
        : this(
            gpuDev,
            datFiles,
            readiedHoldings: null,
            logger,
            MacAC.Client.Graphics.ImmediateGpuAssetSunsetFifo.Instance,
            ownsReadiedHoldings: true,
            TenancyAllowanceKnobs.Default)
    {
    }

    internal RealmTriMeshBridge(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        IDatAccess datFiles,
        IBakedAssetSource readiedHoldings,
        ILogger<RealmTriMeshBridge> logger,
        MacAC.Client.Graphics.IGpuAssetSunsetFifo assetSunset,
        TenancyAllowanceKnobs? budgets = null)
        : this(
            gpuDev,
            datFiles,
            readiedHoldings,
            logger,
            assetSunset,
            ownsReadiedHoldings: false,
            budgets ?? TenancyAllowanceKnobs.Default)
    {
    }

    private RealmTriMeshBridge(
        MacAC.Client.Graphics.Gpu.IClientGpuDevice gpuDev,
        IDatAccess datFiles,
        IBakedAssetSource? readiedHoldings,
        ILogger<RealmTriMeshBridge> logger,
        MacAC.Client.Graphics.IGpuAssetSunsetFifo assetSunset,
        bool ownsReadiedHoldings,
        TenancyAllowanceKnobs budgets)
    {
        ArgumentNullException.ThrowIfNull(gpuDev);
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(budgets);

        _assetSunset = assetSunset;
        var concealedMarkerMemo =
            new System.Collections.Concurrent.ConcurrentDictionary<uint, bool>();
        _coreConcealedMarker = gfxObjRefIdent =>
            concealedMarkerMemo.GetOrAdd(
                gfxObjRefIdent,
                static (ident, src) =>
                    MacAC.Mechanics.Geometry.GfxObjLodResolver
                        .IsRuntimeHiddenMarker(src, ident),
                datFiles);
        AssetTidyCluster assetList = new MacAC.Client.Graphics.AssetTidyCluster();
        ITriMeshPipeDevice? visualsDev = null;
        var settledReadiedHoldings = readiedHoldings;
        ThingTriMeshKeeper? triMeshKeeper = null;
        try
        {
            var rhiDev = new MacAC.Client.Graphics.Gpu.Vulkan.VkMeshPipelineDevice(
                assetSunset);
            visualsDev = rhiDev;
            assetList.Add("WB graphics device", rhiDev.Dispose);
            if (settledReadiedHoldings is null)
            {
                settledReadiedHoldings = new DatBakedAssetSource(
                    datFiles,
                    new ConsoleProblemLogger<ThingTriMeshKeeper>());
                assetList.Add(
                    "WB tooling prepared asset source",
                    settledReadiedHoldings.Dispose);
            }
            triMeshKeeper = new ThingTriMeshKeeper(
                visualsDev,
                gpuDev,
                settledReadiedHoldings,
                new ConsoleProblemLogger<ThingTriMeshKeeper>(),
                budgets);
            assetList.Add("WB object mesh manager", triMeshKeeper.Dispose);
            assetList.TransferAll();
        }
        catch (Exception constructionMiss)
        {
            assetList.RevertConstructionAndThrow(
                "RealmTriMeshBridge construction failed and its mesh/GL prefix did not cleanly roll back.",
                constructionMiss);
        }

        _visualsDev = visualsDev;
        _meshManager = triMeshKeeper;
        _possessedReadiedHoldings = ownsReadiedHoldings
            ? settledReadiedHoldings
            : null;
    }

    private sealed class ConsoleProblemLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState phase) where TState : notnull => NullAmbit.Instance;
        public bool IsEnabled(LogLevel traceTier) => traceTier >= LogLevel.Error;
        public void Log<TState>(
            LogLevel traceTier, EventId signalIdent, TState phase, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(traceTier)) return;
            string msg = formatter(phase, exception);
            Console.WriteLine($"[wb-error] {msg}");
            if (exception is not null)
            {
                Console.WriteLine($"[wb-error]   {exception.GetType().Name}: {exception.Message}");
                IEnumerable<string> pile = (exception.StackTrace ?? "")
                    .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Take(5);
                foreach (var s in pile) Console.WriteLine($"[wb-error]   {s.Trim()}");
            }
        }

        private sealed class NullAmbit : IDisposable
        {
            public static readonly NullAmbit Instance = new();
            public void Dispose() { }
        }
    }

    private RealmTriMeshBridge()
    {
        _isUninitialized = true;
        _coreConcealedMarker = static _ => false;
    }
}
