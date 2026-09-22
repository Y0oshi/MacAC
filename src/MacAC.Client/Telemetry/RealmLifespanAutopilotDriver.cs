using System.Text.Json;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Graphics.Tenancy;
using MacAC.Client.Paging;
using MacAC.Client.Shell.Testing;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim;
using MacAC.Sim.Realm;

namespace MacAC.Client.Telemetry;

internal static class AutopilotArtifactName
{
    public static bool TryVet(string? label, out string problem)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Length > 80)
        {
            problem = "artifact name must contain 1-80 characters";
            return false;
        }

        foreach (char c in label)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
            {
                problem = $"artifact name '{label}' may contain only letters, digits, '-' and '_'";
                return false;
            }
        }

        problem = string.Empty;
        return true;
    }
}

internal sealed record RealmLifespanResourceCapture(
    int LoadedLandblocks,
    int WorldEntities,
    int AnimatedEntities,
    int VisibleLandblocks,
    int TotalLandblocks,
    int LiveEntities,
    int MaterializedLiveEntities,
    CurrentRenderStageOracleCapture RenderSceneOracle,
    RenderStageShadeComparisonCapture RenderSceneShadow,
    RenderFrameProductComparisonCapture RenderFrameProduct,
    int PendingLiveTeardowns,
    int PendingLandblockRetirements,
    int ParticleEmitters,
    int Particles,
    int ParticleBindings,
    int ParticleOwners,
    int EffectOwners,
    int LightOwners,
    int ScriptOwners,
    int ActiveScripts,
    int MeshRenderData,
    int MeshAtlasArrays,
    long MeshEstimatedBytes,
    int StagedMeshUploads,
    long StagedMeshBytes,
    long TrackedGpuBytes,
    long ResidencyGpuBytes,
    long GpuTrackerMinusResidencyBytes,
    int TrackedGpuBuffers,
    int TrackedGpuTextures,
    int OwnedCompositeTextures,
    int CompositeTextureOwners,
    int ActiveParticleTextures,
    int ParticleTextureOwners,
    int CompositeWarmupPending,
    long ManagedBytes,
    long ManagedCommittedBytes,
    long LohSizeBytes,
    long LohFragmentationBytes,
    long ProcessTotalAllocatedBytes,
    long CpuMeshCacheHits,
    long CpuMeshCacheMisses,
    long CpuMeshCacheEvictions,
    long DecodedTextureCacheHits,
    long DecodedTextureCacheMisses,
    long DecodedTextureCacheEvictions,
    long DatObjectCacheHits,
    long DatObjectCacheMisses,
    long DatObjectCacheEvictions,
    int PhysicsGraphGfxObjs,
    int PhysicsGraphSetups,
    int PhysicsGraphCells,
    int PhysicsFlatGfxObjs,
    int PhysicsFlatSetups,
    int PhysicsFlatCells,
    int PhysicsFlatEnvCells,
    ContactProxyStats CollisionShadow,
    PagingWorkTelemetry StreamingWork,
    TenancyCapture Residency,
    double Fps,
    double FrameMilliseconds,
    string? LastFrameProfile);

internal sealed record RealmLifespanCheckpoint(
    int Sequence,
    string Name,
    DateTime TimestampUtc,
    int ProcessId,
    SimPortalCapture Reveal,
    SimRealmAmbienceHoldingCapture EnvironmentOwnership,
    SimRealmCrossingHoldingCapture TransitOwnership,
    RenderFrameVerdict Render,
    RealmLifespanResourceCapture Resources);

internal sealed class RealmLifespanCheckpointAsk(object owner, int series, string name) :
    ICanonWidgetAutopilotCheckpoint
{
    private readonly object _synchronize = new();
    private CanonWidgetAutopilotCheckpointStatus _condition =
        CanonWidgetAutopilotCheckpointStatus.Pending;

    internal object Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));
    public int Sequence { get; } = series;
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    public CanonWidgetAutopilotCheckpointStatus Status
    {
        get
        {
            lock (_synchronize)
                return _condition;
        }
    }

    public string? Error
    {
        get
        {
            lock (_synchronize)
                return field;
        }

        private set;
    }

    internal bool TrySetTerminal(
        CanonWidgetAutopilotCheckpointStatus status,
        string? problem)
    {
        if (status == CanonWidgetAutopilotCheckpointStatus.Pending)
            throw new ArgumentOutOfRangeException(nameof(status));

        lock (_synchronize)
        {
            if (_condition != CanonWidgetAutopilotCheckpointStatus.Pending)
                return false;
            _condition = status;
            Error = problem;
            return true;
        }
    }
}

internal sealed class RealmRevealFactsAutopilotEngine(
    Func<SimPortalCapture> getReveal,
    Func<int> getPortalMaterializationCount)
        : ICanonWidgetAutopilotEngine
{
    private readonly Func<SimPortalCapture> _fetchUnveil = getReveal
            ?? throw new ArgumentNullException(nameof(getReveal));
    private readonly Func<int> _fetchGatewayMaterializationTally = getPortalMaterializationCount
            ?? throw new ArgumentNullException(
                nameof(getPortalMaterializationCount));

    public bool IsRealmReady => _fetchUnveil().IsReady;
    public bool IsRealmViewRectShown => _fetchUnveil().WorldViewportObserved;
    public int GatewayMaterializationCount => _fetchGatewayMaterializationTally();

    public bool TryReqCheckpoint(
        string label,
        out ICanonWidgetAutopilotCheckpoint? checkpoint,
        out string problem)
    {
        checkpoint = null;
        problem = "checkpoints require MACAC_AUTOMATION_ARTIFACT_DIR";
        return false;
    }

    public void AbortCheckpoint(ICanonWidgetAutopilotCheckpoint checkpoint)
    {
    }

    public bool TryReqScreenshot(string label, out string problem)
    {
        problem = "screenshots require MACAC_AUTOMATION_ARTIFACT_DIR";
        return false;
    }

    public bool IsScreenshotDone(string label) => false;
}

