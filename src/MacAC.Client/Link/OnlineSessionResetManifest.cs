namespace MacAC.Client.Link;

using MacAC.Client.Realm;
using MacAC.Sim;
using MacAC.Sim.Actors;

internal sealed class OnlineSessionResetWiring
{
    public required Action PointerGrab { get; init; }
    public required Action AvatarExhibit { get; init; }
    public required Action WarpExhibit { get; init; }
    public required Action RealmSound { get; init; }
    public required Action SessPopups { get; init; }
    public required Action PrefsToonCtx { get; init; }
    public required Action EquippedDescendants { get; init; }
    public required Action DealingExhibit { get; init; }
    public required Action PickExhibit { get; init; }
    public required Action MoteVis { get; init; }
    public required Action IncomingSignalFifo { get; init; }
    public required Action OnlineLiveness { get; init; }
    public required Action<SimEpochTicket> CoreGen { get; init; }
    public required Action<SimEpochTicket> SessPersonaExhibit
    { get; init; }
    public required Action NetworkFxList { get; init; }
    public required Action AnimTapCycles { get; init; }
    public required Action OnlineExhibit { get; init; }
    public required Action DistantTravelTelemetry { get; init; }
}

internal static class OnlineSessionResetManifest
{
    public static OnlineSessionResetPlan Create(OnlineSessionResetWiring mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        return new OnlineSessionResetPlan(
        [
            new("mouse capture", mappings.PointerGrab),
            new("player presentation", mappings.AvatarExhibit),
            new("teleport presentation", mappings.WarpExhibit),
            new("world audio", mappings.RealmSound),
            new("session dialogs", mappings.SessPopups),
            new("settings character context", mappings.PrefsToonCtx),
            new("equipped children", mappings.EquippedDescendants),
            new("interaction presentation", mappings.DealingExhibit),
            new("selection presentation", mappings.PickExhibit),
            new("particle visibility", mappings.MoteVis),
            new("inbound event fifo", mappings.IncomingSignalFifo),
            new("live liveness", mappings.OnlineLiveness),
            new("runtime generation", mappings.CoreGen),
            new(
                "session identity presentation",
                mappings.SessPersonaExhibit),
            new("network effects", mappings.NetworkFxList),
            new("animation hook frames", mappings.AnimTapCycles),
            new("live presentation", mappings.OnlineExhibit),
            new("remote movement diagnostics", mappings.DistantTravelTelemetry),
        ]);
    }
}

internal sealed class GraphicalEngineEpochResetHost(
    OnlineActorCore entities,
    Action drainRenderProjection)
        : ISimEpochResetHarbor
{
    private readonly OnlineActorCore _actors = entities
            ?? throw new ArgumentNullException(nameof(entities));
    private readonly Action _emptyRasterizeProj = drainRenderProjection
            ?? throw new ArgumentNullException(nameof(drainRenderProjection));

    public void RetireActorProj(
        SimActorRecord actor) =>
        _actors.RetireGenProj(actor);

    public void EmptyActorProjBoundary() =>
        _emptyRasterizeProj();

    public void ConcludeActorProjSunset() =>
        _actors.ConcludeGenProjSunset();
}
