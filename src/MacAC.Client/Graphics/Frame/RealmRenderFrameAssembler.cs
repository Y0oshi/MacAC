using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Paging;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Client.Sound;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Illumination;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Sound;

namespace MacAC.Client.Graphics;

internal readonly record struct RealmCameraFrame(
    IClientCamera Camera,
    Matrix4x4 Projection,
    Matrix4x4 LensMirror,
    FrustumFacets Frustum,
    Matrix4x4 InverseView,
    Vector3 Position)
{
    public bool IsOverheadLens { get; init; }
}

internal static class RealmCameraViewRule
{
    internal static bool IsOverheadLens(IClientCamera engagedCam) =>
        engagedCam is CanonFollowCamera { IsLookupManner: true }
            or FollowCamera { IsLookupMode: true };

    internal static AtmosphereFrame Apply(
        in AtmosphereFrame atmosphere,
        bool gapFogDisabled) =>
        gapFogDisabled
            ? atmosphere with { FogMode = FogManner.Off }
            : atmosphere;
}

internal readonly record struct RealmRootFrame(
    FetchedChamber? PlayerRoot,
    bool PlayerSeenOutside,
    uint ViewerCellId,
    Vector3 ViewerEyePosition,
    Vector3 PlayerViewPosition,
    FetchedChamber? ViewerRoot,
    bool CameraInsideCell,
    bool RootSeenOutside,
    bool PlayerInsideCell,
    uint? PlayerLandblockId,
    int RenderCenterLandblockX,
    int RenderCenterLandblockY,
    uint PlayerCellId,
    bool PlayerIndoorGate)
{
    public bool RasterizeSky => ViewerRoot is null || RootSeenOutside;

    public bool HeavensFxListEngaged => RasterizeSky && !PlayerInsideCell;

    public bool CamInsideEnclosedChamber => CameraInsideCell && !RootSeenOutside;

    // Environment gate shared by directional shadows and other outdoor-only render-pack effects
    public bool AvatarOrCamInsideEnclosedChamber =>
        PlayerInsideCell || CamInsideEnclosedChamber;

    public bool IsAtmosphericallyExterior =>
        RasterizeSky && !CamInsideEnclosedChamber;
}

// Borrowed building scratch, valid only until the next build
internal readonly record struct RealmBuildingFrame(
    FetchedChamber? OutdoorNode,
    IReadOnlyList<FetchedChamber> NearbyBuildingCells);

internal readonly record struct RealmRenderFrame(
    RealmCameraFrame Camera,
    RealmRootFrame Roots,
    RealmBuildingFrame Buildings,
    HashSet<uint> AnimatedEntityIds)
{
    public FetchedChamber? ClipTrunk => Roots.ViewerRoot ?? Buildings.OutdoorNode;

    public ResidentPagingWindowFact HousedPagingPane { get; init; }

    public AuthoredCelestialShadeSource CelestialShadeSrc { get; init; }

    public CanonLandscapeVisibilityFrame PriorLandscapeVisibility { get; init; }

    public IDirectionalShadeCellMembership? DirectionalShadowCellMembership
    { get; init; }
}

internal interface IDirectionalShadeCellMembership
{
    bool TryFetchCanonChamberArr(uint actorIdent, out IReadOnlyList<uint> chambers);
}

internal sealed class EmptyDirectionalShadeCellMembership
    : IDirectionalShadeCellMembership
{
    internal static EmptyDirectionalShadeCellMembership Instance { get; } = new();

    public bool TryFetchCanonChamberArr(
        uint actorIdent,
        out IReadOnlyList<uint> chambers)
    {
        _ = actorIdent;
        chambers = Array.Empty<uint>();
        return false;
    }
}

internal sealed class EngineDirectionalShadeCellMembership(
    ProxyRegistry source) : IDirectionalShadeCellMembership
{
    private readonly ProxyRegistry _src = source
        ?? throw new ArgumentNullException(nameof(source));

    public bool TryFetchCanonChamberArr(
        uint actorIdent,
        out IReadOnlyList<uint> chambers) =>
        _src.TryFetchRetailCellArray(actorIdent, out chambers);
}

internal interface IRealmRenderFrameAssembler
{
    RealmRenderFrame Build(
        in RasterizeCycleFoundation foundation,
        bool waitingForSignin,
        DayGroupRow? engagedDayCluster);

}

internal interface IRealmFrameCameraSource
{
    RealmCameraFrame Resolve();
}

internal interface IRealmFrameRootSource
{
    RealmRootFrame Resolve(in RealmCameraFrame cam);
}

internal interface IRealmFrameVisibilityPreparation
{
    CanonLandscapeVisibilityFrame GrabFinishedSceneryVis();

