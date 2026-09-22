using System.Diagnostics;
using System.Numerics;
using MacAC.Assets;
using MacAC.Client.Shell;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IClientChargenPreviewControl
{
    bool Rebuild(
        GenesisOptions knobs,
        uint lineageIdent,
        int genderTag,
        GenesisLookChoice pick);

    void ZoomIn();
    void ZoomOut();
    void SpinClockwise();
    void SpinCounterClockwise();
}

internal interface IChargenPreviewSheetVis
{
    bool IsShown { get; }
}

internal interface IChargenPreviewCycleLens
{
    bool TryFetchShownDims(out int width, out int height);

    void AssignTextureHnd(uint textureHnd);
}

internal sealed class CanonChargenPreviewPageVisibility(MacAC.Client.Shell.CanonWidgetEngine runtime) : IChargenPreviewSheetVis
{
    private readonly MacAC.Client.Shell.CanonWidgetEngine _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public bool IsShown => _runtime.IsChargenPreviewSheetShown;
}

internal sealed class CanonSummaryPreviewPageVisibility(MacAC.Client.Shell.CanonWidgetEngine runtime) : IChargenPreviewSheetVis
{
    private readonly MacAC.Client.Shell.CanonWidgetEngine _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public bool IsShown => _runtime.IsSummaryPreviewSheetShown;
}

internal sealed class CanonChargenPreviewFrameView(
    WidgetViewport viewport,
    IChargenPreviewSheetVis page) : IChargenPreviewCycleLens
{
    private readonly WidgetViewport _viewRect = viewport ?? throw new ArgumentNullException(nameof(viewport));
    private readonly IChargenPreviewSheetVis _sheet = page ?? throw new ArgumentNullException(nameof(page));

    public bool TryFetchShownDims(out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!_viewRect.Visible || !_sheet.IsShown)
            return false;

        width = (int)_viewRect.Width;
        height = (int)_viewRect.Height;
        return true;
    }

    public void AssignTextureHnd(uint textureHnd) =>
        _viewRect.TextureSlot = WidgetTextureChartHandle.ToSocket(textureHnd);
}

