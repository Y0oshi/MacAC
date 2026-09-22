using MacAC.Client.Controls;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Paging;
using MacAC.Client.Realm;
using MacAC.Client.Telemetry;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal readonly record struct RasterizeCycleFoundation(
    bool PortalViewportVisible,
    SkyKeyframe Sky,
    AtmosphereFrame Atmosphere)
{
    public bool EnvironOverrideActive =>
        Atmosphere.Override != MechEnvironOverride.None;
}

internal interface IRasterizeCycleCommenceAssetList
{
    void Begin(int gpuSocket);
}

internal interface IRasterizeCycleSocketOrigin
{
    int LatestSocket { get; }
}

internal interface IRasterizeCycleWipeStage
{
    RasterizeCycleFoundation Clear();
}

internal interface IRasterizeCycleFoundationOrigin
{
    RasterizeCycleFoundation Foundation { get; }
}

internal interface IRenderFrameOnlinePreparation
{
    void Prepare(int gpuSocket);
}

internal sealed class RenderFrameResourceDriver(
    IRasterizeCycleSocketOrigin frameSlots,
    IRasterizeCycleCommenceAssetList resources,
    IRasterizeCycleWipeStage clear,
    IRenderFrameOnlinePreparation live) :
    IRasterizeCycleAssetStage,
    IRasterizeCycleFoundationOrigin
{
    private readonly IRasterizeCycleSocketOrigin _cycleSockets = frameSlots
            ?? throw new ArgumentNullException(nameof(frameSlots));
    private readonly IRasterizeCycleCommenceAssetList _assetList = resources ?? throw new ArgumentNullException(nameof(resources));
    private readonly IRasterizeCycleWipeStage _wipe = clear ?? throw new ArgumentNullException(nameof(clear));
    private readonly IRenderFrameOnlinePreparation _online = live ?? throw new ArgumentNullException(nameof(live));

    public RasterizeCycleFoundation Foundation { get; private set; }

    public void Prepare(RasterizeCycleFeed feed)
    {
        int gpuSocket = _cycleSockets.LatestSocket;
        _assetList.Begin(gpuSocket);
        Foundation = _wipe.Clear();
        _online.Prepare(gpuSocket);
    }
}

internal sealed class EngineRenderFrameBeginResources(
    BitmapStash? textures,
    RealmPaintRouter? router,
    EnvironChamberPainter? surroundingsChambers,
    GatewayZDepthBitmaskPainter? gatewayZDepth,
    ClipCycle? clip,
    LandModernPainter? land,
    StageLightingUboWiring? illumination) : IRasterizeCycleCommenceAssetList
{
    private readonly BitmapStash? _textures = textures;
    private readonly RealmPaintRouter? _router = router;
    private readonly EnvironChamberPainter? _surroundingsChambers = surroundingsChambers;
    private readonly GatewayZDepthBitmaskPainter? _gatewayZDepth = gatewayZDepth;
    private readonly ClipCycle? _clip = clip;
    private readonly LandModernPainter? _land = land;
    private readonly StageLightingUboWiring? _illumination = illumination;

    public void Begin(int gpuSocket)
    {
        _textures?.CommenceCompoundTextureCycle();
        _textures?.PulseCompoundTextureStash();
        _router?.BeginFrame(gpuSocket);
        _surroundingsChambers?.BeginFrame(gpuSocket);
        _gatewayZDepth?.BeginFrame(gpuSocket);
        _clip?.BeginFrame(gpuSocket);
        _land?.BeginFrame(gpuSocket);
        _illumination?.BeginFrame(gpuSocket);
    }
}

internal interface IRasterizeCycleGatewayPhaseOrigin
{
    bool IsGatewayViewRectShown { get; }

    uint EngagedDestChamber { get; }
}

internal sealed class AvatarWarpRenderStateSource(
    AvatarWarpDriver teleport,
    IRenderSignInStateSource login)
        : IRasterizeCycleGatewayPhaseOrigin
{
    private readonly AvatarWarpDriver _warp = teleport ?? throw new ArgumentNullException(nameof(teleport));
    private readonly IRenderSignInStateSource _signin = login ?? throw new ArgumentNullException(nameof(login));

    public bool IsGatewayViewRectShown =>
        _warp.IsPortalViewRectVisible || _signin.IsWaitingForSignin;

    public uint EngagedDestChamber => _warp.EngagedDestinationCell;
}

internal interface IRenderSignInStateSource
{
    bool IsWaitingForSignin { get; }
}

