using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Packs;
using MacAC.Client.Graphics.Picking;
using MacAC.Client.Paging;

namespace MacAC.Client.Graphics;

internal interface IRealmStagePViewPainter
{

    CanonPViewFrameResult DrawInside(CanonPViewFrameInput feed);

    void AbortCycle();
}

internal readonly record struct PreparedRealmStageFrame(
    bool ShouldRender,
    RasterizeCycleFoundation Foundation,
    RealmRenderFrame World,
    int ActiveDayGroup);

internal interface IPreparedRealmStageFramePhase : IRealmStageFramePhase
{
    PreparedRealmStageFrame ReadyEnhanced(RasterizeCycleFeed feed);

    RealmRenderFrameVerdict PaintReadiedEnhanced(
        RasterizeCycleFeed feed,
        in PreparedRealmStageFrame readied);

    void AbortReadiedEnhanced(in PreparedRealmStageFrame readied);
}

internal sealed class RealmStagePViewPainter(
    CanonPViewPainter renderer,
    CanonPLensSweepRunner passes) : IRealmStagePViewPainter
{
    private readonly CanonPViewPainter _painter = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly CanonPLensSweepRunner _passs = passes ?? throw new ArgumentNullException(nameof(passes));

    public CanonPViewFrameResult DrawInside(CanonPViewFrameInput feed) =>
        _painter.DrawInside(feed, _passs);

    public void AbortCycle() => _passs.AbortFrame();
}

