using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Paging;

public sealed class LandblockDisplayRetirementOwner
{
    private readonly LandblockRenderHerald _rasterize;
    private readonly LandblockKineticsHerald _physics;
    private readonly LandblockStaticDisplayHerald _staticExhibit;
    private readonly LightingHookTap _illumination;
    private readonly SeeThroughFadeKeeper _seeThrough;

    public LandblockDisplayRetirementOwner(
        LandblockRenderHerald render,
        LandblockKineticsHerald physics,
        LandblockStaticDisplayHerald staticPresentation,
        LightingHookTap lighting,
        SeeThroughFadeKeeper translucency)
    {
        _rasterize = render ?? throw new ArgumentNullException(nameof(render));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _staticExhibit = staticPresentation
            ?? throw new ArgumentNullException(nameof(staticPresentation));
        _illumination = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _seeThrough = translucency
            ?? throw new ArgumentNullException(nameof(translucency));
        if (!_staticExhibit.FitsAssetList(_illumination, _seeThrough))
        {
            throw new ArgumentException(
                "The retirement resources has to be those owned by the static " +
                "presentation publisher",
                nameof(staticPresentation));
        }
    }

    public void Advance(LandblockSunsetTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        ticket.ExecuteForEachActor(
            LandblockSunsetJuncture.EntityLighting,
            static actor => actor.ServerGuid == 0,
            _staticExhibit.DropIllumination);
        ticket.ExecuteForEachActor(
            LandblockSunsetJuncture.EntityTranslucency,
            static actor => actor.ServerGuid == 0,
            _staticExhibit.DropSeeThrough);
        ticket.ExecuteForEachActor(
            LandblockSunsetJuncture.PluginProjection,
            static actor => actor.ServerGuid == 0,
            _staticExhibit.DropExtensionProj);

        if (!ticket.RunOnce(
            LandblockSunsetJuncture.Physics,
            () => ticket.Kind == LandblockSunsetFlavor.Full
                ? _physics.ProgressDeletion(ticket.LandblockId)
                : _physics.ProgressDemotion(ticket.LandblockId)))

            return;
        if (ticket.Kind == LandblockSunsetFlavor.Full)
        {
            ticket.RunOnce(
                LandblockSunsetJuncture.Terrain,
                () => _rasterize.DropLand(ticket.LandblockId));
        }
        ticket.RunOnce(
            LandblockSunsetJuncture.CellVisibility,
            () => _rasterize.DropChamberVis(ticket.LandblockId));
        ticket.RunOnce(
            LandblockSunsetJuncture.BuildingRegistry,
            () => _rasterize.DropStructureRegistry(ticket.LandblockId));
        ticket.RunOnce(
            LandblockSunsetJuncture.EnvironmentCells,
            () => _rasterize.DropSurroundingsChambers(ticket.LandblockId));
    }

    internal bool Fits(
        LandblockRenderHerald rasterize,
        LandblockKineticsHerald kinetics,
        LandblockStaticDisplayHerald staticExhibit)
    {
        return ReferenceEquals(_rasterize, rasterize)
        && ReferenceEquals(_physics, kinetics)
        && ReferenceEquals(_staticExhibit, staticExhibit)
        && _staticExhibit.FitsAssetList(_illumination, _seeThrough);
    }

    internal LandblockSunsetOpOutcome ProgressOne(
        LandblockSunsetTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.UpcomingIncompleteJuncture switch
        {
            LandblockSunsetJuncture.EntityLighting =>
                ticket.ExecuteActorHop(
                    LandblockSunsetJuncture.EntityLighting,
                    static actor => actor.ServerGuid == 0,
                    _staticExhibit.DropIllumination),
            LandblockSunsetJuncture.EntityTranslucency =>
                ticket.ExecuteActorHop(
                    LandblockSunsetJuncture.EntityTranslucency,
                    static actor => actor.ServerGuid == 0,
                    _staticExhibit.DropSeeThrough),
            LandblockSunsetJuncture.PluginProjection =>
                ticket.ExecuteActorHop(
                    LandblockSunsetJuncture.PluginProjection,
                    static actor => actor.ServerGuid == 0,
                    _staticExhibit.DropExtensionProj),
            LandblockSunsetJuncture.Terrain =>
                ticket.ExecuteOnceHop(
                    LandblockSunsetJuncture.Terrain,
                    () => _rasterize.DropLand(ticket.LandblockId)),
            LandblockSunsetJuncture.Physics =>
                ticket.ExecuteOnceHop(
                    LandblockSunsetJuncture.Physics,
                    () => ticket.Kind == LandblockSunsetFlavor.Full
                        ? _physics.ProgressDeletion(ticket.LandblockId)
                        : _physics.ProgressDemotion(ticket.LandblockId)),
            LandblockSunsetJuncture.CellVisibility =>
                ticket.ExecuteOnceHop(
                    LandblockSunsetJuncture.CellVisibility,
                    () => _rasterize.DropChamberVis(ticket.LandblockId)),
            LandblockSunsetJuncture.BuildingRegistry =>
                ticket.ExecuteOnceHop(
                    LandblockSunsetJuncture.BuildingRegistry,
                    () => _rasterize.DropStructureRegistry(ticket.LandblockId)),
            LandblockSunsetJuncture.EnvironmentCells =>
                ticket.ExecuteOnceHop(
                    LandblockSunsetJuncture.EnvironmentCells,
                    () => _rasterize.DropSurroundingsChambers(ticket.LandblockId)),
            _ => LandblockSunsetOpOutcome.NoWork,
        };
    }
}