internal sealed class RenderSignInStateSource : IRenderSignInStateSource
{
    private readonly IAvatarModeSource _mode;

    public RenderSignInStateSource(bool onlineManner, IAvatarModeSource mode)
    {
        IsWaitingForSignin = onlineManner;
        _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    }

    public bool IsWaitingForSignin => field && !_mode.PursueMannerEverEntered;
}

internal interface ISignInRevealCellSource
{
    bool TryGet(out uint chamberIdent);
}

internal sealed class OnlineSignInRevealCellSource(
    OnlineActorCore entities,
    IAvatarIdentitySource identity) : ISignInRevealCellSource
{
    private readonly OnlineActorCore _actors = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    public bool TryGet(out uint chamberIdent)
    {
        chamberIdent = 0;
        if (!_actors.TryGetCapture(_identity.SrvOid, out var avatarSummon)
            || avatarSummon.Position is not { } locus)

            return false;

        chamberIdent = locus.LandblockId;
        return chamberIdent is not 0;
    }
}

// Render-thread mesh publication, reveal evaluation, and VFX begin
internal sealed class EngineRenderFrameOnlinePreparation(
    BitmapStash? textures,
    RealmTriMeshBridge? triMeshes,
    RealmRevealMarshal? realmUnveil,
    IRasterizeCycleGatewayPhaseOrigin portal,
    IRenderSignInStateSource login,
    ISignInRevealCellSource loginCell,
    MotePainter? motes,
    CycleProfiler profiler,
    bool telemetryTurnedOn)
        : IRenderFrameOnlinePreparation
{
    private readonly BitmapStash? _textures = textures;
    private readonly RealmTriMeshBridge? _triMeshes = triMeshes;
    private readonly RealmRevealMarshal? _worldReveal = realmUnveil;
    private readonly IRasterizeCycleGatewayPhaseOrigin _gateway = portal ?? throw new ArgumentNullException(nameof(portal));
    private readonly IRenderSignInStateSource _signin = login ?? throw new ArgumentNullException(nameof(login));
    private readonly ISignInRevealCellSource _signinChamber = loginCell ?? throw new ArgumentNullException(nameof(loginCell));
    private readonly MotePainter? _motes = motes;
    private readonly CycleProfiler _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
    private readonly bool _telemetryTurnedOn = telemetryTurnedOn;
    private readonly RollingTimingSpecimenPane _pushTiming = new(256);

    public ClientRollingTimingPercentiles PushTiming => _pushTiming.Freeze();

    public void Prepare(int gpuSocket)
    {
        _textures?.PulseCanvasHistogramPrintIfTurnedOn();
        long begin = _telemetryTurnedOn
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;
        using (var pushJuncture = _profiler.BeginStage(CycleJuncture.Upload))
        {
            _triMeshes?.Tick();
            uint unveilChamber = _gateway.EngagedDestChamber;
            if (unveilChamber is 0
                && _signin.IsWaitingForSignin
                && _signinChamber.TryGet(out uint signinChamber))

                unveilChamber = signinChamber;

            if (unveilChamber is not 0)
                _worldReveal?.ReadyAndEvaluate(unveilChamber);
            _motes?.BeginFrame(gpuSocket);
        }

        if (_telemetryTurnedOn)
        {
            _pushTiming.PushStopwatchBeats(
                System.Diagnostics.Stopwatch.GetTimestamp() - begin);
        }
    }
}

internal sealed class RenderWeatherFrameDriver(
    WorldClock worldTime,
    WeatherEngine weather) : IRasterizeWeatherCycleStage
{
    private readonly WorldClock _realmMoment = worldTime ?? throw new ArgumentNullException(nameof(worldTime));
    private readonly WeatherEngine _weather = weather ?? throw new ArgumentNullException(nameof(weather));

    private double _elapsedSeconds;

    internal double PassedSecs => _elapsedSeconds;

    public void Tick(double diffSecs)
    {
        var calendar = _realmMoment.LatestCalendar;
        int dayOrdinal = calendar.Year
            * (DerethDateMoment.DaysInAMonth * DerethDateMoment.MonthsInAYear)
            + (int)calendar.Month * DerethDateMoment.DaysInAMonth
            + (calendar.Day - 1);
        _weather.Tick(
            instantSecs: _elapsedSeconds,
            dayOrdinal,
            dtSecs: (float)diffSecs);
        _elapsedSeconds += diffSecs;
    }
}