internal sealed class RealmStagePainter(
    IRasterizeCycleFoundationOrigin foundation,
    IRenderSignInStateSource login,
    IRealmStageHeavensStateSource sky,
    IRealmRenderFrameAssembler frames,
    IRealmStageActorSource entities,
    IRealmStagePickingFrame? pick,
    IRealmStageAlphaFrame alpha,
    IRealmStageMoteVisibility particleVisibility,
    IRealmStagePViewPainter pview,
    ICanonPViewCellSource pviewCells,
    IRealmStagePassRunner passes,
    IRealmRenderRangeSource renderRange,
    IRealmStageTelemetry diagnostics,
    IRealmEpochAvailability? readiness = null,
    IAtmosphericRealmFrameSink? atmosphere = null) : IPreparedRealmStageFramePhase
{
    private readonly IRasterizeCycleFoundationOrigin _foundation = foundation ?? throw new ArgumentNullException(nameof(foundation));
    private readonly IRenderSignInStateSource _signin = login ?? throw new ArgumentNullException(nameof(login));
    private readonly IRealmStageHeavensStateSource _heavens = sky ?? throw new ArgumentNullException(nameof(sky));
    private readonly IRealmRenderFrameAssembler _cycles = frames ?? throw new ArgumentNullException(nameof(frames));
    private readonly IRealmStageActorSource _actors = entities ?? throw new ArgumentNullException(nameof(entities));
    private readonly IRealmStagePickingFrame? _pick = pick;
    private readonly IRealmStageAlphaFrame _alpha = alpha ?? throw new ArgumentNullException(nameof(alpha));
    private readonly IRealmStageMoteVisibility _moteVis = particleVisibility
            ?? throw new ArgumentNullException(nameof(particleVisibility));
    private readonly IRealmStagePViewPainter _pview = pview ?? throw new ArgumentNullException(nameof(pview));
    private readonly ICanonPViewCellSource _pviewChambers = pviewCells ?? throw new ArgumentNullException(nameof(pviewCells));
    private readonly IRealmStagePassRunner _passs = passes ?? throw new ArgumentNullException(nameof(passes));
    private readonly IRealmRenderRangeSource _rasterizeSpan = renderRange ?? throw new ArgumentNullException(nameof(renderRange));
    private readonly IRealmStageTelemetry _telemetry = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private readonly IRealmEpochAvailability _readiness = readiness ?? AlwaysAvailableRealmEpoch.Instance;
    private readonly IAtmosphericRealmFrameSink? _atmosphere = atmosphere;
    private readonly CanonPViewFrameInput _pviewCycleFeed = new();
    private readonly RealmFrameContext _frame = new();
    private RealmRenderFrame _readiedEnhancedRealm;
    private bool _hasReadiedEnhancedRealm;

    public RealmRenderFrameVerdict Render(RasterizeCycleFeed feed)
    {
        _ = feed;
        var scopes = new FrameScopeStack();
        try
        {
            FrustumFacets? readiedPickFrustum = _hasReadiedEnhancedRealm
                ? _readiedEnhancedRealm.Camera.Frustum
                : null;
            if (_pick is { } pick)
            {
                scopes.Enter(
                    "picking",
                    () => pick.BeginFrame(readiedPickFrustum),
                    pick.ConcludeFrame,
                    pick.CancelFrame);
            }

            if (!_readiness.IsRealmOnHand)
            {
                scopes.LeaveAll();
                return default;
            }

            var foundation = _foundation.Foundation;
            if (foundation.PortalViewportVisible)
            {
                scopes.LeaveAll();
                return default;
            }

            // The alpha queue and the particle visibility close as one unit, and they close in
            // opposite orders depending on whether the frame finished: alpha first on the way out,
            // particles first when unwinding a failure. Entered before the pass runner opens so an
            // already-poisoned queue is recovered by the same unwind path instead of making every
            // later frame fail.
            scopes.Enter(
                "world",
                _alpha.BeginFrame,
                () =>
                {
                    _alpha.EndFrame();
                    _moteVis.ConcludeCycle();
                },
                () =>
                {
                    _moteVis.CancelCycle();
                    _alpha.AbortFrame();
                });

            RealmRenderFrame realm;
            if (_hasReadiedEnhancedRealm)
            {
                realm = _readiedEnhancedRealm;
                _hasReadiedEnhancedRealm = false;
            }
            else
            {
                realm = _cycles.Build(
                    in foundation,
                    _signin.IsWaitingForSignin,
                    _heavens.EngagedDayGroup);
            }
            _atmosphere?.Publish(
                in foundation,
                in realm,
                _heavens.EngagedDayClusterOrdinal);

            scopes.Enter("passes", _passs.BeginFrame, static () => { }, _passs.AbortFrame);

            var cam = realm.Camera;
            var trunks = realm.Roots;
            FetchedChamber? clipTrunk = realm.ClipTrunk;
            _frame.Reset();
            _frame.Interior = clipTrunk is not null;
            _frame.Sky = trunks.RasterizeSky;
            CanonPViewFrameResult? pviewOutcome = null;

            _frame.ClipPlane = _passs.ReadyPlanarRealmClip;
            _frame.PaintSky = () => _passs.PaintPlanarHeavens(
                in cam,
                in foundation,
                _heavens.EngagedDayGroup,
                _heavens.DayFraction);
            _frame.PaintLand = () => _passs.PaintPlanarLand(in cam, trunks.PlayerLandblockId);
            _frame.PaintInterior = () =>
            {
                scopes.Adopt("interior", static () => { }, _pview.AbortCycle);
                pviewOutcome = _pview.DrawInside(
                    _pviewCycleFeed.Reset(
                        clipTrunk!,
                        realm.Buildings.NearbyBuildingCells,
                        trunks.ViewerEyePosition,
                        cam.LensMirror,
                        _pviewChambers,
                        cam.Camera,
                        cam.Position,
                        cam.Frustum,
                        trunks.PlayerLandblockId,
                        realm.AnimatedEntityIds,
                        trunks.RenderCenterLandblockX,
                        trunks.RenderCenterLandblockY,
                        _rasterizeSpan.NearbyRadius,
                        _actors.LbEntries,
                        trunks.RasterizeSky,
                        trunks.PlayerSeenOutside,
                        _heavens.DayFraction,
                        _heavens.EngagedDayGroup,
                        foundation.Sky,
                        foundation.EnvironOverrideActive,
                        trunks.ViewerCellId,
                        trunks.PlayerCellId,
                        trunks.PlayerViewPosition,
                        cam.Camera.View,
                        _telemetry.CamCellResolution,
                        structureDegradesDisabled: cam.IsOverheadLens));
                _moteVis.FlagShownSceneryChambers(pviewOutcome.ShownSceneryChambers);
            };
            _frame.PaintActors = () => _passs.PaintPlanarActors(
                in cam,
                _actors.LbEntries,
                trunks.PlayerLandblockId,
                realm.AnimatedEntityIds);
            _frame.CloseClipGaps = _passs.DeactivateClipGaps;
            _frame.PaintMotes = () => _passs.PaintPostRealmMotes(
                clipTrunk,
                pviewOutcome?.ClipAssembly,
                in cam);
            _frame.PaintWeather = () => _passs.PaintPlanarWeather(
                in cam,
                in foundation,
                _heavens.EngagedDayGroup,
                _heavens.DayFraction);

            RealmFrameSteps.Run(_frame);

            var probe = _telemetry.PaintAndBroadcast(
                in cam,
                _actors.LbLimits);
            scopes.LeaveAll();
            return new RealmRenderFrameVerdict(
                probe.VisibleLandblocks,
                probe.TotalLandblocks,
                NormalWorldDrawn: true);
        }
        catch (Exception rasterizeMiss)
        {
            var cancelMisses = scopes.UnwindAll();
            if (cancelMisses is { Count: > 0 })
            {
                cancelMisses.Insert(0, rasterizeMiss);
                throw new AggregateException(
                    "World rendering failed and the incomplete frame could not be fully aborted.",
                    cancelMisses);
            }
            throw;
        }
    }

    public PreparedRealmStageFrame ReadyEnhanced(RasterizeCycleFeed feed)
    {
        _ = feed;
        if (_hasReadiedEnhancedRealm)
        {
            throw new InvalidOperationException(
                "The prior enhanced world preparation wasn't consumed");
        }

        var foundation = _foundation.Foundation;
        if (!_readiness.IsRealmOnHand || foundation.PortalViewportVisible)
            return new PreparedRealmStageFrame(false, foundation, default, -1);

        var realm = _cycles.Build(
            in foundation,
            _signin.IsWaitingForSignin,
            _heavens.EngagedDayGroup);
        var trunks = realm.Roots;
        realm = realm with
        {
            HousedPagingPane =
                _actors.GrabHousedPagingPane(
                    trunks.RenderCenterLandblockX,
                    trunks.RenderCenterLandblockY),
            CelestialShadeSrc =
                AuthoredCelestialShadeSourcePicker.Resolve(
                    _heavens.EngagedDayGroup,
                    _heavens.DayFraction,
                    foundation.Sky),
        };
        _readiedEnhancedRealm = realm;
        _hasReadiedEnhancedRealm = true;
        return new PreparedRealmStageFrame(
            true,
            foundation,
            realm,
            _heavens.EngagedDayClusterOrdinal);
    }

    public RealmRenderFrameVerdict PaintReadiedEnhanced(
        RasterizeCycleFeed feed,
        in PreparedRealmStageFrame readied)
    {
        if (readied.ShouldRender != _hasReadiedEnhancedRealm)
        {
            throw new InvalidOperationException(
                "The prepared enhanced-world token doesn't match the pending frame");
        }

        try
        {
            return Render(feed);
        }
        finally
        {
            _hasReadiedEnhancedRealm = false;
            _readiedEnhancedRealm = default;
        }
    }

    public void AbortReadiedEnhanced(in PreparedRealmStageFrame readied)
    {
        if (!readied.ShouldRender)
            return;
        if (!_hasReadiedEnhancedRealm)
            return;
        _hasReadiedEnhancedRealm = false;
        _readiedEnhancedRealm = default;
    }

}
