using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Heavens;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal interface IRealmStagePassRunner
{
    void BeginFrame();

    void ReadyPlanarRealmClip();

    void PaintPlanarHeavens(
        in RealmCameraFrame cam,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster,
        float dayRatio);

    void PaintPlanarLand(in RealmCameraFrame cam, uint? avatarLbIdent);

    void PaintPlanarActors(
        in RealmCameraFrame cam,
        IEnumerable<(uint LandblockId, System.Numerics.Vector3 AabbMin,
            System.Numerics.Vector3 AabbMax,
            IReadOnlyList<RealmActor> Entities,
            IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> listings,
        uint? avatarLbIdent,
        HashSet<uint> movingActorIdents);

    void PaintPostRealmMotes(
        FetchedChamber? clipTrunk,
        ClipCycleAssembly? clipAssembly,
        in RealmCameraFrame cam);

    void PaintPlanarWeather(
        in RealmCameraFrame cam,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster,
        float dayRatio);

    void DeactivateClipGaps();

    void AbortFrame();
}

internal sealed class RealmStagePassRunner(
    IRealmPassSurface surface,
    IRenderFrameGlLedger frameGlState,
    ClipCycle clipFrame,
    RealmPaintRouter entities,
    EnvironChamberPainter environmentCells,
    LandModernPainter? land,
    LandscapeDrawTelemetryDriver terrainDiagnostics,
    HeavensPainter? heavens,
    MoteSys? motes,
    MotePainter? motePainter) : IRealmStagePassRunner
{
    private readonly IRealmPassSurface _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
    private readonly IRenderFrameGlLedger _cycleGlPhase = frameGlState
            ?? throw new ArgumentNullException(nameof(frameGlState));
    private readonly ClipCycle _clipCycle = clipFrame ?? throw new ArgumentNullException(nameof(clipFrame));
    private readonly RealmPaintRouter _actors = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly EnvironChamberPainter _surroundingsChambers = environmentCells
            ?? throw new ArgumentNullException(nameof(environmentCells));
    private readonly LandModernPainter? _land = land;
    private readonly LandscapeDrawTelemetryDriver _landTelemetry = terrainDiagnostics
            ?? throw new ArgumentNullException(nameof(terrainDiagnostics));
    private readonly HeavensPainter? _heavens = heavens;
    private readonly MoteSys? _motes = motes;
    private readonly MotePainter? _motePainter = motePainter;
    private readonly HashSet<uint> _shownMoteHolders = [];
    private readonly HashSet<uint> _noExcludedMoteHolders = [];

    public void BeginFrame()
    {
        _shownMoteHolders.Clear();
        _clipCycle.Reset();
    }

    public void ReadyPlanarRealmClip() => _canvas.StageClipCycle();

    public void PaintPlanarHeavens(
        in RealmCameraFrame cam,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster,
        float dayRatio)
    {
        _canvas.ActivateClipGaps();
        Exception? paintMiss = null;
        try
        {
            _heavens?.PaintHeavens(
                cam.Camera,
                cam.Position,
                dayRatio,
                engagedDayCluster,
                foundation.Sky,
                foundation.EnvironOverrideActive);
        }
        catch (Exception problem)
        {
            paintMiss = problem;
            throw;
        }
        finally
        {
            try
            {
                DeactivateClipGaps();
            }
            catch (Exception shutMiss) when (paintMiss is not null)
            {
                throw new AggregateException(
                    "Sky drawing failed and its clip-distance bracket could not be closed",
                    paintMiss,
                    shutMiss);
            }
        }

        if (_motes is not null && _motePainter is not null)
        {
            _motePainter.Draw(
                cam.Camera,
                cam.Position,
                ParticleDrawPass.SkyPreScene);
        }
    }

    public void PaintPlanarLand(
        in RealmCameraFrame cam,
        uint? avatarLbIdent)
    {
        _canvas.ActivateClipGaps();
        _landTelemetry.Begin();
        _land?.Draw(
            cam.Camera,
            cam.Frustum,
            neverPruneLbIdent: avatarLbIdent);
        _landTelemetry.Complete();
    }

    public void PaintPlanarActors(
        in RealmCameraFrame cam,
        IEnumerable<(uint LandblockId, System.Numerics.Vector3 AabbMin,
            System.Numerics.Vector3 AabbMax,
            IReadOnlyList<RealmActor> Entities,
            IReadOnlyDictionary<uint, RealmActor>? AnimatedById)> listings,
        uint? avatarLbIdent,
        HashSet<uint> movingActorIdents)
    {
        _actors.Draw(
            cam.Camera,
            listings,
            cam.Frustum,
            neverPruneLbIdent: avatarLbIdent,
            shownChamberIdents: null,
            movingActorIdents: movingActorIdents);
    }

    public void PaintPostRealmMotes(
        FetchedChamber? clipTrunk,
        ClipCycleAssembly? clipAssembly,
        in RealmCameraFrame cam)
    {
        if (_motes is null || _motePainter is null)
            return;

        if (clipTrunk is null)
        {
            if (clipAssembly is not null)
            {
                _motePainter.PaintForHolders(
                    cam.Camera,
                    cam.Position,
                    ParticleDrawPass.Scene,
                    _shownMoteHolders,
                    includeUnattached: true,
                    excludedAffixedHolderIdents: _noExcludedMoteHolders);
                return;
            }

            _motePainter.Draw(
                cam.Camera,
                cam.Position,
                ParticleDrawPass.Scene);
            return;
        }

        return;
    }

    public void PaintPlanarWeather(
        in RealmCameraFrame cam,
        in RasterizeCycleFoundation foundation,
        DayGroupRow? engagedDayCluster,
        float dayRatio)
    {
        _canvas.ActivateClipGaps();
        Exception? paintMiss = null;
        try
        {
            _heavens?.PaintWeather(
                cam.Camera,
                cam.Position,
                dayRatio,
                engagedDayCluster,
                foundation.Sky,
                foundation.EnvironOverrideActive);
        }
        catch (Exception problem)
        {
            paintMiss = problem;
            throw;
        }
        finally
        {
            try
            {
                DeactivateClipGaps();
            }
            catch (Exception shutMiss) when (paintMiss is not null)
            {
                throw new AggregateException(
                    "Weather drawing failed and its clip-distance bracket could not be closed",
                    paintMiss,
                    shutMiss);
            }
        }

        if (_motes is not null && _motePainter is not null)
        {
            _motePainter.Draw(
                cam.Camera,
                cam.Position,
                ParticleDrawPass.SkyPostScene);
        }
    }

    public void DeactivateClipGaps() => _canvas.SwitchOffClipGaps();

    public void AbortFrame()
    {
        List<Exception>? misses = null;
        TryCancel(_cycleGlPhase.ReinstateCycleDefaults);
        TryCancel(_clipCycle.Reset);
        TryCancel(_actors.CancelLatestRasterizeTableauWatcherCycle);
        _shownMoteHolders.Clear();
        if (misses is { Count: > 0 })
            throw new AggregateException("World scene pass abort failed", misses);

        void TryCancel(Action op)
        {
            try
            {
                op();
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
    }

}