internal sealed class ChargenPreviewDriver :
    IClientChargenPreviewControl,
    IPrivateActorViewportFrame,
    IDisposable
{
    private readonly IChargenPreviewPainter _painter;
    private readonly IChargenPreviewCycleLens _lens;
    private readonly ClientChargenPreviewCamera _cam;
    private readonly ChargenPreviewRotationDriver _spin;
    private readonly IDatAccess _datFiles;
    private readonly IAnimReader _anims;
    private readonly IGenesisPalSetSource _palSets;
    private readonly IGenesisGarbTableSource _clothingCharts;
    private readonly object _datMutex;
    private readonly bool _useZoomedOutEyePt;
    private readonly uint _rasterizeIdent;
    private readonly uint _backdropRasterizeIdent;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private ClientChargenPreviewAnimator? _animator;
    private ChargenPreviewZoomDriver? _zoom;
    private double _previousPassedSecs;
    private bool _hasComposed;
    private uint _previousLineageIdent;
    private int _previousGenderTag = -1;
    private GenesisLookChoice _previousPick;
    private bool _destroyed;

    public ChargenPreviewDriver(
        IChargenPreviewPainter renderer,
        ClientChargenPreviewCamera camera,
        IChargenPreviewCycleLens view,
        IDatAccess dats,
        IAnimReader animations,
        IGenesisPalSetSource palSets,
        IGenesisGarbTableSource clothingTables,
        object datLock,
        bool useZoomedOutEyePt = false,
        uint rasterizeIdent = ChargenPreviewActorAssembler.PreviewRasterizeIdent,
        uint backdropRasterizeIdent = ChargenPreviewActorAssembler.PreviewBackdropRasterizeIdent)
    {
        _painter = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _cam = camera ?? throw new ArgumentNullException(nameof(camera));
        _lens = view ?? throw new ArgumentNullException(nameof(view));
        _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
        _anims = animations ?? throw new ArgumentNullException(nameof(animations));
        _palSets = palSets ?? throw new ArgumentNullException(nameof(palSets));
        _clothingCharts = clothingTables ?? throw new ArgumentNullException(nameof(clothingTables));
        _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));
        _useZoomedOutEyePt = useZoomedOutEyePt;
        _rasterizeIdent = rasterizeIdent;
        _backdropRasterizeIdent = backdropRasterizeIdent;
        _spin = new ChargenPreviewRotationDriver();
        if (_useZoomedOutEyePt)
            _cam.Eye = ClientChargenPreviewCamera.LocateZoomedOutEyePt(0u);
    }

    internal bool IsZoomedIn => _zoom?.IsZoomedIn ?? false;

    // Test-observability seam only
    internal Vector3 CamEyePt => _cam.Eye;

    public bool Rebuild(
        GenesisOptions knobs,
        uint lineageIdent,
        int genderTag,
        GenesisLookChoice pick)
    {
        if (_destroyed)
            return false;

        if (_hasComposed
            && lineageIdent == _previousLineageIdent
            && genderTag == _previousGenderTag
            && pick.Equals(_previousPick))

            return true;

        bool composed;
        GenesisLook outcome;
        lock (_datMutex)
        {
            composed = GenesisLookBuilder.TryConstruct(
                knobs, lineageIdent, genderTag, pick,
                _palSets, _clothingCharts, out outcome);
        }
        if (!composed)

            return false;

        Quaternion bearing = ApproachMath.ApplyBearing(
            Quaternion.Identity, _spin.BearingDeg);
        var assemble = ChargenPreviewActorAssembler.TryAssembleMoving(
            _datFiles, _anims, outcome, lineageIdent, bearing, _datMutex, _rasterizeIdent);
        if (assemble is null)
            return false;

        bool wasZoomedIn = _animator?.IsZoomedIn ?? false;
        _animator = new ClientChargenPreviewAnimator(assemble);
        if (wasZoomedIn)
            _animator.AssignZoomedIn(true);

        bool lineageOrGenderAltered =
            !_hasComposed || lineageIdent != _previousLineageIdent || genderTag != _previousGenderTag;
        if (lineageOrGenderAltered)
        {
            _cam.Eye = _useZoomedOutEyePt
                ? ClientChargenPreviewCamera.LocateZoomedOutEyePt(lineageIdent)
                : ClientChargenPreviewCamera.LocateDefaultEyePt(lineageIdent);
        }

        bool lineageAltered = !_hasComposed || lineageIdent != _previousLineageIdent;
        if (lineageAltered)
        {
            RealmActor? backdrop =
                knobs.TryFetchLineage(lineageIdent, out GenesisHeritageOptions? lineage)
                    ? ChargenPreviewActorAssembler.TryAssembleBackdrop(
                        _datFiles, lineage!.EnvironmentSetupId, _datMutex, _backdropRasterizeIdent)
                    : null;
            _painter.AssignBackdrop(backdrop);
        }

        _zoom = new ChargenPreviewZoomDriver(lineageIdent, _cam, _animator);

        _painter.AssignPreview(_animator.Entity);
        _hasComposed = true;
        _previousLineageIdent = lineageIdent;
        _previousGenderTag = genderTag;
        _previousPick = pick;
        return true;
    }

    public void ZoomIn() => _zoom?.ZoomIn();
    public void ZoomOut() => _zoom?.ZoomOut();
    public void SpinClockwise() => _spin.Toggle(ChargenSpinDir.Clockwise);
    public void SpinCounterClockwise() => _spin.Toggle(ChargenSpinDir.CounterClockwise);

    public void Render()
    {
        if (_destroyed || !_lens.TryFetchShownDims(out int width, out int height))
            return;

        double instant = _clock.Elapsed.TotalSeconds;
        float diffSecs = (float)Math.Max(0.0, instant - _previousPassedSecs);
        _previousPassedSecs = instant;

        _animator?.Tick(diffSecs);
        _spin.Tick(instant);
        _zoom?.Tick(instant);
        _animator?.Entity.Rotation = _spin.ToFacing();

        _lens.AssignTextureHnd(_painter.Render(width, height));
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _painter.AssignPreview(null);
        _painter.AssignBackdrop(null);
        _animator = null;
        _zoom = null;
    }
}
