using System.Numerics;
using MacAC.Client.Graphics.Stage;

namespace MacAC.Client.Graphics.Stride;

internal sealed partial class StrollCycleDriver
{
    void IStrollSignalDrain.OnLandChamberPivot(uint lbIdent, int sideCellCount, int cellIndex)
    {
        DemandOpenCycle();
        if (sideCellCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideCellCount),
                sideCellCount,
                "A landscape LOD grid has to be 1, 2, 4, or 8 cells per side");
        }
        if ((uint)cellIndex >= (uint)(sideCellCount * sideCellCount))
            throw new ArgumentOutOfRangeException(nameof(cellIndex));

        if (MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled)
        {
            StrideTranscriptDump.DumpLandChamber(
                StrideTranscriptDump.LodChamberIdent(lbIdent, sideCellCount, cellIndex));
        }

        FlagIfGrown();
        _signals.Add(StrideFrameEvent.TerrainChamber(lbIdent, sideCellCount, cellIndex));
    }

    void IStrollSignalDrain.OnOrderChamberPivot(uint lbIdent, int sideCellCount, int cellIndex)
    {
        if (!MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled)
            return;

        DemandOpenCycle();
        if (sideCellCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideCellCount),
                sideCellCount,
                "A landscape LOD grid has to be 1, 2, 4, or 8 cells per side");
        }
        if ((uint)cellIndex >= (uint)(sideCellCount * sideCellCount))
            throw new ArgumentOutOfRangeException(nameof(cellIndex));

        StrideTranscriptDump.DumpOrderChamber(
            StrideTranscriptDump.LodChamberIdent(lbIdent, sideCellCount, cellIndex));
    }

    void IStrollSignalDrain.OnLandscapeCellTurn(uint chamberIdent)
        => ProcessSceneryChamberPivot(chamberIdent);

    void IStrollSignalDrain.OnLandscapeCellTurn(
        uint lbIdent,
        int sideCellCount,
        int cellIndex)
    {
        if (sideCellCount is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sideCellCount),
                sideCellCount,
                "A landscape LOD grid has to be 1, 2, 4, or 8 cells per side");
        }
        if ((uint)cellIndex >= (uint)(sideCellCount * sideCellCount))
            throw new ArgumentOutOfRangeException(nameof(cellIndex));

        if (sideCellCount is 1)
            return;

        uint chunkStem = lbIdent & 0xFFFF0000u;
        if (sideCellCount is 8)
        {
            ProcessSceneryChamberPivot(chunkStem | checked((uint)(cellIndex + 1)));
            return;
        }

        DemandOpenCycle();
        if (_sceneryLensCourseOrdinal < 0)
            throw new InvalidOperationException("A landscape turn needs an active view set");
        if (_farDrawCache.TryAffix(
                chunkStem, sideCellCount, cellIndex, _flow,
                this, _sceneryLensCourseOrdinal, _camRealmLocus,
                _alphaSubmissions))
        {
            FlagIfGrown();
            int chamberSpan = 8 / sideCellCount;
            int beginX = cellIndex / sideCellCount * chamberSpan;
            int beginY = cellIndex % sideCellCount * chamberSpan;
            int alphaChamberOrdinal = 0;
            for (int x = beginX; x < beginX + chamberSpan; ++x)
                for (int y = beginY; y < beginY + chamberSpan; ++y)
                {
                    uint chamberIdent = chunkStem | (uint)(x * 8 + y + 1);
                    VisitedSceneryChamberIdents.Add(chamberIdent);
                    bool motes = _chamberMoteTurnsDrawnThisCycle.Add(chamberIdent);
                    _alphaSubmitFlag = _farDrawCache.AlphaEnds[alphaChamberOrdinal++];
                    _signals.Add(StrideFrameEvent.SceneryChamberMotes(
                        chamberIdent, _alphaSubmitFlag, motes));
                }
            return;
        }

        int span = 8 / sideCellCount;
        int coarseX = cellIndex / sideCellCount;
        int coarseY = cellIndex % sideCellCount;
        int leadX = coarseX * span;
        int leadY = coarseY * span;
        for (int x = leadX; x < leadX + span; ++x)
        {
            for (int y = leadY; y < leadY + span; ++y)
            {
                ProcessSceneryChamberPivot(
                    chunkStem | checked((uint)(x * 8 + y + 1)));
            }
        }
    }

    void IStrollSignalDrain.OnOrderChamberQuit(uint lbIdent, int flankChamberTally, int chamberOrdinal)
    {
        DemandOpenCycle();
        FlagIfGrown();
        FlagAlphaIfGrown();
        _signals.Add(StrideFrameEvent.OrderChamberQuit());
    }

    void IStrollSignalDrain.OnSceneryViews(StridePortalView engagedViews)
    {
        ArgumentNullException.ThrowIfNull(engagedViews);
        DemandOpenCycle();
        _sceneryLensCourseOrdinal = _chamberLensCourseOrdinal++;
        GrabViews(0, engagedViews);
    }

    void IStrollSignalDrain.OnStructurePivot(StrollStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        DemandOpenCycle();
        VisitedStructures.Add(structure);

        FlagIfGrown();
        FlagAlphaIfGrown();
        _signals.Add(StrideFrameEvent.AlphaBarrier());

        _latestDcJuncture = StrollPaintJuncture.LookInStatic;
    }

    void IStrollSignalDrain.OnStructureShellPivot(
        StrollStructure structure,
        StrideBuildingPicking pick)
    {
        ArgumentNullException.ThrowIfNull(structure);
        DemandOpenCycle();

        FlagIfGrown();
        var shell = _realmBlob.FetchStructureShellStatics(structure);
        if (shell.Records.Count > 1)
        {
            throw new InvalidOperationException(
                $"Building 0x{structure.LocusChamberIdent:X8} has "
                + $"{shell.Records.Count} retained shell records; wanted no more than one");
        }
        if (shell.Records.Count is 1)
        {
            ref readonly RenderMirrorRecord capture =
                ref shell.Records.AsSpan()[0];
            _populator.FillStructureShell(
                _flow,
                structure.LocusChamberIdent,
                in capture,
                shell.TupleLandblockId,
                _camRealmLocus,
                _lensProj,
                in pick,
                structure.PieceZeroXform,
                _alphaSubmissions);
        }
        if (_alphaSubmissions.Count != _alphaSubmitFlag)
        {
            FlagIfGrown();
            FlagAlphaIfGrown();
        }
    }

    void IStrollSignalDrain.OnPunchGeo(
        StrollStructure structure, StridePolygon polyg, int engagedLensOrdinal)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(polyg);
        DemandOpenCycle();

        if (StrideVisibilityMath.IsRejectedByGatewayPolygBoundaryGuard(polyg.Vertices))
            return;

        FlagIfGrown();
        Matrix4x4 realmXform = _realmBlob.FetchStructureRealmXform(structure);
        _signals.Add(
            StrideFrameEvent.PunchFan(ConvertToRealm(polyg, realmXform), engagedLensOrdinal));
    }

    void IStrollSignalDrain.OnInteriorFloodPaintPivot(IReadOnlyList<uint> chambers, int beyondLensTally)
    {
        ArgumentNullException.ThrowIfNull(chambers);
        DemandOpenCycle();

        if (beyondLensTally > 0)
        {
            FlagIfGrown();
            _signals.Add(StrideFrameEvent.SceneryDrain());

            _router.ProgressStrollPiecePassStamp();
            _chamberShellsDrawnThisCycle.Clear();
            _chamberMoteTurnsDrawnThisCycle.Clear();

            int loaded = GatewaysDrawnTally;
            GatewaysDrawnTally = 0;
            if (loaded is not 0)
            {
                FlagIfGrown();
                _signals.Add(StrideFrameEvent.WipeInteriorZDepth());
            }

            FlagIfGrown();
            _signals.Add(StrideFrameEvent.QuitSeals());
        }

        InteriorFloodChambers.Clear();
        for (int idx = 0; idx < chambers.Count; ++idx)
            InteriorFloodChambers.Add(chambers[idx]);

        WriteFloodTurns(StrollPaintJuncture.CellStatic, chambers);
    }

    void IStrollSignalDrain.OnWeatherPivot(uint beholderChamberIdent)
    {
        DemandOpenCycle();
        WeatherTurnFired = true;

        if (!MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled)
            return;

        StrideTranscriptDump.DumpObjectChamberPivot(beholderChamberIdent);
    }
}