internal sealed partial class RealmLifespanAutopilotDriver(
    Func<SimPortalCapture> getReveal,
    Func<SimRealmAmbienceHoldingCapture>
            getEnvironmentOwnership,
    Func<SimRealmCrossingHoldingCapture> getTransitOwnership,
    Func<int> getPortalMaterializationCount,
    Func<RenderFrameVerdict, RealmLifespanResourceCapture> captureResources,
    FrameScreenshotDriver screenshots,
    string artifactDirectory,
    Action<string>? trace = null,
    Func<int>? fetchRasterizeBundlePerformanceSpecimenTally = null,
    Func<(bool Succeeded, string Error)>? restartRasterizeBundlePerformance = null,
    Func<bool>? fetchRasterizeBundleFailedToCanon = null,
    Func<CanonWidgetAutopilotRenderPackStatus>? fetchRasterizeBundleCondition = null,
    Func<string, (bool Succeeded, string Error)>? pickRasterizeBundle = null,
    Func<(bool Succeeded, string Error)>? deactivateRasterizeBundle = null,
    Func<(bool Succeeded, string Error)>? reenableRasterizeBundle = null,
    Func<(int Width, int Height)>? fetchFramebufferDims = null,
    Func<int, int, (bool Succeeded, string Error)>? rescaleFramebuffer = null,
    Action? reqClientShut = null) :
    ICanonWidgetAutopilotEngine,
    IRenderFramePostTelemetryPhase,
    IDisposable
{
    private static readonly JsonSerializerOptions JsonKnobs = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Func<SimPortalCapture> _fetchUnveil = getReveal ?? throw new ArgumentNullException(nameof(getReveal));

    private readonly Func<SimRealmAmbienceHoldingCapture>
        _fetchSurroundingsOwnership = getEnvironmentOwnership
            ?? throw new ArgumentNullException(
                nameof(getEnvironmentOwnership));

    private readonly Func<SimRealmCrossingHoldingCapture>
        _fetchPassageOwnership = getTransitOwnership
            ?? throw new ArgumentNullException(nameof(getTransitOwnership));

    private readonly Func<int> _fetchGatewayMaterializationTally = getPortalMaterializationCount
            ?? throw new ArgumentNullException(nameof(getPortalMaterializationCount));

    private readonly Func<int> _fetchRasterizeBundlePerformanceSpecimenTally =
            fetchRasterizeBundlePerformanceSpecimenTally ?? (() => 0);

    private readonly Func<bool> _fetchRasterizeBundleFailedToCanon = fetchRasterizeBundleFailedToCanon ?? (() => false);

    private readonly Func<CanonWidgetAutopilotRenderPackStatus>
        _fetchRasterizeBundleCondition = fetchRasterizeBundleCondition
            ?? (() => CanonWidgetAutopilotRenderPackStatus.Retail);

    private readonly Func<string, (bool Succeeded, string Error)>
        _pickRasterizeBundle = pickRasterizeBundle
            ?? (_ => (false, "render-pack selection automation is unavailable"));

    private readonly Func<(bool Succeeded, string Error)>?
        _deactivateRasterizeBundle = deactivateRasterizeBundle;

    private readonly Func<(bool Succeeded, string Error)>?
        _reenableRasterizeBundle = reenableRasterizeBundle;

    private readonly Func<(int Width, int Height)> _fetchFramebufferDims = fetchFramebufferDims ?? (() => (0, 0));

    private readonly Func<int, int, (bool Succeeded, string Error)>
        _rescaleFramebuffer = rescaleFramebuffer
            ?? ((_, _) => (false, "framebuffer resize automation is unavailable"));

    private readonly Func<(bool Succeeded, string Error)>
        _restartRasterizeBundlePerformance = restartRasterizeBundlePerformance
            ?? (() => (false, "render-pack performance automation is unavailable"));

    private readonly Action? _reqClientShut = reqClientShut;

    private readonly Func<RenderFrameVerdict, RealmLifespanResourceCapture>
        _grabAssetList = captureResources ?? throw new ArgumentNullException(nameof(captureResources));

    private readonly FrameScreenshotDriver _screenshots = screenshots ?? throw new ArgumentNullException(nameof(screenshots));

    private readonly string _artifactFolder = string.IsNullOrWhiteSpace(artifactDirectory)
            ? throw new ArgumentException("An automation artifact directory is needed", nameof(artifactDirectory))
            : Path.GetFullPath(artifactDirectory);

    private readonly Action<string> _trace = trace ?? (_ => { });

    private readonly object _reqHolder = new();

    private readonly object _synchronize = new();

    private readonly Queue<RealmLifespanCheckpointAsk> _reqs = [];

    private string? _previousTurnedOnRasterizeBundlePreset;

    private int _series;

    private bool _destroyed;
}
