using System.Numerics;
using MacAC.Client.Graphics.Stage;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonPViewPainter
{
    private readonly RenderStageShadeEngine _rasterizeTableauShade;

    private readonly ClipCycleAssembly _clipAssemblyTemp = new();

    private readonly CanonPViewFrameResult _cycleOutcomeTemp = new();

    private readonly HashSet<uint> _drawableChambersTemp = [];

    private readonly HashSet<uint> _shownChambersTemp = [];

    private readonly HashSet<uint> _shownSceneryChambersTemp = [];

    private Action? _strollPreWipeDynamics;

    private Stride.StrollCycleDriver? _walkFrameDriverScratch;

    private Stride.StrollProductionCycleCtx? _strollCycleCtxTemp;

    private StrideProductionLeafPainter? _strollLeafPainterTemp;

    private readonly Action _strollDrainSceneryAct;

    private readonly Action _strollWipeInteriorZDepthAct;

    private readonly Func<int> _strollPaintQuitSealsFn;

    private CanonPLensSweepRunner? _engagedStrollPasss;

    private CanonPViewFrameInput? _engagedStrollCycle;

    private ClipCycleAssembly? _engagedStrollClipAssembly;

    private readonly Stride.StrollStructureRegistry _strollStructures;

    private readonly Stride.StrideLandscapeAssembler _strollScenery;

    private readonly ChamberVis _strollChamberRegistry;

    private readonly Stride.StrideProductionRealmData _strollRealmBlob;

    internal CanonPViewPainter(
        RenderStageShadeEngine renderSceneShadow,
        Stride.StrollStructureRegistry walkBuildings,
        Stride.StrideLandscapeAssembler walkLandscape,
        ChamberVis walkCellRegistry,
        ProxyRegistry shadows,
        BuildingDegradeDriver? structureDegrades = null)
    {
        _rasterizeTableauShade = renderSceneShadow
            ?? throw new ArgumentNullException(nameof(renderSceneShadow));
        _strollStructures = walkBuildings
            ?? throw new ArgumentNullException(nameof(walkBuildings));
        _strollScenery = walkLandscape
            ?? throw new ArgumentNullException(nameof(walkLandscape));
        _strollChamberRegistry = walkCellRegistry
            ?? throw new ArgumentNullException(nameof(walkCellRegistry));
        _strollRealmBlob = new Stride.StrideProductionRealmData(
            _strollStructures,
            shadows ?? throw new ArgumentNullException(nameof(shadows)));
        _cycleStroll = structureDegrades is null
            ? new Stride.CanonCycleStroll()
            : new Stride.CanonCycleStroll(structureDegrades);
        _strollDrainSceneryAct = DrainStrollScenery;
        _strollWipeInteriorZDepthAct = WipeStrollInteriorZDepth;
        _strollPaintQuitSealsFn = PaintStrollQuitSeals;
    }

    private const float ExteriorStructureSeedGap = float.PositiveInfinity;

    private readonly Stride.CanonCycleStroll _cycleStroll;

    private void DrainStrollScenery()
    {
        CanonPLensSweepRunner passs = _engagedStrollPasss
            ?? throw new InvalidOperationException(
                "The retained walk leaf has no active pass binding");

        _strollPreWipeDynamics?.Invoke();
        passs.DrainSceneryAlpha();
    }

    private void WipeStrollInteriorZDepth()
    {
        CanonPLensSweepRunner passs = _engagedStrollPasss
            ?? throw new InvalidOperationException(
                "The retained walk leaf has no active pass binding");

        passs.PurgeInteriorZDepth();
    }

    private void ClearWalkFrameBindings()
    {
        _walkFrameDriverScratch?.CancelCycle(wipeKept: false);
        _engagedStrollPasss = null;
        _engagedStrollCycle = null;
        _engagedStrollClipAssembly = null;
    }

    private static RenderFrameTelemetryCounts TraverseProbeCounts(
        RenderMirrorCounts src)
    {
        int dynamics = checked(
            src.LiveDynamicRoot
            + src.ActiveAnimatedStatic
            + src.EquippedChild);
        return new RenderFrameTelemetryCounts(
            src.OutdoorStatic,
            src.IndoorCellStatic,
            dynamics,
            TransformCount: src.Total,
            OpaqueClassificationCount: 0,
            AlphaClassificationCount: 0,
            LightSetCount: 0,
            SelectionPartCount: 0,
            RouteCandidateCount: src.Total,
            EntityCandidateCount: src.Total,
            MeshPartCount: 0);
    }
}

public interface ICanonPViewCellSource
{
    FetchedChamber? Find(uint chamberIdent);
}

public sealed class CanonPViewFrameInput
{
    public FetchedChamber TrunkChamber { get; private set; } = null!;

    public IReadOnlyList<FetchedChamber>? NearbyStructureChambers { get; private set; }

