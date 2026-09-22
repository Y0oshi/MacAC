using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Stride;

internal sealed partial class StrollCycleDriver
{
    internal bool WeatherTurnFired { get; private set; }

    IReadOnlyList<uint> IStrideLookInViewSource.GazeInCellTurns => GazeInChamberTurns;

    public bool OrbShownInGazeInPivot(
        int courseOrdinal,
        in Vector3 middle,
        float radius,
        bool testOrb = true)
    {
        if ((uint)courseOrdinal >= (uint)_gazeInTurns.Count)
            return false;

        var pivot = _gazeInTurns[courseOrdinal];
        for (int sliceShift = 0; sliceShift < pivot.SliceCount; ++sliceShift)
        {
            var slice = _gazeInSlices[pivot.SliceStart + sliceShift];
            if (!testOrb
                || StrideVisibilityMath.ViewconeVerify(
                    middle,
                    radius,
                    _gazeInCyPlane,
                    CollectionsMarshal.AsSpan(_gazeInPlanes).Slice(
                        slice.PlaneStart,
                        slice.PlaneCount)) != StrideBoundingType.Outside)

                return true;
        }
        return false;
    }

    public string DepictGazeInPivot(
        int courseOrdinal,
        in Vector3 middle,
        float radius)
    {
        if ((uint)courseOrdinal >= (uint)_gazeInTurns.Count)
            return "route-missing";

        var pivot = _gazeInTurns[courseOrdinal];
        var blurb = new System.Text.StringBuilder(192);
        float cyGap = Vector3.Dot(_gazeInCyPlane.Normal, middle)
            + _gazeInCyPlane.D;
        blurb.Append("turnCell=0x")
            .Append(pivot.CellId.ToString("X8"))
            .Append(" slices=").Append(pivot.SliceCount)
            .Append(" cy=").Append(cyGap.ToString("F5"))
            .Append(" cyMargin=").Append((cyGap + radius).ToString("F5"));

        for (int sliceShift = 0; sliceShift < pivot.SliceCount; ++sliceShift)
        {
            var slice = _gazeInSlices[pivot.SliceStart + sliceShift];
            blurb.Append(" slice[").Append(sliceShift).Append("]=");
            for (int planeShift = 0; planeShift < slice.PlaneCount; ++planeShift)
            {
                if (planeShift is not 0)
                    blurb.Append(',');
                StridePlane plane = _gazeInPlanes[slice.PlaneStart + planeShift];
                float gap = Vector3.Dot(plane.Normal, middle) + plane.D;
                blurb.Append(gap.ToString("F5"));
            }
        }
        return blurb.ToString();
    }

    internal uint GazeInSliceClipSocketAt(int courseOrdinal, int sliceOffset = 0)
    {
        var pivot = _gazeInTurns[courseOrdinal];
        return (uint)sliceOffset >= (uint)pivot.SliceCount
            ? throw new ArgumentOutOfRangeException(nameof(sliceOffset))
            : _gazeInSlices[pivot.SliceStart + sliceOffset].ClipSlot;
    }

    internal int InteriorFloodLensSliceTallyAt(int floodChamberOrdinal)
    {
        int courseOrdinal = InteriorFloodLensCourseAt(floodChamberOrdinal);
        return _gazeInTurns[courseOrdinal].SliceCount;
    }

    internal ReadOnlySpan<Vector4> InteriorFloodLensClipPlanesAt(
        int floodChamberOrdinal,
        int sliceOffset)
    {
        int courseOrdinal = InteriorFloodLensCourseAt(floodChamberOrdinal);
        var pivot = _gazeInTurns[courseOrdinal];
        if ((uint)sliceOffset >= (uint)pivot.SliceCount)
            throw new ArgumentOutOfRangeException(nameof(sliceOffset));

        var slice = _gazeInSlices[pivot.SliceStart + sliceOffset];
        return _clipCycle is null ? [] : _clipCycle.FetchSocketPlanes(slice.ClipSlot);
    }

    internal void RebindCycle(
        IStrideFrameLeafPainter leafRenderer,
        ClipCycle? clipCycle)
    {
        CancelCycle(wipeKept: false);
        _leafPainter = leafRenderer
            ?? throw new ArgumentNullException(nameof(leafRenderer));
        _clipCycle = clipCycle;
    }

