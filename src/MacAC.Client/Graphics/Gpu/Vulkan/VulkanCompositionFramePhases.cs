using System.Diagnostics;
using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkRenderFrameClearPhase(
    WorldClock worldTime,
    WeatherEngine weather,
    IRasterizeCycleGatewayPhaseOrigin portal,
    MoteVisibilityDriver particleVisibility,
    VkBackbufferClearLedger clear) : IRasterizeCycleWipeStage
{
    private readonly WorldClock _realmMoment = worldTime ?? throw new ArgumentNullException(nameof(worldTime));
    private readonly WeatherEngine _weather = weather ?? throw new ArgumentNullException(nameof(weather));
    private readonly IRasterizeCycleGatewayPhaseOrigin _gateway = portal ?? throw new ArgumentNullException(nameof(portal));
    private readonly MoteVisibilityDriver _moteVis = particleVisibility
            ?? throw new ArgumentNullException(nameof(particleVisibility));
    private readonly VkBackbufferClearLedger _wipe = clear ?? throw new ArgumentNullException(nameof(clear));

    public RasterizeCycleFoundation Clear()
    {
        bool gatewayViewRectShown = _gateway.IsGatewayViewRectShown;
        if (gatewayViewRectShown)
            _moteVis.Reset();

        SkyKeyframe heavens = _realmMoment.LatestHeavens;
        AtmosphereFrame atmosphere = _weather.Snapshot(in heavens);
        Vector4 wipe = gatewayViewRectShown
            ? new Vector4(0f, 0f, 0f, 1f)
            : new Vector4(
                Math.Clamp(atmosphere.FogColor.X, 0f, 1f),
                Math.Clamp(atmosphere.FogColor.Y, 0f, 1f),
                Math.Clamp(atmosphere.FogColor.Z, 0f, 1f),
                1f);

        var foundation = new RasterizeCycleFoundation(
            gatewayViewRectShown,
            heavens,
            atmosphere);
        _wipe.WipeTint = wipe;
        _wipe.Foundation = foundation;
        return foundation;
    }
}

