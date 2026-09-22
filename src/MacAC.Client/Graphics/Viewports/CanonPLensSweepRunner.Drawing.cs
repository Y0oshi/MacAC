using System.Numerics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Stride;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Graphics;

internal sealed partial class CanonPLensSweepRunner
{
    public void PaintSolidChamberShells(HashSet<uint> chamberIdents) =>
        _environChambers.Render(BatchRenderPass.Opaque, chamberIdents);

    public void PaintSeeThruChamberShellsSequenced(IReadOnlyList<uint> chamberIdents) =>
        _environChambers.RasterizeSeeThruSequenced(chamberIdents);

    public void DrawWeatherOnce(CanonPViewFrameInput cycle)
    {
        if (!ShouldPaintWeatherOnce(cycle.RasterizeHeavens, cycle.RasterizeWeather, cycle.AvatarChamberIdent))
            return;

        _heavens?.PaintWeather(
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
                ParticleDrawPass.SkyPostScene);
        }
    }

    public void PaintSceneryStaticMotes(
        CanonPViewFrameInput cycle,
        uint chamberIdent)
    {
        if (_motes is not null && _motePainter is not null)
        {
            _motePainter.PaintForChamber(
                cycle.Camera,
                cycle.CamRealmLocus,
                ParticleDrawPass.Scene,
                chamberIdent,
                clipSocket: 0);
        }
    }

    public int PaintQuitGatewayBitmask(
        CanonPViewFrameInput cycle,
        uint chamberIdent,
        ReadOnlySpan<Vector4> clipPlanes)
    {
        return DrawPortalDepthWrite(
            chamberIdent,
            clipPlanes,
            cycle,
            forceFarawayZ: cycle.TrunkChamber.IsExteriorJoint);
    }

    public void DrawUnattachedSceneParticles(
        CanonPViewFrameInput cycle,
        bool exteriorChambers)
    {
        if (_motes is null || _motePainter is null)
            return;

        _motePainter.PaintForHolders(
            cycle.Camera,
            cycle.CamRealmLocus,
            ParticleDrawPass.Scene,
            _noTableauMoteActorIdents,
            includeUnattached: true,
            clipSocket: 0,
            unattachedChamberAmbit: exteriorChambers
                ? LooseEmitterCellScope.OutdoorCells
                : LooseEmitterCellScope.InteriorCells);
    }

    public void PaintChamberMotes(
        CanonPViewFrameInput cycle,
        uint chamberIdent)
    {
        if (_motes is null || _motePainter is null)
            return;

        _motePainter.PaintForChamber(
            cycle.Camera,
            cycle.CamRealmLocus,
            ParticleDrawPass.Scene,
            chamberIdent,
            clipSocket: 0);
    }

    private void PaintImmediateEnvironChamberCourse(
        uint chamberIdent,
        EnvironChamberSeeThruCourse course,
        bool specificsCanvasEngaged)
    {
        _singleChamberRosterTemp.Clear();
        _singleChamberRosterTemp.Add(chamberIdent);
        _environChambers.PaintSeeThruSequenced(
            _singleChamberRosterTemp,
            course,
            specificsCanvasEngaged);
    }

    void IEnvironChamberImmediatePaintDrain.PaintImmediate(
        uint chamberIdent,
        EnvironChamberSeeThruCourse course,
        bool specificsCanvasEngaged)
    {
        PaintImmediateEnvironChamberCourse(chamberIdent, course, specificsCanvasEngaged);
    }

    private int DrawPortalDepthWrite(
        uint chamberIdent,
        ReadOnlySpan<Vector4> clipPlanes,
        CanonPViewFrameInput cycle,
        bool forceFarawayZ,
        int? soleGatewayOrdinal = null)
    {
        if (_portalDepthMask is null)
            return 0;
        FetchedChamber? chamber = cycle.Cells.Find(chamberIdent);
        if (chamber is null)
            return 0;

        int submitted = 0;
        Span<Vector3> realm = stackalloc Vector3[32];
        for (int ordinal = 0; ordinal < chamber.Portals.Count; ++ordinal)
        {
            if (soleGatewayOrdinal.HasValue && ordinal != soleGatewayOrdinal.Value)
                continue;
            if (chamber.Portals[ordinal].OtherCellId != 0xFFFF)
                continue;
            if (ordinal >= chamber.PortalPolygons.Count)
                break;
            Vector3[] ownVerts = chamber.PortalPolygons[ordinal];

            if (StrideVisibilityMath.IsRejectedByGatewayPolygBoundaryGuard(ownVerts))
                continue;

            int tally = Math.Min(ownVerts.Length, realm.Length);
            for (int vert = 0; vert < tally; ++vert)
            {
                realm[vert] = Vector3.Transform(
                    ownVerts[vert],
                    chamber.WorldTransform);
            }

            ++submitted;

            _portalDepthMask.PaintZDepthFan(
                realm[..tally],
                cycle.LensMirror,
                clipPlanes,
                forceFarawayZ);
        }
        return submitted;
    }
}