    internal void CancelCycle(bool wipeKept = true)
    {
        if (wipeKept)
            _farDrawCache.Clear();
        _cx = null;
        _lensProj = default;
        _camRealmLocus = default;
        _sceneryTurnsThisCycle = 0;
        _latestDcJuncture = null;
        _primedToRerun = false;
        WeatherTurnFired = false;
        _flow.Reset();
        _signals.Clear();
        _flagLoci.Clear();
        _alphaSubmissions.Clear();
        _queuedLandLot.Clear();
        _alphaSubmitFlag = 0;
        VisitedChambers.Clear();
        GazeInChamberTurns.Clear();
        _gazeInTurns.Clear();
        _gazeInSlices.Clear();
        _gazeInPlanes.Clear();
        _floodLensCourseTemp.Clear();
        _router.FinishStrollPieceCycle();
        _chamberShellsDrawnThisCycle.Clear();
        _chamberMoteTurnsDrawnThisCycle.Clear();
        _gazeInCyPlane = default;
        GazeInChambers.Clear();
        VisitedStructures.Clear();
        VisitedSceneryChamberIdents.Clear();
        InteriorFloodChambers.Clear();
        _chamberLensCourseOrdinal = 0;
        _sceneryLensCourseOrdinal = -1;
    }

    internal void DuplicateShownChambersTo(HashSet<uint> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(destination, VisitedChambers)
            || ReferenceEquals(destination, VisitedSceneryChamberIdents))
        {
            throw new ArgumentException(
                "The visible-cell destination can't alias a walk source set",
                nameof(destination));
        }