    void Begin(in RealmCameraFrame cam, bool waitingForSignin);

    void BroadcastLensProj(in RealmCameraFrame cam);
}

internal interface IRealmFramePreferencesPreview
{
    void Apply(in RealmCameraFrame cam);
}

internal interface IRealmFrameEnvironmentPreparation
{
    void Prepare(
        in RealmCameraFrame cam,
        in RealmRootFrame trunks,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster);

}

internal interface IHeavensPesActivationTurnstile
{
    void Tick();
}

internal sealed class EngineHeavensPesActivationTurnstile(
    HeavensPesFrameDriver skyPes,
    IRealmFrameCameraSource camera,
    IRealmFrameRootSource roots)
        : IHeavensPesActivationTurnstile
{
    private readonly HeavensPesFrameDriver _heavensPes = skyPes ?? throw new ArgumentNullException(nameof(skyPes));
    private readonly IRealmFrameCameraSource _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IRealmFrameRootSource _trunks = roots ?? throw new ArgumentNullException(nameof(roots));

    public void Tick()
    {
        RealmCameraFrame cam = _cam.Resolve();
        RealmRootFrame trunks = _trunks.Resolve(in cam);
        _heavensPes.AssignEngaged(trunks.HeavensFxListEngaged);
    }
}

internal interface IRealmFrameAnimatedActorSource
{
    HashSet<uint> Capture();
}

internal interface IRealmFrameBuildingSource
{
    RealmBuildingFrame Collect(
        FetchedChamber? beholderTrunk,
        uint beholderChamberIdent,
        in FrustumFacets frustum);
}

internal sealed class RealmRenderFrameAssembler(
    IRealmFrameCameraSource camera,
    IRealmFrameVisibilityPreparation visibility,
    IRealmFramePreferencesPreview settings,
    IRealmFrameRootSource roots,
    IRealmFrameEnvironmentPreparation environment,
    IRealmFrameAnimatedActorSource animated,
    IRealmFrameBuildingSource buildings,
    IDirectionalShadeCellMembership directionalShadowCells) : IRealmRenderFrameAssembler
{
    private readonly IRealmFrameCameraSource _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IRealmFrameVisibilityPreparation _vis = visibility ?? throw new ArgumentNullException(nameof(visibility));
    private readonly IRealmFramePreferencesPreview _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly IRealmFrameRootSource _trunks = roots ?? throw new ArgumentNullException(nameof(roots));
    private readonly IRealmFrameEnvironmentPreparation _surroundings = environment ?? throw new ArgumentNullException(nameof(environment));
    private readonly IRealmFrameAnimatedActorSource _moving = animated ?? throw new ArgumentNullException(nameof(animated));
    private readonly IRealmFrameBuildingSource _structures = buildings ?? throw new ArgumentNullException(nameof(buildings));
    private readonly IDirectionalShadeCellMembership _directedShadeChambers = directionalShadowCells
            ?? throw new ArgumentNullException(nameof(directionalShadowCells));

    public RealmRenderFrame Build(
        in RasterizeCycleFoundation foundation,
        bool waitingForSignin,
        DayGroupRow? engagedDayCluster)
    {
        CanonLandscapeVisibilityFrame precedingSceneryVis =
            _vis.GrabFinishedSceneryVis();
        if (waitingForSignin)
            precedingSceneryVis = CanonLandscapeVisibilityFrame.None;
        RealmCameraFrame cam = _cam.Resolve();
        _vis.Begin(in cam, waitingForSignin);
        _prefs.Apply(in cam);
        RealmRootFrame trunks = _trunks.Resolve(in cam);
        _surroundings.Prepare(in cam, in trunks, in foundation, engagedDayCluster);
        _vis.BroadcastLensProj(in cam);
        HashSet<uint> moving = _moving.Capture();
        FrustumFacets frustum = cam.Frustum;
        RealmBuildingFrame structures = _structures.Collect(
            trunks.ViewerRoot,
            trunks.ViewerCellId,
            in frustum);
        return new RealmRenderFrame(cam, trunks, structures, moving)
        {
            PriorLandscapeVisibility = precedingSceneryVis,
            DirectionalShadowCellMembership = _directedShadeChambers,
        };
    }

}