    public Vector3 BeholderEyePtSpot { get; private set; }
    public Matrix4x4 LensMirror { get; private set; }
    public ICanonPViewCellSource Cells { get; private set; } = null!;
    public IClientCamera Camera { get; private set; } = null!;
    public Vector3 CamRealmLocus { get; private set; }
    public FrustumFacets? Frustum { get; private set; }
    public uint? AvatarLbIdent { get; private set; }
    public HashSet<uint>? MovingActorIdents { get; private set; }
    public int RasterizeMiddleLbX { get; private set; }
    public int RasterizeMiddleLbY { get; private set; }
    public int RasterizeRadius { get; private set; }
    public IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
        IReadOnlyList<RealmActor> Entities,
        IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> LbListings
    { get; private set; } = Array.Empty<(uint, Vector3, Vector3,
        IReadOnlyList<RealmActor>,
        IReadOnlyDictionary<uint, RealmActor>?)>();

    public bool RasterizeHeavens { get; private set; }
    public bool RasterizeWeather { get; private set; }
    public bool StructureDegradesDisabled { get; private set; }
    public float DayRatio { get; private set; }
    public DayGroupRow? EngagedDayCluster { get; private set; }
    public SkyKeyframe SkyKeyframe { get; private set; }
    public bool EnvironOverrideEngaged { get; private set; }
    public uint BeholderChamberTag { get; private set; }
    public uint AvatarChamberIdent { get; private set; }
    public Vector3 AvatarLensLocus { get; private set; }
    public Matrix4x4 CamLens { get; private set; }
    public CameraChamberResolution CamChamberResolution { get; private set; }

    internal CanonPViewFrameInput Reset(
        FetchedChamber trunkChamber,
        IReadOnlyList<FetchedChamber>? nearbyStructureChambers,
        Vector3 beholderEyePtSpot,
        Matrix4x4 lensProj,
        ICanonPViewCellSource chambers,
        IClientCamera cam,
        Vector3 camRealmLocus,
        FrustumFacets? frustum,
        uint? avatarLbIdent,
        HashSet<uint>? movingActorIdents,
        int rasterizeMiddleLbX,
        int rasterizeMiddleLbY,
        int rasterizeRadius,
        IReadOnlyList<(uint LandblockId, Vector3 AabbMin, Vector3 AabbMax,
            IReadOnlyList<RealmActor> Entities,
            IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> lbListings,
        bool rasterizeHeavens,
        bool rasterizeWeather,
        float dayRatio,
        DayGroupRow? engagedDayCluster,
        SkyKeyframe heavensKeyframe,
        bool environOverrideEngaged,
        uint beholderChamberIdent,
        uint avatarChamberIdent,
        Vector3 avatarLensLocus,
        Matrix4x4 camLens,
        CameraChamberResolution camChamberResolution,
        bool structureDegradesDisabled = false)
    {
        TrunkChamber = trunkChamber;
        NearbyStructureChambers = nearbyStructureChambers;
        BeholderEyePtSpot = beholderEyePtSpot;
        LensMirror = lensProj;
        Cells = chambers;
        Camera = cam;
        CamRealmLocus = camRealmLocus;
        Frustum = frustum;
        AvatarLbIdent = avatarLbIdent;
        MovingActorIdents = movingActorIdents;
        RasterizeMiddleLbX = rasterizeMiddleLbX;
        RasterizeMiddleLbY = rasterizeMiddleLbY;
        RasterizeRadius = rasterizeRadius;
        LbListings = lbListings;
        RasterizeHeavens = rasterizeHeavens;
        RasterizeWeather = rasterizeWeather;
        DayRatio = dayRatio;
        EngagedDayCluster = engagedDayCluster;
        SkyKeyframe = heavensKeyframe;
        EnvironOverrideEngaged = environOverrideEngaged;
        BeholderChamberTag = beholderChamberIdent;
        AvatarChamberIdent = avatarChamberIdent;
        AvatarLensLocus = avatarLensLocus;
        CamLens = camLens;
        CamChamberResolution = camChamberResolution;
        StructureDegradesDisabled = structureDegradesDisabled;
        return this;
    }
}

public sealed class CanonPViewFrameResult
{
    public ClipCycleAssembly ClipAssembly { get; private set; } = null!;
    public HashSet<uint> DrawableChambers { get; private set; } = null!;

    public HashSet<uint> ShownSceneryChambers { get; private set; } = null!;

    public HashSet<uint> VisibleCells { get; private set; } = null!;

    internal RenderFrameTelemetryCounts ProbeCounts { get; private set; }
    internal RenderMirrorCounts SrcCounts { get; private set; }
    internal InteriorActorPartition.ClientResult? ProbePartition
    { get; private set; }

    internal CanonPViewFrameResult Reset(
        ClipCycleAssembly clipAssembly,
        HashSet<uint> drawableChambers,
        HashSet<uint> shownChambers,
        HashSet<uint> shownSceneryChambers,
        RenderFrameTelemetryCounts probeCounts,
        RenderMirrorCounts srcCounts,
        InteriorActorPartition.ClientResult? probePartition)
    {
        ClipAssembly = clipAssembly;
        DrawableChambers = drawableChambers;
        VisibleCells = shownChambers;
        ShownSceneryChambers = shownSceneryChambers;
        ProbeCounts = probeCounts;
        SrcCounts = srcCounts;
        ProbePartition = probePartition;
        return this;
    }

}
