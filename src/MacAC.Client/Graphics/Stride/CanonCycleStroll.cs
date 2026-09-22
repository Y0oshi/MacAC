using System.Numerics;

namespace MacAC.Client.Graphics.Stride;

public interface ICanonFrameStrideScope : IStrideBuildingFrameScope
{
    StridePlane CyPlane { get; }

    void AssignEngagedLens(StridePortalView views, int ordinal);

    float ViewRectWidth { get; }
    float ViewRectHeight { get; }

    uint BeholderCellId => 0u;

    bool WeatherLatchOpen => false;
    bool BuildingDegradesDisabled => false;
}

public sealed class CanonCycleStroll
{
    private readonly BuildingDegradeDriver? _degradation;
    private readonly float? _fixedDowngradeGap;
    private readonly float? _fixedDowngradeMultiplier;
    private readonly StridePortalView _defaultLens = new();

    public StridePView InteriorPLens { get; } = new() { PaintLandscape = true };
    public StridePView ExteriorPLens { get; } = new() { PaintLandscape = false };

    public bool AlwaysPaintObjects = true;

    public int ObjectLoopThreshold = 4;

    public CanonCycleStroll() { }

    internal CanonCycleStroll(BuildingDegradeDriver degradation)
        => _degradation = degradation ?? throw new ArgumentNullException(nameof(degradation));

    internal CanonCycleStroll(float downgradeGap, float downgradeMultiplier)
    {
        _fixedDowngradeGap = downgradeGap;
        _fixedDowngradeMultiplier = downgradeMultiplier;
    }

    internal StridePortalView InteriorBeyondLens => InteriorPLens.BeyondLens;

    public void WalkFrame(
        uint camChamberIdent, StrollChamber? cameraCell, StrideLandscape scenery,
        ICanonFrameStrideScope cx, IStrollSignalDrain drain)
    {
        if ((camChamberIdent & 0xFFFF) < 0x100)
        {
            _defaultLens.RestartForPush();
            StrideCopyView.AffixWholeViewRectQuad(
                _defaultLens, cx.Rays, cx.RealmViewpoint,
                cx.ViewRectWidth, cx.ViewRectHeight);
            PaintScenery(scenery, _defaultLens, cx, drain);
        }
        else
        {
            DrawInside(
                cameraCell ?? throw new ArgumentNullException(nameof(cameraCell)),
                scenery, cx, drain);
        }
    }

    public void DrawInside(
        StrollChamber chamber, StrideLandscape scenery,
        ICanonFrameStrideScope cx, IStrollSignalDrain drain)
    {
        drain.Emit(StrideEvent.DrawInside(chamber.CellId));
        chamber.PushLens();
        AppendViews(chamber.StabList, cx);
        StrideCopyView.AffixWholeViewRectQuad(
            chamber.TopLens, cx.Rays, cx.RealmViewpoint,
            cx.ViewRectWidth, cx.ViewRectHeight);
        InteriorPLens.FabricateLens(chamber, 0xFFFF, cx.ChamberCtx);

        uint[] floodChambers = WritePaintChambers(InteriorPLens, drain);
        int beyondLensTally = InteriorPLens.BeyondLens.ViewCount;
        if (beyondLensTally > 0)
            PaintScenery(scenery, InteriorPLens.BeyondLens, cx, drain);

        drain.OnInteriorFloodPaintPivot(floodChambers, beyondLensTally);

        DropViews(chamber.StabList, cx);
        chamber.TakeLens();
    }

