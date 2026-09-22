namespace MacAC.Client.Graphics;

internal readonly record struct RasterizeCycleFeed(
    double DeltaSeconds,
    int ViewportWidth,
    int ViewportHeight)
{
    /// <summary>
    /// The backbuffer's extent in pixels. A capture is sized with the swapchain, so anything
    /// reading the presented frame has to ask in these terms and not in the viewport's points,
    /// which differ on a scaled display. Zero means "same as the viewport".
    /// </summary>
    public int BackbufferWidth { get; init; }

    public int BackbufferHeight { get; init; }

    public (int Width, int Height) CaptureExtent =>
        BackbufferWidth > 0 && BackbufferHeight > 0
            ? (BackbufferWidth, BackbufferHeight)
            : (ViewportWidth, ViewportHeight);
}

internal readonly record struct RealmRenderFrameVerdict(
    int VisibleLandblocks,
    int TotalLandblocks,
    bool NormalWorldDrawn);

internal readonly record struct PrivateDisplayFrameVerdict(
    bool PortalViewportDrawn,
    bool ScreenshotCaptured);

internal readonly record struct RenderFrameVerdict(
    RealmRenderFrameVerdict World,
    PrivateDisplayFrameVerdict Presentation,
    bool SkippedZeroArea = false)
{
    internal static RenderFrameVerdict ZeroArea => new(default, default, SkippedZeroArea: true);
}

internal interface IRasterizeCycleLifespan
{
    void BeginFrame();

    void EndFrame();
}

internal interface IStructureDowngradeCycleBeat
{
    void Tick(double passedSecs);
}

internal interface IRasterizeCycleAssetStage
{
    void Prepare(RasterizeCycleFeed feed);
}

internal interface IRasterizeCycleGpuReading
{
    void BeginFrame();

    void EndFrame();
}

internal interface IRealmStageFramePhase
{
    RealmRenderFrameVerdict Render(RasterizeCycleFeed feed);
}

internal interface IPrivateDisplayFramePhase
{
    PrivateDisplayFrameVerdict Render(
        RasterizeCycleFeed feed,
        RealmRenderFrameVerdict realm);
}

internal interface IRenderFrameTelemetryPhase
{
    void Publish(RasterizeCycleFeed feed, RenderFrameVerdict verdict);
}

internal interface IRenderFramePostTelemetryPhase
{
    void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict);
}

internal sealed class NullRenderFramePostTelemetryPhase :
    IRenderFramePostTelemetryPhase
{
    public static NullRenderFramePostTelemetryPhase Instance { get; } = new();

    private NullRenderFramePostTelemetryPhase()
    {
    }

    public void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
    }
}

internal sealed class SerialRenderFramePostTelemetryPhase :
    IRenderFramePostTelemetryPhase
{
    private readonly IRenderFramePostTelemetryPhase[] _phases;

    public SerialRenderFramePostTelemetryPhase(
        params IRenderFramePostTelemetryPhase[] phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        if (phases.Length is 0 || phases.Any(static stage => stage is null))
        {
            throw new ArgumentException(
                "At least one non-null post-diagnostic phase is needed",
                nameof(phases));
        }
        _phases = [.. phases];
    }

    public void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
        for (int idx = 0; idx < _phases.Length; ++idx)
            _phases[idx].Process(feed, verdict);
    }
}

internal interface IRasterizeCycleMissRecovery
{
    void AbortFrame();
}

internal sealed class NullRasterizeCycleMissRecovery : IRasterizeCycleMissRecovery
{
    public static NullRasterizeCycleMissRecovery Instance { get; } = new();

    private NullRasterizeCycleMissRecovery()
    {
    }

    public void AbortFrame()
    {
    }
}

