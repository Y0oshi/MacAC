using System.Numerics;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonPViewPainter
{
    internal CanonPViewFrameResult DrawInside(
        CanonPViewFrameInput cx,
        CanonPLensSweepRunner passs)
    {
        ArgumentNullException.ThrowIfNull(cx);
        ArgumentNullException.ThrowIfNull(passs);
        passs.BeginFrame();
        CanonPLensSweepRunner strollExecutor = passs as CanonPLensSweepRunner
            ?? throw new InvalidOperationException(
                "The retail frame walk needs RetailPViewPassExecutor");
        var clipAssembly = passs.CommenceStrollClipCycle(
            cx.TrunkChamber.IsExteriorJoint,
            _clipAssemblyTemp);

        _drawableChambersTemp.Clear();
        var drawableChambers = _drawableChambersTemp;

        Stride.StrollCycleDriver strollDriver;
        {
            Matrix4x4 lens = cx.CamLens;
            Vector3 ahead = Vector3.Normalize(new Vector3(-lens.M13, -lens.M23, -lens.M33));
            var affix = strollExecutor!.StrollAffixReach;
            float viewRectWidth = affix?.Width ?? 1024f;
            float viewRectHeight = affix?.Height ?? 720f;
            bool weatherLatchOpen = CanonPLensSweepRunner.ShouldPaintWeatherOnce(
                cx.RasterizeHeavens, cx.RasterizeWeather, cx.AvatarChamberIdent);
            if (_strollCycleCtxTemp is null)
            {
                _strollCycleCtxTemp = new Stride.StrollProductionCycleCtx(
                    _strollChamberRegistry,
                    _strollStructures,
                    cx.BeholderEyePtSpot,
                    ahead,
                    cx.LensMirror,
                    viewRectWidth,
                    viewRectHeight,
                    cx.BeholderChamberTag,
                    weatherLatchOpen,
                    cx.StructureDegradesDisabled);
            }
            else
            {
                _strollCycleCtxTemp.Reset(
                    cx.BeholderEyePtSpot,
                    ahead,
                    cx.LensMirror,
                    viewRectWidth,
                    viewRectHeight,
                    cx.BeholderChamberTag,
                    weatherLatchOpen,
                    cx.StructureDegradesDisabled);
            }
            var strollCtx = _strollCycleCtxTemp;
            _strollScenery!.AssignBeholder(cx.BeholderChamberTag, cx.BeholderEyePtSpot);
            var strollScenery = _strollScenery.Landscape;

            Stride.StrollChamber? strollCamChamber = null;
            if ((cx.BeholderChamberTag & 0xFFFFu) >= 0x100)
            {
                strollCamChamber = (_strollChamberRegistry!.TryFetchChamber(cx.BeholderChamberTag, out FetchedChamber? fetched)
                    ? fetched?.Stroll
                    : null) ?? throw new InvalidOperationException(
                        $"walk root=0x{cx.BeholderChamberTag:X8}: the interior camera cell has "
                        + "no committed walk data - rendering "
                        + "needs the walk registry to by now hold the viewer's own cell "
                        + "(fail-loud rule; a silently skipped root would leave the frame "
                        + "with no static draws at all)");
            }

            if (cx.TrunkChamber.IsExteriorJoint && clipAssembly.BeyondLensSlices.Length is not 1)
            {
                throw new InvalidOperationException(
                    "walk static cutover: an outdoor root's clip assembly produced "
                    + $"{clipAssembly.BeyondLensSlices.Length} beyond-view slices, not the "
                    + "wanted 1 - the outdoor root draws through the full-screen default "
                    + "view (retail set_default_view; the walk fans precisely its own 1), and "
                    + "the outdoor slice data comes from the assembler. Refuse an not valid count");
            }

            _strollRealmBlob!.BeginFrame(
                _rasterizeTableauShade!.Ask,
                cx.AvatarLbIdent ?? 0u,
                cx.RasterizeMiddleLbX,
                cx.RasterizeMiddleLbY);

            _engagedStrollPasss = strollExecutor;
            _engagedStrollCycle = cx;
            _engagedStrollClipAssembly = clipAssembly;
            if (_strollLeafPainterTemp is null)
            {
                _strollLeafPainterTemp = new StrideProductionLeafPainter(
                    strollExecutor,
                    cx,
                    clipAssembly,
                    _strollDrainSceneryAct,
                    _strollWipeInteriorZDepthAct,
                    _strollPaintQuitSealsFn);
            }
            else
            {
                _strollLeafPainterTemp.Reset(
                    strollExecutor,
                    cx,
                    clipAssembly,
                    _strollDrainSceneryAct,
                    _strollWipeInteriorZDepthAct,
                    _strollPaintQuitSealsFn);
            }

            if (_walkFrameDriverScratch is null)
            {
                _walkFrameDriverScratch = new Stride.StrollCycleDriver(
                    strollExecutor.Router,
                    _strollLeafPainterTemp,
                    _strollRealmBlob,
                    clipCycle: clipAssembly.Frame);
            }
            else
            {
                _walkFrameDriverScratch.RebindCycle(
                    _strollLeafPainterTemp,
                    clipAssembly.Frame);
            }
            strollDriver = _walkFrameDriverScratch;

            _cycleStroll.ObjectLoopThreshold = cx.RasterizeRadius;

            try
            {
                strollDriver.Collect(
                    _cycleStroll, cx.BeholderChamberTag, strollCamChamber, strollScenery, strollCtx,
                    cx.LensMirror, cx.CamRealmLocus);
            }
            catch
            {
                strollDriver.CancelCycle();
                ClearWalkFrameBindings();
                throw;
            }
            if (strollCamChamber is not null)
            {
                ClipCycleAssembler.ReassembleOutsideViewFromWalk(
                    clipAssembly,
                    _cycleStroll.InteriorBeyondLens,
                    viewRectWidth,
                    viewRectHeight);
            }

            _drawableChambersTemp.Clear();
            _drawableChambersTemp.UnionWith(strollDriver.VisitedChambers);
            strollDriver.DuplicateShownChambersTo(_shownChambersTemp);
            _shownSceneryChambersTemp.Clear();
            _shownSceneryChambersTemp.UnionWith(strollDriver.VisitedSceneryChamberIdents);

        }

        passs.ReadyClipCycle();

        var readyChambers = drawableChambers;

        passs.ReadyChamberLots(cx, readyChambers);

        {
            var keptCounts = _rasterizeTableauShade.Counts;
            var counts = TraverseProbeCounts(keptCounts);
            var srcCounts = keptCounts;
            var outcome = _cycleOutcomeTemp.Reset(
                clipAssembly,
                drawableChambers,
                _shownChambersTemp,
                _shownSceneryChambersTemp,
                counts,
                srcCounts,
                probePartition: null);
            _strollPreWipeDynamics = () =>
            {
                DrawLandscapeDynamicsPhase(
                    cx,
                    passs,
                    clipAssembly,
                    strollDriver);
            };
            try
            {
                DrawWalkDrivenStatics(cx, strollExecutor, strollDriver!);
                if (cx.TrunkChamber.IsExteriorJoint)
                {
                    DrawLandscapeDynamicsPhase(
                        cx,
                        passs,
                        clipAssembly,
                        strollDriver);
                }
            }
            finally
            {
                _strollPreWipeDynamics = null;
                ClearWalkFrameBindings();
            }

            passs.DrawUnattachedSceneParticles(cx, exteriorChambers: false);

            return outcome;
        }
    }

    private int PaintStrollQuitSeals()
    {
        CanonPViewFrameInput cycle = _engagedStrollCycle
            ?? throw new InvalidOperationException(
                "The retained walk leaf has no active frame binding");
        CanonPLensSweepRunner passs = _engagedStrollPasss
            ?? throw new InvalidOperationException(
                "The retained walk leaf has no active pass binding");
        Stride.StrollCycleDriver driver = _walkFrameDriverScratch
            ?? throw new InvalidOperationException(
                "The retained walk leaf has no active driver binding");

        return PaintStrollQuitGatewayBitmasks(cycle, passs, driver);
    }

    private void DrawWalkDrivenStatics(
        CanonPViewFrameInput cx,
        CanonPLensSweepRunner passs,
        Stride.StrollCycleDriver driver)
    {
        var (cycle, coder) = passs.DemandStrollSubmission();
        try
        {
            driver.Replay(cycle, coder);
        }
        finally
        {
            passs.ConcludeStrollLandCycle();
        }

    }

    private void DrawLandscapeDynamicsPhase(
        CanonPViewFrameInput cx,
        CanonPLensSweepRunner passs,
        ClipCycleAssembly clipAssembly,
        Stride.StrollCycleDriver strollDriver)
    {
        if (clipAssembly.BeyondLensSlices.Length is not 0)
        {
            passs.DrawUnattachedSceneParticles(cx, exteriorChambers: true);
        }

        if (strollDriver.WeatherTurnFired)
            passs.DrawWeatherOnce(cx);
    }

    private int PaintStrollQuitGatewayBitmasks(
        CanonPViewFrameInput cx,
        CanonPLensSweepRunner passs,
        Stride.StrollCycleDriver driver)
    {
        int submitted = 0;
        List<uint> floodChambers = driver.InteriorFloodChambers;
        for (int idx = floodChambers.Count - 1; idx >= 0; --idx)
        {
            uint chamberIdent = floodChambers[idx];
            int sliceTally = driver.InteriorFloodLensSliceTallyAt(idx);
            for (int sliceOrdinal = 0; sliceOrdinal < sliceTally; ++sliceOrdinal)
            {
                submitted += passs.PaintQuitGatewayBitmask(
                    cx,
                    chamberIdent,
                    driver.InteriorFloodLensClipPlanesAt(idx, sliceOrdinal));
            }
        }
        return submitted;
    }
}