    public void PaintScenery(
        StrideLandscape scenery, StridePortalView engagedViews,
        ICanonFrameStrideScope cx, IStrollSignalDrain drain)
    {
        drain.Emit(StrideEvent.Landscape(engagedViews.ViewCount));
        drain.OnSceneryViews(engagedViews);
        scenery.CalcPaintOrdering();
        scenery.VerifyChunks(cx.CyPlane, engagedViews);

        for (int idx = scenery.ChunkPaintTally - 1; idx >= 0; --idx)
        {
            var chunk = scenery.Chunks[scenery.ChunkPaintRoster[idx]];
            if (chunk is null || chunk.InLens == StrideBoundingType.Outside)
                continue;
            int chamberTally = chunk.FlankChamberTally * chunk.FlankChamberTally;
            for (int kdx = 0; kdx < chamberTally; ++kdx)
            {
                int chamberOrdinal = chunk.PaintArr[kdx];
                bool chamberInLens =
                    chunk.ChamberInLens[chamberOrdinal] != StrideBoundingType.Outside;

                if (chamberInLens)
                {
                    drain.OnLandChamberPivot(
                        chunk.LbTag, chunk.FlankChamberTally, chamberOrdinal);
                }

                if (!AlwaysPaintObjects && !chamberInLens)
                    continue;

                drain.OnOrderChamberPivot(
                    chunk.LbTag, chunk.FlankChamberTally, chamberOrdinal);

                if (chunk.ChamberStructures[chamberOrdinal] is StrollStructure structure)
                    DrawBuilding(structure, engagedViews, cx, drain);
                if (chunk.FlankChamberTally is 8 || chunk.Ring <= ObjectLoopThreshold)
                {
                    if ((uint)chamberOrdinal < (uint)chunk.CoarseChamberStructures.Length)
                    {
                        foreach (StrollStructure coarseStructure in chunk.CoarseChamberStructures[chamberOrdinal])
                            DrawBuilding(coarseStructure, engagedViews, cx, drain);
                    }
                    drain.OnLandscapeCellTurn(
                        chunk.LbTag,
                        chunk.FlankChamberTally,
                        chamberOrdinal);
                }

                drain.OnOrderChamberQuit(
                    chunk.LbTag,
                    chunk.FlankChamberTally,
                    chamberOrdinal);
            }
        }

        if (cx.WeatherLatchOpen)
            drain.OnWeatherPivot(cx.BeholderCellId);
    }

    public void DrawBuilding(
        StrollStructure structure, StridePortalView engagedViews,
        ICanonFrameStrideScope cx, IStrollSignalDrain drain)
    {
        drain.Emit(StrideEvent.Building(structure.LocusChamberIdent));
        var pick = structure.Select(
            cx.BeholderGapTo(structure),
            _degradation?.DowngradeGap ?? _fixedDowngradeGap ?? 50f,
            _degradation?.EngagedMultiplier ?? _fixedDowngradeMultiplier ?? 0f,
            degradesDisabled: cx.BuildingDegradesDisabled);
        if (pick.GfxObjId is 0)
            return;

        drain.OnStructurePivot(structure);

        if (pick.DrawingBsp is StrideBspNode bsp)
        {
            int lensTally = Math.Max(engagedViews.ViewCount, 0);
            GatewaySweepDrain passDrain = new GatewaySweepDrain(structure, drain);
            Vector3 viewpoint = cx.ViewpointInStructure(structure);
            for (int v = 0; v < lensTally; ++v)
            {
                passDrain.EngagedLensOrdinal = v;
                cx.AssignEngagedLens(engagedViews, v);
                StrideBuildingPortals.AssemblePaintGatewaysSole(
                    bsp, 1, viewpoint,
                    (gatewayRef, pass) => StrideBuildingPortals.PaintGateway(
                        ExteriorPLens, structure, gatewayRef, pass, cx, passDrain));
                StrideBuildingPortals.AssemblePaintGatewaysSole(
                    bsp, 2, viewpoint,
                    (gatewayRef, pass) => StrideBuildingPortals.PaintGateway(
                        ExteriorPLens, structure, gatewayRef, pass, cx, passDrain));
            }
        }

        drain.OnStructureShellPivot(structure, pick);
    }

    private uint[] WritePaintChambers(StridePView pview, IStrollSignalDrain drain)
    {
        uint[] chambers = new uint[pview.ChamberPaintRoster.Count];
        for (int idx = 0; idx < chambers.Length; ++idx)
            chambers[idx] = pview.ChamberPaintRoster[idx].CellId;
        drain.Emit(StrideEvent.DrawCells(pview.BeyondLens.ViewCount, chambers));
        return chambers;
    }

    private void AppendViews(uint[] stabRoster, ICanonFrameStrideScope cx)
    {
        foreach (uint ident in stabRoster)
            cx.FetchShown(ident)?.PushLens();
    }

    private void DropViews(uint[] stabRoster, ICanonFrameStrideScope cx)
    {
        foreach (uint ident in stabRoster)
            cx.FetchShown(ident)?.TakeLens();
    }

    private sealed class GatewaySweepDrain(StrollStructure structure, IStrollSignalDrain drain)
        : StrideBuildingPortals.IStridePortalPassSink
    {
        public int EngagedLensOrdinal;

        public void OnPunch(StridePolygon polyg) => drain.OnPunchGeo(structure, polyg, EngagedLensOrdinal);

        public void OnPaintChambers(StridePView pview)
        {
            uint[] chambers = new uint[pview.ChamberPaintRoster.Count];
            for (int idx = 0; idx < chambers.Length; ++idx)
                chambers[idx] = pview.ChamberPaintRoster[idx].CellId;
            drain.Emit(StrideEvent.DrawCells(pview.BeyondLens.ViewCount, chambers));
        }
    }
}