internal sealed class RenderFrameConductor(
    IRasterizeCycleLifespan lifetime,
    IRasterizeCycleGpuReading gpuMeasurement,
    IRasterizeCycleAssetStage resources,
    IRealmStageFramePhase world,
    IPrivateDisplayFramePhase presentation,
    IRenderFrameTelemetryPhase diagnostics,
    IRenderFramePostTelemetryPhase postDiagnostics,
    IRasterizeCycleMissRecovery recovery,
    IStructureDowngradeCycleBeat? structureDegrades = null,
    IPrivateCycleScreenshot? screenshots = null) : IPlayRasterizeCycleTrunk
{
    private readonly IRasterizeCycleLifespan _lifespan = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
    private readonly IRasterizeCycleGpuReading _gpuMeasurement = gpuMeasurement
            ?? throw new ArgumentNullException(nameof(gpuMeasurement));
    private readonly IRasterizeCycleAssetStage _assetList = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly IRealmStageFramePhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly IPrivateDisplayFramePhase _exhibit = presentation ?? throw new ArgumentNullException(nameof(presentation));
    private readonly IRenderFrameTelemetryPhase _telemetry = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly IRenderFramePostTelemetryPhase _postTelemetry = postDiagnostics
            ?? throw new ArgumentNullException(nameof(postDiagnostics));
    private readonly IRasterizeCycleMissRecovery _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    private readonly IStructureDowngradeCycleBeat? _structureDegrades = structureDegrades;
    private readonly IPrivateCycleScreenshot? _screenshots = screenshots;

    public RenderFrameVerdict Render(RasterizeCycleFeed feed)
    {
        // A window with no area has nothing to render and no swapchain to render into; decide that BEFORE
        // a GPU frame is opened so nothing downstream (the render-pack activation extent, the world
        // target, the presentation viewport) ever sees a zero extent.
        if (feed.ViewportWidth <= 0 || feed.ViewportHeight <= 0)
            return RenderFrameVerdict.ZeroArea;

        _structureDegrades?.Tick(feed.DeltaSeconds);
        _lifespan.BeginFrame();
        RealmRenderFrameVerdict realm;
        PrivateDisplayFrameVerdict exhibit;
        try
        {
            Exception? measuredRasterizeMiss = null;
            _gpuMeasurement.BeginFrame();
            try
            {
                _assetList.Prepare(feed);
                realm = _world.Render(feed);
                exhibit = _exhibit.Render(feed, realm);
            }
            catch (Exception problem)
            {
                measuredRasterizeMiss = problem;
                throw;
            }
            finally
            {
                try
                {
                    _gpuMeasurement.EndFrame();
                }
                catch (Exception measurementMiss)
                    when (measuredRasterizeMiss is not null)
                {
                    throw new AggregateException(
                        "Rendering failed and its GPU measurement could not be closed",
                        measuredRasterizeMiss,
                        measurementMiss);
                }
            }
        }
        catch (Exception rasterizeMiss)
        {
            ProcessRasterizeMiss(rasterizeMiss);
            throw;
        }

        _lifespan.EndFrame();
        var (captureWidth, captureHeight) = feed.CaptureExtent;
        bool screenshotGrabbed = _screenshots?.GrabQueued(captureWidth, captureHeight) == true;
        RenderFrameVerdict verdict = new RenderFrameVerdict(
            realm,
            exhibit with { ScreenshotCaptured = screenshotGrabbed });
        _telemetry.Publish(feed, verdict);
        _postTelemetry.Process(feed, verdict);
        return verdict;
    }

    private void ProcessRasterizeMiss(Exception rasterizeMiss)
    {
        Exception? recoveryMiss = null;
        try
        {
            _recovery.AbortFrame();
        }
        catch (Exception problem)
        {
            recoveryMiss = problem;
        }

        try
        {
            _lifespan.EndFrame();
        }
        catch (Exception shutMiss)
        {
            if (recoveryMiss is not null)
            {
                throw new AggregateException(
                    "Rendering failed and neither presentation recovery nor the in-flight GPU frame could be closed",
                    rasterizeMiss,
                    recoveryMiss,
                    shutMiss);
            }

            throw new AggregateException(
                "Rendering failed and the in-flight GPU frame could not be closed",
                rasterizeMiss,
                shutMiss);
        }

        if (recoveryMiss is not null)
        {
            throw new AggregateException(
                "Rendering failed and the presentation frame could not be aborted",
                rasterizeMiss,
                recoveryMiss);
        }
    }
}