internal sealed class EngineRealmFrameCameraSource(
    CameraDriver cameras,
    Func<IClientCamera, IClientCamera> applyViewPlane) : IRealmFrameCameraSource
{
    private readonly CameraDriver _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
    private readonly Func<IClientCamera, IClientCamera> _enactLensPlane = applyViewPlane
            ?? throw new ArgumentNullException(nameof(applyViewPlane));

    public RealmCameraFrame Resolve()
    {
        IClientCamera engagedCam = _cameras.Active;
        bool overheadLens = RealmCameraViewRule.IsOverheadLens(engagedCam);
        IClientCamera cam = _enactLensPlane(engagedCam);
        Matrix4x4 proj = cam.Projection;
        Matrix4x4 lensProj = cam.View * proj;
        var frustum = FrustumFacets.FromLensProj(lensProj);
        Matrix4x4.Invert(cam.View, out Matrix4x4 invLens);
        var locus = new Vector3(invLens.M41, invLens.M42, invLens.M43);
        return new RealmCameraFrame(
            cam,
            proj,
            lensProj,
            frustum,
            invLens,
            locus)
        {
            IsOverheadLens = overheadLens,
        };
    }
}

internal sealed class EngineRealmFrameRootSource(
    KineticEngine physics,
    ChamberVis cells,
    IAvatarModeSource mode,
    IFollowCameraSource chase,
    ISimAvatarDriverSource player,
    OnlineRealmOriginLedger origin) : IRealmFrameRootSource
{
    private const float LbDims = 192f;

    private readonly KineticEngine _physics = physics ?? throw new ArgumentNullException(nameof(physics));
    private readonly ChamberVis _chambers = cells ?? throw new ArgumentNullException(nameof(cells));
    private readonly IAvatarModeSource _mode = mode ?? throw new ArgumentNullException(nameof(mode));
    private readonly IFollowCameraSource _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
    private readonly ISimAvatarDriverSource _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));

    public RealmRootFrame Resolve(in RealmCameraFrame cam)
    {
        FetchedChamber? avatarTrunk = null;
        if (_physics.DataCache?.ChamberGraph.CurrChamber is MacAC.Mechanics.Realm.Cells.EnvCell avatarChamber
            && _chambers.TryFetchChamber(avatarChamber.Id, out FetchedChamber? registeredAvatar))
        {
            avatarTrunk = registeredAvatar;
        }

        bool avatarObservedBeyond = avatarTrunk?.SeenOutside ?? true;
        uint beholderChamberIdent = _mode.IsPlayerMode
            && _pursue.Retail is { } canonPursue
            && CameraTelemetry.UseCanonPursueCam
                ? canonPursue.BeholderChamberIdent
                : avatarTrunk?.CellId ?? 0u;
        FetchedChamber? beholderTrunk = null;
        if (beholderChamberIdent != 0u
            && _chambers.TryFetchChamber(beholderChamberIdent, out FetchedChamber? registeredBeholder))
        {
            beholderTrunk = registeredBeholder;
        }

        var avatar = _avatar.Controller;
        Vector3 avatarLensLocus = avatar?.RasterizeLocus
            ?? avatar?.Position
            ?? cam.Position;
        bool camInsideChamber = beholderTrunk is not null;
        bool trunkObservedBeyond = beholderTrunk?.SeenOutside ?? true;
        bool avatarInsideChamber = avatarTrunk is not null && !avatarObservedBeyond;

        uint? avatarLbIdent = null;
        if (_mode.IsPlayerMode && avatar is not null)
        {
            int avatarX = _origin.CenterX + (int)Math.Floor(avatar.Position.X / LbDims);
            int avatarY = _origin.CenterY + (int)Math.Floor(avatar.Position.Y / LbDims);
            avatarLbIdent = (uint)((avatarX << 24) | (avatarY << 16) | 0xFFFF);
        }

        int rasterizeMiddleX = _origin.CenterX
            + (int)Math.Floor(cam.Position.X / LbDims);
        int rasterizeMiddleY = _origin.CenterY
            + (int)Math.Floor(cam.Position.Y / LbDims);
        uint avatarChamberIdent = _physics.DataCache?.ChamberGraph.CurrChamber?.Id ?? 0u;
        bool avatarInsideLatch = DrawTelemetry.ShouldRasterizeInside(
            avatarChamberIdent,
            avatarTrunk is not null);

        return new RealmRootFrame(
            avatarTrunk,
            avatarObservedBeyond,
            beholderChamberIdent,
            cam.Position,
            avatarLensLocus,
            beholderTrunk,
            camInsideChamber,
            trunkObservedBeyond,
            avatarInsideChamber,
            avatarLbIdent,
            rasterizeMiddleX,
            rasterizeMiddleY,
            avatarChamberIdent,
            avatarInsideLatch);
    }
}

