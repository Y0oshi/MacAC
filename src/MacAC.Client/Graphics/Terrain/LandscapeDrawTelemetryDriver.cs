using System.Diagnostics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Paging;
using MacAC.Client.Realm;

namespace MacAC.Client.Graphics;

internal readonly record struct FramePipelineTelemetryFacts(
    LandblockDisplayTelemetry Presentation,
    ClientRollingTimingPercentiles UploadTiming,
    int EntitiesWalked,
    int DeferredApplyBacklog,
    int FullWindowRetirementCount,
    int LastFullWindowRetirementLandblockCount,
    int MaterializedEntityCount,
    int SpawnSnapshotCount,
    int ResidentEntityCount);

internal interface IFramePipelineTelemetryFactsSource
{
    LandscapeRenderTelemetryFacts GrabLand();

    FramePipelineTelemetryFacts GrabCycle();
}

internal sealed class EngineFramePipelineTelemetryFactsSource(
    LandModernPainter? land,
    LandblockDisplayPipeline? exhibit,
    EngineRenderFrameOnlinePreparation? onlinePrep,
    RealmPaintRouter? router,
    PagingDriver? paging,
    OnlineActorCore liveEntities,
    GpuRealmPhase world) :
    IFramePipelineTelemetryFactsSource
{
    private readonly LandModernPainter? _land = land;
    private readonly LandblockDisplayPipeline? _exhibit = exhibit;
    private readonly EngineRenderFrameOnlinePreparation? _onlinePrep = onlinePrep;
    private readonly RealmPaintRouter? _router = router;
    private readonly PagingDriver? _paging = paging;
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly GpuRealmPhase _world = world ?? throw new ArgumentNullException(nameof(world));

    public LandscapeRenderTelemetryFacts GrabLand()
    {
        int strollDraws = _land?.StrollPaintTally ?? 0;
        if (strollDraws > 0)
            return new(_land!.StrollShownSocketTally, strollDraws, _land.FetchedSockets, _land.CapSockets);

        int shownSockets = _land?.ShownSockets ?? 0;
        return new(shownSockets, shownSockets, _land?.FetchedSockets ?? 0, _land?.CapSockets ?? 0);
    }

    public FramePipelineTelemetryFacts GrabCycle()
    {
        return new(
        _exhibit?.Diagnostics ?? default,
        _onlinePrep?.PushTiming ?? default,
        _router?.PreviousPaintStats.EntitiesWalked ?? 0,
        _paging?.PostponedEnactBacklog ?? 0,
        _paging?.WholePaneSunsetTally ?? 0,
        _paging?.PreviousWholePaneSunsetLbTally ?? 0,
        _onlineActors.MaterializedTally,
        _onlineActors.Snapshots.Count,
        _world.Entities.Count);
    }
}

internal sealed class LandscapeDrawTelemetryDriver(
    bool turnedOn,
    RealmRenderTelemetry world,
    IFramePipelineTelemetryFactsSource facts,
    IRenderFrameTelemetryLog log)
{
    internal const long BulletinIntervalMillis = 5_000;

    private readonly bool _turnedOn = turnedOn;
    private readonly RealmRenderTelemetry _world = world ?? throw new ArgumentNullException(nameof(world));
    private readonly IFramePipelineTelemetryFactsSource _facts = facts ?? throw new ArgumentNullException(nameof(facts));
    private readonly IRenderFrameTelemetryLog _trace = log ?? throw new ArgumentNullException(nameof(log));
    private long _previousBulletinMillis;

    private long _strollAccumulatedBeats;

    public void Begin() => _world.CommenceLandPaint();

    public void Complete() => Complete(_turnedOn ? Environment.TickCount64 : 0L);

    public void AmassStrollLot(long passedBeats) => _strollAccumulatedBeats += passedBeats;

    public void FinishStrollCycle() => ConcludeStrollCycle(_turnedOn ? Environment.TickCount64 : 0L);

    internal void Complete(long instantMillis)
    {
        _world.FinishLandPaint();
        BroadcastIfDue(instantMillis);
    }

    internal void ConcludeStrollCycle(long instantMillis)
    {
        double beatsToHundredthsMicroseconds = 1_000_000.0 * 100.0 / Stopwatch.Frequency;
        _world.PushLandSpecimen(
            (long)(_strollAccumulatedBeats * beatsToHundredthsMicroseconds));
        _strollAccumulatedBeats = 0;
        BroadcastIfDue(instantMillis);
    }

    internal static string ComposeCycle(FramePipelineTelemetryFacts facts)
    {
        var rasterize = facts.Presentation.Render;
        var kinetics = facts.Presentation.Physics;
        var statics = facts.Presentation.Statics;
        double beatsToMicros = 1_000_000.0 / Stopwatch.Frequency;
        double pushMedian = facts.UploadTiming.MedianHundredthsMicroseconds / 100.0;
        double pushP95 = facts.UploadTiming.Percentile95HundredthsMicroseconds / 100.0;
        return
            $"[FRAME-DIAG] publish={rasterize.BeginCount}/{rasterize.CompleteCount} "
            + $"render_total_us=[terrain={rasterize.TerrainPublishTicks * beatsToMicros:F1} "
            + $"begin={rasterize.BeginPublishTicks * beatsToMicros:F1} "
            + $"complete={rasterize.CompletePublishTicks * beatsToMicros:F1}] "
            + $"physics_total_us=[base={kinetics.BasePublishTicks * beatsToMicros:F1} "
            + $"gfx={kinetics.GfxCacheTicks * beatsToMicros:F1} "
            + $"complete={kinetics.CompletePublishTicks * beatsToMicros:F1}] "
            + $"static={statics.BeginCount}/{statics.CompleteCount}"
            + $"(active={statics.ActiveEntityCount})  "
            + $"entUpl_us={pushMedian:F1}m/{pushP95:F1}p95  "
            + $"deferred={facts.DeferredApplyBacklog}  "
            + $"fullRetire={facts.FullWindowRetirementCount}"
            + $"(landblocks={facts.LastFullWindowRetirementLandblockCount})  "
            + $"esg={facts.MaterializedEntityCount} spawn={facts.SpawnSnapshotCount} "
            + $"resident={facts.ResidentEntityCount} walked={facts.EntitiesWalked}";
    }

    private void BroadcastIfDue(long instantMillis)
    {
        if (!_turnedOn
            || instantMillis - _previousBulletinMillis <= BulletinIntervalMillis)

            return;

        _world.BroadcastLandTelemetry(_facts.GrabLand());
        _trace.WriteLine(ComposeCycle(_facts.GrabCycle()));
        _previousBulletinMillis = instantMillis;
    }
}
