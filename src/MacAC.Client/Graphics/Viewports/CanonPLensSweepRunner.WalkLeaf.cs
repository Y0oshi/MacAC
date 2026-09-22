using System.Diagnostics;
using System.Numerics;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonPLensSweepRunner
{
    internal void DrawWalkSky(CanonPViewFrameInput cycle)
    {
        if (!cycle.RasterizeHeavens)
            return;

        _heavens?.PaintHeavens(
            cycle.Camera,
            cycle.CamRealmLocus,
            cycle.DayRatio,
            cycle.EngagedDayCluster,
            cycle.SkyKeyframe,
            cycle.EnvironOverrideEngaged);
        if (_motes is not null && _motePainter is not null)
        {
            _motePainter.Draw(
                cycle.Camera,
                cycle.CamRealmLocus,
                ParticleDrawPass.SkyPreScene);
        }
    }

    internal void DrawWalkLandCellBatch(
        CanonPViewFrameInput cycle,
        IReadOnlyList<(uint LandblockId, int SideCellCount, int CellIndex)> chambers)
    {
        long begin = Stopwatch.GetTimestamp();
        _land?.PaintLandChambers(cycle.LensMirror, chambers);
        _landTelemetry.AmassStrollLot(Stopwatch.GetTimestamp() - begin);
    }

    internal void ConcludeStrollLandCycle() => _landTelemetry.FinishStrollCycle();

    internal bool HasStrollRenderableSpoutsInChamber(uint chamberIdent)
    {
        return _motes?.HasRenderableSpoutsInCell(ParticleDrawPass.Scene, chamberIdent) ?? false;
    }

    internal void PaintStrollPunchFan(
        CanonPViewFrameInput cycle,
        ClipCycleAssembly clipAssembly,
        StridePolygon realmPolyg,
        int activeViewIndex)
    {
        if (_portalDepthMask is null)
            return;
        Vector3[] verts = realmPolyg.Vertices;
        if (verts.Length < 3)
            return;

        ReadOnlySpan<ClipLensSlice> slices = clipAssembly.BeyondLensSlices;
        if ((uint)activeViewIndex >= (uint)slices.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeViewIndex),
                activeViewIndex,
                $"punch fan pinned to walk view {activeViewIndex} but the "
                + $"reassembled beyond view only holds {slices.Length} "
                + "index-aligned slice(s) - ReassembleOutsideViewFromWalk "
                + "desynchronized from RetailFrameWalk.DrawBuilding's own "
                + "view count (fail-loud rule; never draw unclipped)");
        }

        var slice = slices[activeViewIndex];
        if (slice.NothingVisible)
            return;

        Span<Vector3> realm = stackalloc Vector3[32];
        int tally = Math.Min(verts.Length, realm.Length);
        for (int vert = 0; vert < tally; ++vert)
            realm[vert] = verts[vert];
        _portalDepthMask.PaintZDepthFan(
            realm[..tally],
            cycle.LensMirror,
            slice.Planes,
            forceFarawayZ: true);
    }
}

internal sealed class StrideProductionLeafPainter : IStrideFrameLeafPainter
{
    private CanonPLensSweepRunner _passs = null!;
    private CanonPViewFrameInput _cycle = null!;
    private ClipCycleAssembly _clipAssembly = null!;
    private Action _drainScenery = null!;
    private Action _wipeInteriorZDepth = null!;
    private Func<int> _paintQuitSeals = null!;
    private readonly HashSet<uint> _singleChamberTemp = [];

    internal StrideProductionLeafPainter(
        CanonPLensSweepRunner passs,
        CanonPViewFrameInput cycle,
        ClipCycleAssembly clipAssembly,
        Action drainScenery,
        Action wipeInteriorZDepth,
        Func<int> paintQuitSeals)
        => Reset(passs, cycle, clipAssembly, drainScenery, wipeInteriorZDepth, paintQuitSeals);

    public void SketchHeavens() => _passs.DrawWalkSky(_cycle);

    public void PaintLandChamberLot(
        IReadOnlyList<(uint LandblockId, int SideCellCount, int CellIndex)> chambers) =>
        _passs.DrawWalkLandCellBatch(_cycle, chambers);

    public bool HasRenderableSpoutsInChamber(uint chamberIdent) =>
        _passs.HasStrollRenderableSpoutsInChamber(chamberIdent);

    public void PaintChamberShell(uint chamberIdent)
    {
        _singleChamberTemp.Clear();
        _singleChamberTemp.Add(chamberIdent);
        _passs.PaintSolidChamberShells(_singleChamberTemp);
        if (_passs.ChamberHasSeeThruShell(chamberIdent))

            _passs.SubmitOrDrawTransparentCellShell(chamberIdent);
    }

    public void DrainScenery() => _drainScenery();

    public void WipeInteriorDepth() => _wipeInteriorZDepth();

    public ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyStaticMotes(uint chamberIdent) =>
        _passs.ReadyChamberMoteAlpha(_cycle, chamberIdent);

    public ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyChamberMotes(uint chamberIdent) =>
        _passs.ReadyChamberMoteAlpha(_cycle, chamberIdent);

    public int PaintQuitSeals() => _paintQuitSeals();

    public void PaintPunchFan(StridePolygon realmPolyg, int engagedLensOrdinal)
    {
        _passs.PaintStrollPunchFan(_cycle, _clipAssembly, realmPolyg, engagedLensOrdinal);
    }

    public void AlphaBarrier() => _passs.DrainStructureAlpha();

    public void DrainOrderChamberQuit() => _passs.DrainOrderChamberQuitAlpha();

    internal void Reset(
        CanonPLensSweepRunner passes,
        CanonPViewFrameInput frame,
        ClipCycleAssembly clipAssembly,
        Action flushLandscape,
        Action clearInteriorDepth,
        Func<int> drawExitSeals)
    {
        _passs = passes ?? throw new ArgumentNullException(nameof(passes));
        _cycle = frame ?? throw new ArgumentNullException(nameof(frame));
        _clipAssembly = clipAssembly
            ?? throw new ArgumentNullException(nameof(clipAssembly));
        _drainScenery = flushLandscape
            ?? throw new ArgumentNullException(nameof(flushLandscape));
        _wipeInteriorZDepth = clearInteriorDepth
            ?? throw new ArgumentNullException(nameof(clearInteriorDepth));
        _paintQuitSeals = drawExitSeals
            ?? throw new ArgumentNullException(nameof(drawExitSeals));
    }
}