internal sealed class EngineRealmFrameVisibilityPreparation
    : IRealmFrameVisibilityPreparation
{
    private readonly CanonPickingStage? _pick;
    private readonly MoteVisibilityDriver _motes;
    private readonly RealmRevealMarshal? _unveil;
    private readonly BatchFrustum? _surroundingsFrustum;

    public EngineRealmFrameVisibilityPreparation(
        CanonPickingStage? pick,
        MoteVisibilityDriver particles,
        LandModernPainter? land,
        RealmRevealMarshal? unveil,
        BatchFrustum? surroundingsFrustum)
    {
        _pick = pick;
        _motes = particles ?? throw new ArgumentNullException(nameof(particles));
        _ = land;
        _unveil = unveil;
        _surroundingsFrustum = surroundingsFrustum;
    }

    public CanonLandscapeVisibilityFrame GrabFinishedSceneryVis() =>
        _motes.GrabFinishedSceneryVis();

    public void Begin(in RealmCameraFrame cam, bool waitingForSignin)
    {
        _pick?.AssignLensFrustum(cam.Frustum);
        _motes.BeginFrame(cam.Position);
        if (waitingForSignin)
            return;

        _motes.EmployRealmLens();
        _unveil?.WatchRealmViewRectShown();
    }

    public void BroadcastLensProj(in RealmCameraFrame cam) =>
        _surroundingsFrustum?.Update(cam.LensMirror);
}

internal sealed class EngineRealmFramePreferencesPreview(
    IEnginePreferencesPreviewSource settings,
    OpenAlSoundEngine? sound,
    CameraDriver cameras,
    DisplayFramePacingDriver pacing) : IRealmFramePreferencesPreview
{
    private readonly IEnginePreferencesPreviewSource _prefs = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly OpenAlSoundEngine? _sound = sound;
    private readonly CameraDriver _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
    private readonly DisplayFramePacingDriver _pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));

    public void Apply(in RealmCameraFrame cam)
    {
        if (_prefs.HasDraftPreview)
        {
            EnginePreferencesStartupTargets.ImposeSound(
                _sound,
                _prefs.SoundPreview);
            var readout = _prefs.ReadoutPreview;
            EnginePreferencesStartupTargets.ImposeFieldOfLens(
                _cameras,
                readout.FieldOfView);
            _pacing.ImposePreference(readout.VSync);
        }

        if (_sound is not { IsAvailable: true })
            return;

        Matrix4x4 inv = cam.InverseView;
        var ahead = new Vector3(-inv.M31, -inv.M32, -inv.M33);
        Vector3 locus = cam.Position;
        _sound.AssignListener(
            locus.X, locus.Y, locus.Z,
            CanonMixer.CompassBearingDeg(Vector3.Zero, ahead));
    }
}

internal interface IRealmRenderRangeSource
{
    int NearbyRadius { get; }

    int FarawayRadius { get; }
}

internal sealed class RealmRenderRangeLedger(int nearbyRadius, int farawayRadius) : IRealmRenderRangeSource
{
    public int NearbyRadius { get; set; } = nearbyRadius;

    public int FarawayRadius { get; set; } = farawayRadius;
}

