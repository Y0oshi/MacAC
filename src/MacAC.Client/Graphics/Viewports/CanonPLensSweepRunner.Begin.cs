using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonPLensSweepRunner
{
    public void BeginFrame()
    {
    }

    public ClipCycleAssembly CommenceStrollClipCycle(
        bool exteriorTrunk,
        ClipCycleAssembly reuseAssembly)
    {
        return ClipCycleAssembler.CommenceStrollCycle(_clipCycle, exteriorTrunk, reuseAssembly);
    }

    private readonly RealmPaintRouter _actors;

    internal RealmPaintRouter Router => _actors;

    public void AbortFrame()
    {
        List<Exception>? misses = null;
        TryCancel(_cycleGlPhase.ReinstateCycleDefaults);
        TryCancel(_noTableauMoteActorIdents.Clear);
        if (misses is { Count: > 0 })
            throw new AggregateException("Retail PView pass abort failed", misses);

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

    internal (int Width, int Height)? StrollAffixReach =>
        _actors.StrollAffixReach;

    public void ReadyClipCycle() =>
        _canvas.StageClipCycle();

    public void ReadyChamberLots(
        CanonPViewFrameInput cycle,
        HashSet<uint> shownChamberIdents)
    {
        _environChambers.ReadyRasterizeLots(
            cycle.LensMirror,
            cycle.CamRealmLocus,
            sift: shownChamberIdents,
            middleLbX: cycle.RasterizeMiddleLbX,
            middleLbY: cycle.RasterizeMiddleLbY,
            rasterizeRadius: cycle.RasterizeRadius);
    }

    public bool ChamberHasSeeThruShell(uint chamberIdent) =>
        _environChambers.ChamberHasSeeThru(chamberIdent);

    public void PurgeInteriorZDepth() => _canvas.ClearInteriorZDepth();

    public void DrainSceneryAlpha() =>
        _alpha.Flush(CanonAlphaFlushSite.LandscapeFlush, 0f);
    internal (IGpuCycle Frame, IGpuSweepCoder Encoder) DemandStrollSubmission() =>
        _actors.DemandStrollSubmission();

    internal ReadOnlySpan<PreparedMoteAlphaSubmission> ReadyChamberMoteAlpha(
        CanonPViewFrameInput cycle,
        uint chamberIdent)
    {
        return _motes is null || _motePainter is null
            ? []
            : _motePainter.ReadyForChamberAlpha(
            cycle.Camera,
            cycle.CamRealmLocus,
            ParticleDrawPass.Scene,
            chamberIdent,
            clipSocket: 0);
    }

    internal void SubmitOrDrawTransparentCellShell(uint chamberIdent)
    {
        bool specificsCanvasEngaged = _environChambers.TransparentDetailEnabled;
        var courses = _environChambers.FetchSeeThruCourses(
            chamberIdent,
            specificsCanvasEngaged);
        RelaySeeThruChamberShell(
            chamberIdent,
            courses,
            specificsCanvasEngaged,
            _alpha,
            _environChamberClipAlphaSrc,
            _environChamberBlendAlphaSrc,
            this);
    }

    internal static void RelaySeeThruChamberShell(
        uint chamberIdent,
        EnvironChamberSeeThruCourse courses,
        bool specificsCanvasEngaged,
        CanonAlphaFifo fifo,
        EnvironChamberAlphaPaintOrigin clipSrc,
        EnvironChamberAlphaPaintOrigin alphaSrc,
        IEnvironChamberImmediatePaintDrain rasterizeImmediate)
    {
        if ((courses & EnvironChamberSeeThruCourse.Immediate) != 0)
        {
            rasterizeImmediate.PaintImmediate(
                chamberIdent,
                EnvironChamberSeeThruCourse.Immediate,
                specificsCanvasEngaged);
        }

        if ((courses & EnvironChamberSeeThruCourse.Clip) != 0)
        {
            int ticket = clipSrc.AppendQueuedChamberIdent(chamberIdent);
            if (!fifo.TryAffix(
                    CanonAlphaList.Clip,
                    clipSrc,
                    ticket,
                    overrideClipmap: false))

                clipSrc.RevertQueuedChamberIdent(ticket);
        }

        if ((courses & EnvironChamberSeeThruCourse.Alpha) != 0)
        {
            int ticket = alphaSrc.AppendQueuedChamberIdent(chamberIdent);
            if (!fifo.TryAffix(
                    CanonAlphaList.Alpha,
                    alphaSrc,
                    ticket,
                    overrideClipmap: false))

                alphaSrc.RevertQueuedChamberIdent(ticket);
        }
    }

    internal static void RelaySeeThruChamberShell(
        uint chamberIdent,
        EnvironChamberSeeThruCourse courses,
        bool specificsCanvasEngaged,
        CanonAlphaFifo fifo,
        EnvironChamberAlphaPaintOrigin clipSrc,
        EnvironChamberAlphaPaintOrigin alphaSrc,
        RenderImmediateEnvCellRoute rasterizeImmediate)
    {
        if ((courses & EnvironChamberSeeThruCourse.Immediate) != 0)
        {
            rasterizeImmediate(
                chamberIdent,
                EnvironChamberSeeThruCourse.Immediate,
                specificsCanvasEngaged);
        }

        if ((courses & EnvironChamberSeeThruCourse.Clip) != 0)
        {
            int ticket = clipSrc.AppendQueuedChamberIdent(chamberIdent);
            if (!fifo.TryAffix(
                    CanonAlphaList.Clip,
                    clipSrc,
                    ticket,
                    overrideClipmap: false))

                clipSrc.RevertQueuedChamberIdent(ticket);
        }

        if ((courses & EnvironChamberSeeThruCourse.Alpha) != 0)
        {
            int ticket = alphaSrc.AppendQueuedChamberIdent(chamberIdent);
            if (!fifo.TryAffix(
                    CanonAlphaList.Alpha,
                    alphaSrc,
                    ticket,
                    overrideClipmap: false))

                alphaSrc.RevertQueuedChamberIdent(ticket);
        }
    }

    internal static bool ShouldPaintWeatherOnce(
        bool rasterizeHeavens, bool rasterizeWeather, uint avatarChamberIdent)
    {
        return rasterizeHeavens && rasterizeWeather && (avatarChamberIdent & 0xFFFFu) < 0x100u;
    }

    internal void DrainStructureAlpha() =>
        _alpha.Flush(CanonAlphaFlushSite.DrawBuilding, 0f);

    internal void DrainOrderChamberQuitAlpha() =>
        _alpha.Flush(CanonAlphaFlushSite.SortCellExit, 0.75f);
}
