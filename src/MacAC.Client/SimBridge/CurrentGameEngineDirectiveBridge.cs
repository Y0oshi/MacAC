using MacAC.Client.Dealing;
using MacAC.Sim;
using MacAC.Sim.Presence;

namespace MacAC.Client.SimBridge;

internal sealed partial class CurrentGameEngineDirectiveBridge(
    OnlineSessionDriver session,
    OnlineSessionHarbor sessionHost,
    IDirectiveBus commands,
    ISimCoreLens view,
    SimStashLedger inventory,
    SimToonLedger character,
    SimActionLedger actions,
    SimAvatarLocomotionLedger movement,
    SimFellowsLedger fellowship,
    PickingDealingDriver selection,
    ISimCoreEventSink events)
        : ISimSessionDirectives,
      ISimSelectionDirectives,
      ISimFightingDirectives,
      ISimArcanaDirectives,
      ISimLocomotionDirectives,
      ISimCommsDirectives,
      ISimPortalDirectives,
      ISimStashStateDirectives,
      ISimArcanabookDirectives,
      ISimToonDirectives,
      ISimSocialDirectives,
      ISimFellowsDirectives,
      ISimAllegianceDirectives
{
    private readonly OnlineSessionDriver _session = session ?? throw new ArgumentNullException(nameof(session));

    private readonly OnlineSessionHarbor _sessHub = sessionHost ?? throw new ArgumentNullException(nameof(sessionHost));

    private readonly IDirectiveBus _commands = commands ?? throw new ArgumentNullException(nameof(commands));

    private readonly ISimCoreLens _lens = view ?? throw new ArgumentNullException(nameof(view));

    private readonly SimStashLedger _satchel = inventory ?? throw new ArgumentNullException(nameof(inventory));

    private readonly SimToonLedger _toon = character ?? throw new ArgumentNullException(nameof(character));

    private readonly SimActionLedger _actions = actions
            ?? throw new ArgumentNullException(nameof(actions));

    private readonly SimAvatarLocomotionLedger _movement = movement
            ?? throw new ArgumentNullException(nameof(movement));

    private readonly SimFellowsLedger _fellowship = fellowship
            ?? throw new ArgumentNullException(nameof(fellowship));

    private readonly PickingDealingDriver _pick = selection ?? throw new ArgumentNullException(nameof(selection));

    private readonly ISimCoreEventSink _signals = events ?? throw new ArgumentNullException(nameof(events));
}