internal sealed class EngineRealmFrameEnvironmentPreparation(
    EngineKnobs options,
    WorldClock worldTime,
    LightKeeper lighting,
    RealmPaintRouter? router,
    EnvironChamberPainter? surroundingsChambers,
    StageLightingUboWiring? illuminationUbo,
    IRealmRenderRangeSource ranges,
    HeavensPesFrameDriver? heavensPes,
    Func<bool>? persistentDaylight = null)
        : IRealmFrameEnvironmentPreparation
{
    private const float LbDims = 192f;

    private readonly EngineKnobs _knobs = options ?? throw new ArgumentNullException(nameof(options));
    private readonly WorldClock _realmMoment = worldTime ?? throw new ArgumentNullException(nameof(worldTime));
    private readonly LightKeeper _illumination = lighting ?? throw new ArgumentNullException(nameof(lighting));
    private readonly RealmPaintRouter? _router = router;
    private readonly EnvironChamberPainter? _surroundingsChambers = surroundingsChambers;
    private readonly StageLightingUboWiring? _illuminationUbo = illuminationUbo;
    private readonly IRealmRenderRangeSource _spans = ranges ?? throw new ArgumentNullException(nameof(ranges));
    private readonly HeavensPesFrameDriver? _heavensPes = heavensPes;
    private readonly Func<bool> _persistentDaylight = persistentDaylight ?? (static () => false);

    public void Prepare(
        in RealmCameraFrame cam,
        in RealmRootFrame trunks,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster)
    {
        _heavensPes?.Update(
            (float)_realmMoment.DayFraction,
            engagedDayCluster,
            cam.Position,
            trunks.HeavensFxListEngaged);

        SkyKeyframe sceneryIllumination = _persistentDaylight()
            ? _realmMoment.HeavensAtDayRatio(0.5f)
            : foundation.Sky;
        UpdateSunFromSky(sceneryIllumination, trunks.PlayerInsideCell);
        _illumination.RefreshBeholderLamp(trunks.PlayerViewPosition);
        _illumination.Tick(cam.Position);
        _illumination.AssemblePtLampCapture(trunks.PlayerViewPosition);
        _router?.AssignTableauLamps(_illumination.PtCapture);
        _surroundingsChambers?.AssignPtCapture(_illumination.PtCapture);

        AtmosphereFrame atmosphere = RealmCameraViewRule.Apply(
            foundation.Atmosphere,
            cam.IsOverheadLens);
        var ubo = SceneLightBlock.Build(
            _illumination,
            in atmosphere,
            cam.Position,
            (float)_realmMoment.DayFraction);
        _illuminationUbo?.Upload(ubo);
    }

    private void UpdateSunFromSky(SkyKeyframe keyframe, bool avatarInsideChamber)
    {
        Vector3 sunToRealm = -SkyStateSource.SunDirFromKeyframe(keyframe);
        if (avatarInsideChamber)
        {
            _illumination.Sun = new LightEmitter
            {
                Kind = LampFlavor.Directional,
                RealmAhead = sunToRealm,
                TintLinear = Vector3.Zero,
                Intensity = 0f,
                Range = 1f,
            };
            _illumination.LatestAmbient = new CellAmbientLight(
                new Vector3(0.20f, 0.20f, 0.20f),
                Vector3.Zero,
                sunToRealm);
            return;
        }

        _illumination.Sun = new LightEmitter
        {
            Kind = LampFlavor.Directional,
            RealmAhead = sunToRealm,
            TintLinear = keyframe.SunColor,
            Intensity = 1f,
            Range = 1f,
        };
        _illumination.LatestAmbient = new CellAmbientLight(
            keyframe.AmbientColor,
            keyframe.SunColor,
            sunToRealm);
    }
}

internal sealed class EngineRealmFrameAnimatedActorSource(
    OnlineActorMotionEngineView<OnlineActorMotionLedger> live,
    CanonStaticAnimatingObjectRota? statics,
    EquippedChildRenderDriver? equipped)
        : IRealmFrameAnimatedActorSource
{
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger> _online = live ?? throw new ArgumentNullException(nameof(live));
    private readonly CanonStaticAnimatingObjectRota? _statics = statics;
    private readonly EquippedChildRenderDriver? _equipped = equipped;
    private readonly HashSet<uint> _temp = [];

    public HashSet<uint> Capture()
    {
        _online.DuplicateSpatialIdentsTo(_temp);
        _statics?.DuplicateMovingActorIdentsTo(_temp);
        if (_equipped is not null)
        {
            foreach (uint actorIdent in _equipped.AffixedActorIdents)
                _temp.Add(actorIdent);
        }

        return _temp;
    }
}

internal sealed class EngineRealmFrameBuildingSource(
    LandblockDisplayPipeline presentation,
    ChamberVis cells) : IRealmFrameBuildingSource
{
    private readonly LandblockDisplayPipeline _exhibit = presentation
            ?? throw new ArgumentNullException(nameof(presentation));
    private readonly ChamberVis _chambers = cells ?? throw new ArgumentNullException(nameof(cells));
    private readonly List<FetchedChamber> _temp = [];

    public RealmBuildingFrame Collect(
        FetchedChamber? beholderTrunk,
        uint beholderChamberIdent,
        in FrustumFacets frustum)
    {
        _temp.Clear();
        FetchedChamber? exteriorJoint = null;
        if (beholderTrunk is null && beholderChamberIdent == 0u)
            return new RealmBuildingFrame(null, _temp);

        foreach (StructureRegistry registry in _exhibit.StructureRegistries)
        {
            foreach (Structure structure in registry.All())
            {
                if (structure.HasGatewayLimits
                    && !FrustumPruner.IsAabbShown(
                        frustum,
                        structure.GatewayLimits.Min,
                        structure.GatewayLimits.Max))
                {
                    continue;
                }

                foreach (uint chamberIdent in structure.EnvironChamberIdents)
                {
                    if (_chambers.TryFetchChamber(chamberIdent, out FetchedChamber? chamber) && chamber is not null)
                        _temp.Add(chamber);
                }
            }
        }

        if (beholderTrunk is null)
            exteriorJoint = ExteriorChamberJoint.Build(beholderChamberIdent);

        return new RealmBuildingFrame(exteriorJoint, _temp);
    }
}
