using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Realm;

internal interface IOnlineActorTeardownMarshal
    : IOnlineActorEngineComponentLifespan
{
    void DiscardUnknownHolder(uint srvOid);
}

internal sealed class OnlineActorEngineTeardownDriver
    : IOnlineActorTeardownMarshal
{
    private readonly OnlineActorCore? _runtime;
    private readonly OnlineActorDisplayDriver? _exhibit;
    private readonly ActorEffectDriver? _fxList;
    private readonly PickingDealingDriver? _interactions;
    private readonly PickPhase? _pick;
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger>? _anims;
    private readonly RemoteLocomotionObservationLedger? _distantTravelObservations;
    private readonly SeeThroughFadeKeeper? _seeThroughFades;
    private readonly OnlineActorMirrorWithdrawalDriver? _projWithdrawal;
    private readonly EquippedChildRenderDriver? _descendants;
    private readonly ProxyRegistry? _shades;
    private readonly OnlineActorLightDriver? _lamps;
    private readonly ActorTaxonomyStash? _taxonomy;
    private readonly IAvatarIdentitySource? _identity;
    private readonly Func<OnlineActorRecord, OnlineActorTeardownPlan> _buildPlan;
    private readonly Action<uint> _forgetUnknownHolder;

    public OnlineActorEngineTeardownDriver(
        OnlineActorCore runtime,
        OnlineActorDisplayDriver presentation,
        ActorEffectDriver effects,
        PickingDealingDriver? interactions,
        PickPhase selection,
        OnlineActorMotionEngineView<OnlineActorMotionLedger> animations,
        RemoteLocomotionObservationLedger remoteMovementObservations,
        SeeThroughFadeKeeper translucencyFades,
        OnlineActorMirrorWithdrawalDriver projectionWithdrawal,
        EquippedChildRenderDriver children,
        ProxyRegistry shadows,
        OnlineActorLightDriver lights,
        ActorTaxonomyStash classification,
        IAvatarIdentitySource identity)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _exhibit = presentation ?? throw new ArgumentNullException(nameof(presentation));
        _fxList = effects ?? throw new ArgumentNullException(nameof(effects));
        _interactions = interactions;
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _anims = animations ?? throw new ArgumentNullException(nameof(animations));
        _distantTravelObservations = remoteMovementObservations
            ?? throw new ArgumentNullException(nameof(remoteMovementObservations));
        _seeThroughFades = translucencyFades
            ?? throw new ArgumentNullException(nameof(translucencyFades));
        _projWithdrawal = projectionWithdrawal
            ?? throw new ArgumentNullException(nameof(projectionWithdrawal));
        _descendants = children ?? throw new ArgumentNullException(nameof(children));
        _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
        _lamps = lights ?? throw new ArgumentNullException(nameof(lights));
        _taxonomy = classification
            ?? throw new ArgumentNullException(nameof(classification));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _buildPlan = BuildPlan;
        _forgetUnknownHolder = effects.DropUnknownHolder;
    }

    internal OnlineActorEngineTeardownDriver(
        Func<OnlineActorRecord, OnlineActorTeardownPlan> createPlan,
        Action<uint> forgetUnknownOwner)
    {
        _buildPlan = createPlan ?? throw new ArgumentNullException(nameof(createPlan));
        _forgetUnknownHolder = forgetUnknownOwner
            ?? throw new ArgumentNullException(nameof(forgetUnknownOwner));
    }

    public void TearDown(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        capture.CoreModuleTeardownPlan ??= _buildPlan(capture);
        capture.CoreModuleTeardownPlan.Advance();
    }

    public void DiscardUnknownHolder(uint srvOid) =>
        _forgetUnknownHolder(srvOid);

    private OnlineActorTeardownPlan BuildPlan(OnlineActorRecord capture)
    {
        var core = _runtime!;
        uint srvOid = capture.ServerOid;
        List<Action> cleanups = new List<Action>
        {
            () => _exhibit!.Forget(capture),
            () => _fxList!.OnLiveActorUnregistered(capture),
            () =>
            {
                bool substituteExists =
                    core.TryFetchRecord(srvOid, out OnlineActorRecord latest)
                    && !ReferenceEquals(latest, capture);
                if (_interactions is { } interactions)
                {
                    interactions.OnActorRemoved(capture, substituteExists);
                }
                else if (!substituteExists
                    && _pick!.ChosenObjectTag == srvOid)
                {
                    _pick.Clear(
                        PickChangeSource.System,
                        PickChangeReason.SelectedObjectRemoved);
                }
            },
        };

        if (capture.WorldEntity is not { } extantActor)
            return new OnlineActorTeardownPlan(cleanups);

        if (_anims!.TryGetValue(extantActor.Id, out OnlineActorMotionLedger anim))
            cleanups.Add(() => anim.Sequencer?.Manager.ServiceQuitRealm());
        if (capture.RemoteMotionRuntime is PeerMotion distantLocomotion)
            cleanups.Add(distantLocomotion.Movement.ProcessQuitRealm);
        if (capture.PhysicsHost is ActorKineticsHarbor kineticsHub)
        {
            cleanups.Add(kineticsHub.LocusKeeper.UnStick);
            cleanups.Add(kineticsHub.WipeMark);
            cleanups.Add(kineticsHub.AlertQuitRealm);
        }

        cleanups.Add(() => _anims.Drop(capture));
        cleanups.Add(() => _taxonomy!.DirtyActor(extantActor.Id));
        if (capture.ProjTag is { } projTag)
            cleanups.Add(() => _distantTravelObservations!.Drop(projTag));
        cleanups.Add(() => _seeThroughFades!.WipeActor(extantActor.Id));
        cleanups.Add(() => _projWithdrawal!.ExitRealm(
            capture,
            _identity!.SrvOid));
        cleanups.Add(() => _descendants!.OnLogicalTeardown(capture));
        cleanups.Add(() => _shades!.Deregister(extantActor.Id));
        cleanups.Add(() => _lamps!.Forget(capture));
        return new OnlineActorTeardownPlan(cleanups);
    }
}