        destination.Clear();
        destination.UnionWith(VisitedChambers);
        destination.UnionWith(VisitedSceneryChamberIdents);
    }

    internal void RunFrame(
        CanonCycleStroll stroll,
        uint camChamberIdent,
        StrollChamber? camChamber,
        StrideLandscape scenery,
        ICanonFrameStrideScope cx,
        IGpuCycle cycle,
        IGpuSweepCoder coder,
        Matrix4x4 lensProj,
        Vector3 camRealmLocus)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(coder);

        Collect(
            stroll, camChamberIdent, camChamber, scenery, cx,
            lensProj, camRealmLocus);
        Replay(cycle, coder);
    }

    internal void Collect(
        CanonCycleStroll stroll,
        uint camChamberIdent,
        StrollChamber? camChamber,
        StrideLandscape scenery,
        ICanonFrameStrideScope cx,
        Matrix4x4 lensProj,
        Vector3 camRealmLocus)
    {
        ArgumentNullException.ThrowIfNull(stroll);
        ArgumentNullException.ThrowIfNull(scenery);
        ArgumentNullException.ThrowIfNull(cx);

        BeginFrame(cx, lensProj, camRealmLocus);
        if (MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled)
        {
            ++_transcriptCycleNumber;
            StrideTranscriptDump.DumpCycleTrunk(
                _transcriptCycleNumber,
                camChamberIdent,
                camRealmLocus - new Vector3(
                    scenery.BeholderRealmOriginX, scenery.BeholderRealmOriginY, 0f),
                cx.CyPlane.Normal);
        }
        try
        {
            stroll.WalkFrame(camChamberIdent, camChamber, scenery, cx, this);
            EndFrame();
        }
        catch
        {
            CancelCycle();
            throw;
        }
    }

    internal void BeginFrame(
        ICanonFrameStrideScope cx,
        Matrix4x4 lensProj,
        Vector3 camRealmLocus)
    {
        ArgumentNullException.ThrowIfNull(cx);
        if (_cx is not null)
        {
            throw new InvalidOperationException(
                "WalkFrameDriver.BeginFrame was called while a previous frame was still open - "
                + "the driver isn't re-entrant; call "
                + "EndFrame (or let a thrown exception's cleanup run) prior to starting the next");
        }

        _router.CommenceStrollPieceCycle();
        _farDrawCache.BeginFrame();
        _cx = cx;
        _lensProj = lensProj;
        _camRealmLocus = camRealmLocus;
        _sceneryTurnsThisCycle = 0;
        _latestDcJuncture = null;
        _primedToRerun = false;
        WeatherTurnFired = false;
        _flow.Reset();
        _signals.Clear();
        _flagLoci.Clear();
        _alphaSubmissions.Clear();
        _queuedLandLot.Clear();
        _alphaSubmitFlag = 0;
        VisitedChambers.Clear();
        GazeInChamberTurns.Clear();
        _gazeInTurns.Clear();
        _gazeInSlices.Clear();
        _gazeInPlanes.Clear();
        _gazeInCyPlane = cx.CyPlane;
        _chamberShellsDrawnThisCycle.Clear();
        _chamberMoteTurnsDrawnThisCycle.Clear();
        GazeInChambers.Clear();
        VisitedStructures.Clear();
        VisitedSceneryChamberIdents.Clear();
        InteriorFloodChambers.Clear();
        _chamberLensCourseOrdinal = 0;
        _sceneryLensCourseOrdinal = -1;
    }

    internal void EndFrame()
    {
        try
        {
            FlagIfGrown();
            FlagAlphaIfGrown();
        }
        finally
        {
            _router.FinishStrollPieceCycle();
            _cx = null;
            _primedToRerun = true;
        }
    }

    internal void Replay(IGpuCycle cycle, IGpuSweepCoder coder)
    {
        ArgumentNullException.ThrowIfNull(cycle);
        ArgumentNullException.ThrowIfNull(coder);
        if (!_primedToRerun)
        {
            throw new InvalidOperationException(
                "WalkFrameDriver.Replay was called without a completed Collect (BeginFrame/"
                + "EndFrame, or Collect/RunFrame) preceding it - there is nothing recorded to "
                + "replay.");
        }

        try
        {
            if (_flow.Count > 0)
                _router.PrepareOrderedStream(cycle, _flow, _lensProj, _flagLoci);

            _queuedLandLot.Clear();

            int cur = 0;
            int alphaCur = 0;
            for (int idx = 0; idx < _signals.Count; ++idx)
            {
                var e = _signals[idx];
                switch (e.Kind)
                {
                    case StrideFrameEventKind.StreamMark:
                        int finish = e.IntArgument;
                        int tally = finish - cur;
                        _trace?.OnDrain(tally, _flow.Junctures.GetRange(cur, tally));
                        _router.DrawOrderedRange(coder, cur, tally);
                        cur = finish;
                        break;
                    case StrideFrameEventKind.AlphaSubmitMark:
                        int alphaFinish = e.IntArgument;
                        for (; alphaCur < alphaFinish; ++alphaCur)
                        {
                            var lot =
                                _alphaSubmissions[alphaCur];
                            _router.SubmitWalkAlphaInstance(
                                in lot,
                                _lensProj);
                        }
                        break;
                    case StrideFrameEventKind.Sky:
                        DrainQueuedLandLot();
                        _leafPainter.SketchHeavens();
                        break;
                    case StrideFrameEventKind.GroundCell:
                        _queuedLandLot.Add((e.CellId, e.IntArgument >> 8, e.IntArgument & 0xFF));
                        break;
                    case StrideFrameEventKind.CellShell:
                        DrainQueuedLandLot();
                        _leafPainter.PaintChamberShell(e.CellId);
                        break;
                    case StrideFrameEventKind.PunchFan:
                        DrainQueuedLandLot();
                        _leafPainter.PaintPunchFan(e.Polygon!, e.IntArgument);
                        break;
                    case StrideFrameEventKind.AlphaBarrier:
                        DrainQueuedLandLot();
                        _leafPainter.AlphaBarrier();
                        break;
                    case StrideFrameEventKind.SortCellExit:
                        DrainQueuedLandLot();
                        _leafPainter.DrainOrderChamberQuit();
                        break;
                    case StrideFrameEventKind.LandscapeFlush:
                        DrainQueuedLandLot();
                        _leafPainter.DrainScenery();
                        break;
                    case StrideFrameEventKind.ClearInteriorDepth:
                        DrainQueuedLandLot();
                        _leafPainter.WipeInteriorDepth();
                        break;
                    case StrideFrameEventKind.ExitSeals:
                        DrainQueuedLandLot();
                        GatewaysDrawnTally += _leafPainter.PaintQuitSeals();
                        break;
                    case StrideFrameEventKind.StaticParticles:
                        SubmitChamberAlpha(
                            e,
                            ref alphaCur,
                            staticMotePivot: true);
                        break;
                    case StrideFrameEventKind.CellParticles:
                        SubmitChamberAlpha(
                            e,
                            ref alphaCur,
                            staticMotePivot: false);
                        break;
                }
            }
            DrainQueuedLandLot();
        }
        finally
        {
            _flow.Reset();
            _signals.Clear();
            _flagLoci.Clear();
            _alphaSubmissions.Clear();
            _queuedLandLot.Clear();
            _alphaSubmitFlag = 0;
            _primedToRerun = false;
            _cx = null;
        }
    }

    private int InteriorFloodLensCourseAt(int floodCellIndex)
    {
        if ((uint)floodCellIndex >= (uint)InteriorFloodChambers.Count
            || (uint)floodCellIndex >= (uint)_floodLensCourseTemp.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(floodCellIndex));
        }

        int courseOrdinal = _floodLensCourseTemp[floodCellIndex];
        return (uint)courseOrdinal >= (uint)_gazeInTurns.Count
            ? throw new InvalidOperationException(
                $"Interior flood cell 0x{InteriorFloodChambers[floodCellIndex]:X8} "
                + "has no captured portal-view route")
            : courseOrdinal;
    }

    private void SubmitChamberAlpha(
        in StrideFrameEvent e,
        ref int alphaCur,
        bool staticMotePivot)
    {
        ReadOnlySpan<PreparedMoteAlphaSubmission> motes =
            [];
        bool includeMotes = e.FloatArgument != 0f;
        if (includeMotes && _leafPainter.HasRenderableSpoutsInChamber(e.CellId))
        {
            DrainQueuedLandLot();
            motes = staticMotePivot
                ? _leafPainter.ReadyStaticMotes(e.CellId)
                : _leafPainter.ReadyChamberMotes(e.CellId);
        }

        int objectFinish = e.IntArgument;
        int moteOrdinal = 0;
        while (alphaCur < objectFinish || moteOrdinal < motes.Length)
        {
            bool grabMote = moteOrdinal < motes.Length
                && (alphaCur >= objectFinish
                    || motes[moteOrdinal].DistanceSq
                        > _alphaSubmissions[alphaCur].SortDistanceSq);
            if (grabMote)
            {
                motes[moteOrdinal++].Affix();
            }
            else
            {
                var lot =
                    _alphaSubmissions[alphaCur++];
                _router.SubmitWalkAlphaInstance(in lot, _lensProj);
            }
        }
    }

    private void DrainQueuedLandLot()
    {
        if (_queuedLandLot.Count is 0)
            return;
        _leafPainter.PaintLandChamberLot(_queuedLandLot);
        _queuedLandLot.Clear();
    }

    void IStrollSignalDrain.Emit(in StrideEvent strollSignal)
    {
        switch (strollSignal.Kind)
        {
            case StrideEventKind.DrawInside:
                StrideTranscriptDump.DumpPaintInside(strollSignal.CellId);
                _latestDcJuncture = StrollPaintJuncture.CellStatic;
                VisitedChambers.Add(strollSignal.CellId);
                break;
            case StrideEventKind.Landscape:
                StrideTranscriptDump.DumpScenery();
                ProcessSceneryPivot(strollSignal.OutsideViewCount);
                break;
            case StrideEventKind.DrawCells:
                StrideTranscriptDump.DumpPaintChambers(
                    exteriorPview: _latestDcJuncture == StrollPaintJuncture.LookInStatic,
                    strollSignal.OutsideViewCount,
                    strollSignal.Cells);
                foreach (uint ident in strollSignal.Cells)
                    VisitedChambers.Add(ident);
                ProcessPaintChambersPivot(strollSignal.Cells);
                break;
            case StrideEventKind.Building:
                StrideTranscriptDump.DumpStructure(strollSignal.CellId);
                break;
        }
    }

    private void WriteFloodTurns(StrollPaintJuncture juncture, IReadOnlyList<uint> chambers)
    {
        _floodLensCourseTemp.Clear();
        for (int idx = 0; idx < chambers.Count; ++idx)
            _floodLensCourseTemp.Add(-1);

        for (int idx = chambers.Count - 1; idx >= 0; --idx)
        {
            int lensCourseOrdinal = GrabChamberLensCourse(chambers[idx]);
            _floodLensCourseTemp[idx] = lensCourseOrdinal;
            if (MacAC.Mechanics.Drawing.DrawTelemetry.DumpWalkTranscriptEnabled)
            {
                int onlineLensTally = _gazeInTurns[lensCourseOrdinal].SliceCount;
                for (int lens = 0; lens < onlineLensTally; ++lens)
                    StrideTranscriptDump.DumpEnvironChamberShell(chambers[idx]);
            }
            if (_chamberShellsDrawnThisCycle.Add(chambers[idx]))
            {
                FlagIfGrown();
                _signals.Add(StrideFrameEvent.ChamberShell(chambers[idx]));
            }
        }

        for (int idx = chambers.Count - 1; idx >= 0; --idx)
            WriteChamberInsidesPivot(juncture, chambers[idx], _floodLensCourseTemp[idx]);
    }

    private void WriteChamberInsidesPivot(
        StrollPaintJuncture juncture,
        uint chamberIdent,
        int lensCourseOrdinal)
    {
        StrideTranscriptDump.DumpObjectChamberPivot(chamberIdent);

        var records = _realmBlob.FetchChamberObjects(chamberIdent);
        _populator.FillChamberObjects(
            _flow,
            juncture,
            chamberIdent,
            records.Records,
            records.TupleLandblockId,
            _camRealmLocus,
            _lensProj,
            this,
            lensCourseOrdinal,
            _alphaSubmissions);
        FlagIfGrown();

        if (juncture == StrollPaintJuncture.LookInStatic)
        {
            GazeInChamberTurns.Add(chamberIdent);
            GazeInChambers.Add(chamberIdent);
        }

        bool includeMotes = _chamberMoteTurnsDrawnThisCycle.Add(chamberIdent);
        _alphaSubmitFlag = _alphaSubmissions.Count;
        _signals.Add(StrideFrameEvent.ChamberMotes(
            chamberIdent,
            _alphaSubmitFlag,
            includeMotes));
    }

    private void ProcessSceneryChamberPivot(uint chamberIdent)
    {
        DemandOpenCycle();
        if (_sceneryLensCourseOrdinal < 0)
        {
            throw new InvalidOperationException(
                "A landscape cell turn fired prior to CanonCycleStroll installed its active "
                + "view set - the walk and draw driver are desynchronized");
        }
        VisitedSceneryChamberIdents.Add(chamberIdent);
        var records = _realmBlob.FetchExteriorObjects(chamberIdent);
        _populator.FillChamberObjects(
            _flow,
            StrollPaintJuncture.OutdoorStatic,
            chamberIdent,
            records.Records,
            records.TupleLandblockId,
            _camRealmLocus,
            _lensProj,
            this,
            _sceneryLensCourseOrdinal,
            _alphaSubmissions);
        FlagIfGrown();
        bool includeMotes = _chamberMoteTurnsDrawnThisCycle.Add(chamberIdent);
        _alphaSubmitFlag = _alphaSubmissions.Count;
        _signals.Add(StrideFrameEvent.SceneryChamberMotes(
            chamberIdent,
            _alphaSubmitFlag,
            includeMotes));
    }

    private void ProcessSceneryPivot(int engagedLensTally)
    {
        DemandOpenCycle();
        if (engagedLensTally < 1)
        {
            throw new InvalidOperationException(
                $"A Landscape turn fired with {engagedLensTally} active views - "
                + "CanonCycleStroll only draws the landscape through an installed view set "
                + "(the outdoor root's full-screen default view, or an interior root's "
                + "surviving exit views, both no fewer than 1). A zero/negative count is a "
                + "walk/driver desync");
        }
        if (_sceneryTurnsThisCycle is not 0)
        {
            throw new InvalidOperationException(
                "A second Landscape turn fired in one frame - RetailFrameWalk.WalkFrame/"
                + "DrawInside's own call graph guarantees no more than one Landscape turn per "
                + "frame (outdoor root draws it once; an interior root draws it no more than "
                + "once more, through surviving exit views). A second occurrence is a walk/"
                + "driver desync that would draw the sky twice");
        }

        FlagIfGrown();
        _signals.Add(StrideFrameEvent.Sky());
        ++_sceneryTurnsThisCycle;
    }

    private void ProcessPaintChambersPivot(IReadOnlyList<uint> chambers)
    {
        DemandOpenCycle();
        if (_latestDcJuncture is not { } juncture)
        {
            throw new InvalidOperationException(
                "A DrawCells turn fired prior to any DrawInside or Building turn established "
                + "which stage its cells belong to - a walk/driver desync ("
                + "fail-loud rule): CanonCycleStroll only ever emits DrawCells after DrawInside "
                + "(the interior root's own flood) or after a building's look-in portal pass");
        }

        if (juncture == StrollPaintJuncture.CellStatic)

            return;

        WriteFloodTurns(juncture, chambers);
    }

    private int GrabChamberLensCourse(uint chamberIdent)
    {
        int lensCourseOrdinal = _chamberLensCourseOrdinal++;
        GrabChamberViews(chamberIdent);
        return lensCourseOrdinal;
    }

    private void GrabChamberViews(uint chamberIdent)
    {
        var cx = DemandOpenCycle();
        StrollChamber? chamber = cx.FetchShown(chamberIdent);
        if (chamber is null || chamber.CountLens <= 0)
        {
            _gazeInTurns.Add(new StrideLookInTurn(
                chamberIdent, _gazeInSlices.Count, 0));
            return;
        }

        GrabViews(chamberIdent, chamber.TopLens);
    }

    private void GrabViews(uint chamberIdent, StridePortalView gatewayLens)
    {
        int sliceBegin = _gazeInSlices.Count;
        for (int sliceOrdinal = 0; sliceOrdinal < gatewayLens.ViewCount; ++sliceOrdinal)
        {
            var poly = gatewayLens.View.Polys[sliceOrdinal];
            int planeBegin = _gazeInPlanes.Count;
            for (int rim = 0; rim < poly.VertexCount; ++rim)
            {
                _gazeInPlanes.Add(
                    gatewayLens.View.Vertices[poly.VertexIndex + rim].Plane);
            }
            uint clipSocket = AffixClipSocket(gatewayLens, poly);
            _gazeInSlices.Add(new StrideLookInSlice(
                planeBegin, poly.VertexCount, clipSocket));
        }
        _gazeInTurns.Add(new StrideLookInTurn(
            chamberIdent, sliceBegin, _gazeInSlices.Count - sliceBegin));
    }

    private uint AffixClipSocket(StridePortalView gatewayLens, StrideViewPoly poly)
    {
        if (_clipCycle is null)
            return 0;

        ICanonFrameStrideScope cx = (ICanonFrameStrideScope)DemandOpenCycle();
        int tally = poly.VertexCount;
        if (tally < 3)
            return 0;

        Span<Vector2> ndc = stackalloc Vector2[Math.Min(tally, StrideCopyView.UpperVerts)];
        float lowerX = float.MaxValue, lowerY = float.MaxValue;
        float upperX = float.MinValue, upperY = float.MinValue;
        for (int idx = 0; idx < ndc.Length; ++idx)
        {
            Vector2 px = gatewayLens.View.Vertices[poly.VertexIndex + idx].Point;
            Vector2 pt = new(
                px.X / cx.ViewRectWidth * 2f - 1f,
                1f - px.Y / cx.ViewRectHeight * 2f);
            ndc[idx] = pt;
            lowerX = MathF.Min(lowerX, pt.X);
            lowerY = MathF.Min(lowerY, pt.Y);
            upperX = MathF.Max(upperX, pt.X);
            upperY = MathF.Max(upperY, pt.Y);
        }

        if (ndc.Length > ClipCycle.UpperPlanes)
        {
            Span<Vector4> aabbPlanes =
            [
                new(1f, 0f, 0f, -lowerX),
                new(-1f, 0f, 0f, upperX),
                new(0f, 1f, 0f, -lowerY),
                new(0f, -1f, 0f, upperY),
            ];
            return checked((uint)_clipCycle.AppendSlot(aabbPlanes));
        }

        float area2 = 0f;
        for (int idx = 0; idx < ndc.Length; ++idx)
            area2 += ndc[idx].X * ndc[(idx + 1) % ndc.Length].Y
                - ndc[(idx + 1) % ndc.Length].X * ndc[idx].Y;
        bool ccw = area2 >= 0f;
        Span<Vector4> planes = stackalloc Vector4[ClipCycle.UpperPlanes];
        for (int idx = 0; idx < ndc.Length; ++idx)
        {
            int latest = ccw ? idx : ndc.Length - 1 - idx;
            int upcoming = ccw
                ? (idx + 1) % ndc.Length
                : (ndc.Length - 2 - idx + ndc.Length) % ndc.Length;
            Vector2 p = ndc[latest];
            Vector2 q = ndc[upcoming];
            Vector2 direction = q - p;
            Vector2 norm = Vector2.Normalize(new Vector2(-direction.Y, direction.X));
            planes[idx] = new Vector4(norm.X, norm.Y, 0f, -Vector2.Dot(norm, p));
        }
        return checked((uint)_clipCycle.AppendSlot(planes[..ndc.Length]));
    }

    private void FlagIfGrown()
    {
        int tally = _flow.Count;
        int previous = _flagLoci.Count > 0 ? _flagLoci[^1] : 0;
        if (tally == previous)
            return;

        _flagLoci.Add(tally);
        _signals.Add(StrideFrameEvent.Flag(tally));
    }
}