internal sealed class VulkanRealmTableauStage(
    ILatestGpuCycleOrigin frames,
    VkBackbufferClearLedger clear,
    Func<int> sampleCount,
    VkRealmPassScope scope,
    IRealmStageFramePhase world,
    RenderPackDriver? rasterizeBundles = null,
    AtmosphericFrameInputLedger? atmosphere = null,
    Func<RasterizeBundleActivationReach, RenderPackActivationCapture>?
            enactRasterizeBundleBoundary = null,
    RenderStageShadeEngine? rasterizeTableau = null,
    RealmPaintRouter? realmTriMeshes = null,
    LandModernPainter? land = null) : IRealmStageFramePhase
{
    private readonly ILatestGpuCycleOrigin _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
    private readonly VkBackbufferClearLedger _wipe = clear ?? throw new ArgumentNullException(nameof(clear));
    private readonly Func<int> _specimenTally = sampleCount ?? throw new ArgumentNullException(nameof(sampleCount));
    private readonly VkRealmPassScope _ambit = scope ?? throw new ArgumentNullException(nameof(scope));
    private readonly IRealmStageFramePhase _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly RenderPackDriver? _rasterizeBundles = rasterizeBundles;
    private readonly AtmosphericFrameInputLedger? _atmosphere = atmosphere;
    private readonly Func<RasterizeBundleActivationReach, RenderPackActivationCapture>?
        _enactRasterizeBundleBoundary = enactRasterizeBundleBoundary;
    private readonly RenderStageShadeEngine? _rasterizeTableau = rasterizeTableau;
    private readonly RealmPaintRouter? _realmTriMeshes = realmTriMeshes;
    private readonly LandModernPainter? _land = land;

    public RealmRenderFrameVerdict Render(RasterizeCycleFeed feed)
    {
        IGpuCycle cycle = _cycles.LatestCycle
            ?? throw new InvalidOperationException(
                "The Vulkan world phase needs an open IGpuCycle (see GpuDeviceCycleLifespan)");

        int specimens = _specimenTally();
        if (_rasterizeBundles is not null)
        {
            var reach = new RasterizeBundleActivationReach(
                feed.ViewportWidth,
                feed.ViewportHeight,
                specimens);
            _ = _enactRasterizeBundleBoundary is not null
                ? _enactRasterizeBundleBoundary(reach)
                : _rasterizeBundles.ImposeAtCycleBoundary(reach);
        }
        if (_rasterizeBundles?.ActiveRuntime is { } engaged)
        {
            if (engaged is IDefaultRealmPathRenderPackEngine)
                return RenderRetail(cycle, feed);
            if (engaged is not IAtmosphericRealmGraphEngine graph
                || _atmosphere is null)
            {
                _rasterizeBundles.OnCoreMiss(
                    "The selected pack has no compatible production world graph.");
                return RenderRetail(cycle, feed);
            }

            var cpuJunctureProfile =
                graph as IAtmosphericCpuStageProfileEngine;
            bool profileCpuJunctures = cpuJunctureProfile?.ShouldProfileCpuCycle(cycle.SerialNo) == true;
            long bundleCpuBeats = 0;
            long markPrepBeats = 0;
            IGpuRasterizeMark mark;
            long bundleBegun = Stopwatch.GetTimestamp();
            try
            {
                mark = graph.PrepareWorldTarget(
                    feed.ViewportWidth,
                    feed.ViewportHeight,
                    specimens);
            }
            catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
            {
                _rasterizeBundles.OnCoreMiss(
                    "Atmospheric target creation failed: "
                    + problem.GetBaseException().Message);
                return RenderRetail(cycle, feed);
            }
            finally
            {
                long passed = Stopwatch.GetTimestamp() - bundleBegun;
                bundleCpuBeats += passed;
                if (profileCpuJunctures)
                    markPrepBeats = passed;
            }

            _atmosphere.BeginFrame(in feed, _wipe.Foundation);
            PreparedRealmStageFrame? readied = null;
            if (graph is IDirectionalShadeRealmGraphEngine directed)
            {
                if (_world is not IPreparedRealmStageFramePhase readiedRealm
                    || _rasterizeTableau is null
                    || _realmTriMeshes is null
                    || _land is null)
                {
                    _rasterizeBundles.OnCoreMiss(
                        "The selected directional-shadow pack has no compatible world preparation seam.");
                    return RenderRetail(cycle, feed);
                }

                PreparedRealmStageFrame val;
                try
                {
                    val = readiedRealm.ReadyEnhanced(feed);
                }
                catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
                {
                    _rasterizeBundles.OnCoreMiss(
                        "Atmospheric world preparation failed: "
                        + problem.GetBaseException().Message);
                    return RenderRetail(cycle, feed);
                }
                readied = val;
                if (val.ShouldRender)
                {
                    bundleBegun = Stopwatch.GetTimestamp();
                    try
                    {
                        RenderStageProbe tableau = _rasterizeTableau.Ask;
                        RasterizeCycleFoundation readiedFoundation = val.Foundation;
                        RealmRenderFrame readiedRealmCycle = val.World;
                        directed.RenderDirectionalShadows(
                            cycle,
                            in readiedFoundation,
                            in readiedRealmCycle,
                            val.ActiveDayGroup,
                            in tableau,
                            _realmTriMeshes,
                            _land);
                    }
                    catch (Exception problem) when (VkRenderFailureRule.IsFatal(problem))
                    {
                        readiedRealm.AbortReadiedEnhanced(in val);
                        throw;
                    }
                    catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
                    {
                        readiedRealm.AbortReadiedEnhanced(in val);
                        _realmTriMeshes.AbortDirectedShadeXformCycle(cycle);
                        _rasterizeBundles.OnCoreMiss(
                            "Directional shadow rendering failed: "
                            + problem.GetBaseException().Message);
                        return RenderRetail(cycle, feed);
                    }
                    finally
                    {
                        bundleCpuBeats += Stopwatch.GetTimestamp() - bundleBegun;
                    }
                }
            }
            RealmRenderFrameVerdict verdict;
            long recipientCpuBeats = 0;
            try
            {
                using IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
                {
                    Name = "atmospheric-world-hdr",
                    Color = new GpuTintAffix(
                        mark,
                        GpuPullOp.Clear,
                        specimens > 1 ? GpuVaultOp.Resolve : GpuVaultOp.Store,
                        _wipe.WipeTint),
                    ZDepth = new GpuZDepthAffix(
                        GpuPullOp.Clear,
                        GpuVaultOp.Store,
                        1f,
                        0),
                    SampleCount = specimens,
                });
                using IDisposable bulletin = readied is { ShouldRender: true }
                    ? _ambit.BroadcastReadied(coder)
                    : _ambit.Publish(coder);
                if (readied is { } val)
                {
                    using IDisposable recipientTicker = coder.CommenceTickerAmbit(
                        RasterizeBundlePerformanceAmbitLabels.EnhancedRealmRecipient);
                    long recipientBegun = Stopwatch.GetTimestamp();
                    try
                    {
                        verdict = ((IPreparedRealmStageFramePhase)_world)
                            .PaintReadiedEnhanced(feed, in val);
                    }
                    finally
                    {
                        recipientCpuBeats += Stopwatch.GetTimestamp() - recipientBegun;
                    }
                }
                else
                {
                    verdict = _world.Render(feed);
                }
            }
            catch (Exception problem) when (VkRenderFailureRule.IsFatal(problem))
            {
                if (readied is { } val
                    && _world is IPreparedRealmStageFramePhase readiedRealm)
                {
                    readiedRealm.AbortReadiedEnhanced(in val);
                }
                _realmTriMeshes?.AbortDirectedShadeXformCycle(cycle);
                throw;
            }
            catch (Exception problem)
            {
                if (readied is { } val
                    && _world is IPreparedRealmStageFramePhase readiedRealm)
                {
                    readiedRealm.AbortReadiedEnhanced(in val);
                }
                _realmTriMeshes?.AbortDirectedShadeXformCycle(cycle);
                _rasterizeBundles?.OnCoreMiss(
                    "Atmospheric world rendering failed: "
                    + problem.GetBaseException().Message);
                return default;
            }

            try
            {
                AtmosphericCycleFeeds atmospheric = _atmosphere.Freeze();
                bundleBegun = Stopwatch.GetTimestamp();
                graph.PaintPostProc(cycle, in atmospheric);
                bundleCpuBeats += Stopwatch.GetTimestamp() - bundleBegun;
                var observation = new RasterizeBundleCyclePerformanceObservation(
                    PackAddedCpuMilliseconds: bundleCpuBeats * 1000d / Stopwatch.Frequency,
                    StableFrameBoundary: verdict.NormalWorldDrawn,
                    feed.ViewportWidth,
                    feed.ViewportHeight,
                    specimens,
                    AbsoluteEnhancedWorldReceiverCpuMilliseconds:
                        recipientCpuBeats * 1000d / Stopwatch.Frequency);
                bool observationSucceeded = false;
                long watchBegun = profileCpuJunctures ? Stopwatch.GetTimestamp() : 0L;
                try
                {
                    _rasterizeBundles.WatchEngagedCycle(in observation);
                    observationSucceeded = true;
                }
                catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
                {
                    _rasterizeBundles.OnCoreMiss(
                        "Atmospheric performance observation failed: "
                        + problem.GetBaseException().Message);
                }
                long watchBookkeepingBeats = profileCpuJunctures
                    ? Stopwatch.GetTimestamp() - watchBegun
                    : 0L;
                if (observationSucceeded && profileCpuJunctures)
                {
                    cpuJunctureProfile!.ConcludeCpuProfile(
                        cycle.SerialNo,
                        markPrepBeats,
                        bundleCpuBeats,
                        watchBookkeepingBeats,
                        verdict.NormalWorldDrawn);
                }
                return verdict;
            }
            catch (Exception problem) when (!VkRenderFailureRule.IsFatal(problem))
            {
                _rasterizeBundles.OnCoreMiss(
                    "Atmospheric post-processing failed: "
                    + problem.GetBaseException().Message);
                return verdict;
            }
        }

        return RenderRetail(cycle, feed);
    }

    private RealmRenderFrameVerdict RenderRetail(
        IGpuCycle cycle,
        RasterizeCycleFeed feed)
    {
        int specimens = _specimenTally();
        using IGpuSweepCoder coder = cycle.BeginPass(new GpuPassSpec
        {
            Name = "vk-world",
            Color = new GpuTintAffix(
                Target: null,
                Load: GpuPullOp.Clear,
                Store: specimens > 1 ? GpuVaultOp.Resolve : GpuVaultOp.Store,
                ClearColor: _wipe.WipeTint),
            ZDepth = new GpuZDepthAffix(
                Load: GpuPullOp.Clear,
                Store: GpuVaultOp.DontCare,
                ClearDepth: 1f,
                ClearStencil: 0),
            SampleCount = specimens,
        });

        using IDisposable bulletin = _ambit.Publish(coder);
        return _world.Render(feed);
    }
}

internal sealed class VkBackbufferClearLedger
{
    internal System.Numerics.Vector4 WipeTint { get; set; } = new(0f, 0f, 0f, 1f);

    internal RasterizeCycleFoundation Foundation { get; set; }
}

internal sealed class NullRasterizeCycleGpuReading : IRasterizeCycleGpuReading
{
    public static NullRasterizeCycleGpuReading Instance { get; } = new();

    private NullRasterizeCycleGpuReading()
    {
    }

    public void BeginFrame()
    {
    }

    public void EndFrame()
    {
    }
}
