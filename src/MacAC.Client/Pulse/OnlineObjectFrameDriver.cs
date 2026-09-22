using System.Numerics;
using MacAC.Client.Controls;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Preferences;
using MacAC.Client.Realm;
using MacAC.Client.Sound;
using MacAC.Cockpit.Panels.Settings;
using MacAC.Mechanics.Drawing;
using MacAC.Mechanics.Effects;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Pulse;

internal interface IOnlineObjectFramePhase
{
    void Tick(float diffSecs);
}

internal interface IPostWireDirectiveFramePhase
{
    void ExecutePostNetworkDirectiveStage();
}

internal interface IOnlineSpatialReconcilePhase
{
    void Reconcile();
}

internal interface IRenderMirrorSyncPhase
{
    void SynchronizeEngagedSrcs();
}

internal interface IMoteRangeSource
{
    float SpanMultiplier { get; }
}

internal sealed class PreferencesMoteRangeSource(IEnginePreferencesPreviewSource settings) : IMoteRangeSource
{
    private readonly IEnginePreferencesPreviewSource _prefs = settings ?? throw new ArgumentNullException(nameof(settings));

    public float SpanMultiplier
    {
        get
        {
            return _prefs.ReadoutPreview.ParticleRange == MoteSpan.Extended
            ? MoteVisibilityDriver.ExtendedSpanMultiplier
            : 1f;
        }
    }
}

internal sealed class OnlineEffectFrameDriver(
    SeeThroughFadeKeeper translucencyFades,
    MotionHookFrameQueue animationHooks,
    ActorEffectDriver entityEffects,
    ParticleHookTap particleSink,
    OnlineActorLightDriver lights,
    MoteVisibilityDriver particleVisibility,
    MoteSys particles,
    KineticScriptRunner scripts,
    IKineticsScriptTimeSource scriptTime,
    IMoteRangeSource particleRange,
    IAmbientCycleStage? ambientCycle = null)
{
    private readonly SeeThroughFadeKeeper _seeThroughFades = translucencyFades
            ?? throw new ArgumentNullException(nameof(translucencyFades));
    private readonly MotionHookFrameQueue _animTaps = animationHooks ?? throw new ArgumentNullException(nameof(animationHooks));
    private readonly ActorEffectDriver _actorFxList = entityEffects ?? throw new ArgumentNullException(nameof(entityEffects));
    private readonly ParticleHookTap _moteDrain = particleSink ?? throw new ArgumentNullException(nameof(particleSink));
    private readonly OnlineActorLightDriver _lamps = lights ?? throw new ArgumentNullException(nameof(lights));
    private readonly MoteVisibilityDriver _moteVis = particleVisibility
            ?? throw new ArgumentNullException(nameof(particleVisibility));
    private readonly MoteSys _motes = particles ?? throw new ArgumentNullException(nameof(particles));
    private readonly KineticScriptRunner _programs = scripts ?? throw new ArgumentNullException(nameof(scripts));
    private readonly IKineticsScriptTimeSource _programMoment = scriptTime ?? throw new ArgumentNullException(nameof(scriptTime));
    private readonly IMoteRangeSource _moteSpan = particleRange ?? throw new ArgumentNullException(nameof(particleRange));
    private readonly IAmbientCycleStage? _ambientCycle = ambientCycle;

    public void Tick(float diffSecs)
    {
        _seeThroughFades.AdvanceAll(diffSecs);
        _animTaps.Drain();
        _actorFxList.RefreshLiveOwnerPoses();
        _moteDrain.RefreshAttachedEmitters();
        _lamps.Refresh();
        _moteVis.Apply(_motes, _moteSpan.SpanMultiplier);

        _motes.Tick(diffSecs);
        _programs.Tick(_programMoment.LatestProgramMoment);
        _ambientCycle?.PulseAmbient(diffSecs);
    }
}

internal sealed class HeavensPesActivationTurnstileSlot : IHeavensPesActivationTurnstile
{
    private IHeavensPesActivationTurnstile? _latch;

    public IDisposable BindOwned(IHeavensPesActivationTurnstile latch)
    {
        ArgumentNullException.ThrowIfNull(latch);
        if (_latch is not null)
            throw new InvalidOperationException("Sky presentation effects are by now bound");

        _latch = latch;
        return new Binding(this, latch);
    }

    public void Tick() => _latch?.Tick();

    private void Loosen(IHeavensPesActivationTurnstile anticipated)
    {
        if (ReferenceEquals(_latch, anticipated))
            _latch = null;
    }

    private sealed class Binding(
        HeavensPesActivationTurnstileSlot socket,
        IHeavensPesActivationTurnstile anticipated) : IDisposable
    {
        private HeavensPesActivationTurnstileSlot? _socket = socket;

        public void Dispose() =>
            Interlocked.Exchange(ref _socket, null)?.Loosen(anticipated);
    }
}

internal sealed class OnlineObjectFrameDriver(
    CanonInboundEventRouter inboundEvents,
    CanonAvatarFrameDriver localPlayerFrame,
    PickingDealingDriver? pickInteractions,
    OnlineActorCore liveEntities,
    IAvatarIdentitySource localPlayer,
    OnlineRealmOriginLedger origin,
    OnlineActorMotionRota animations,
    CanonStaticAnimatingObjectRota staticAnimations,
    OnlineActorMotionExhibitor animationPresenter,
    OnlineActorMotionEngineView<OnlineActorMotionLedger> animatedEntities,
    EquippedChildRenderDriver equippedChildren,
    OnlineEffectFrameDriver effects,
    IOnlineRenderMirrorSink? rasterizeProjections = null,
    StaticRenderMirrorDiary? staticRasterizeProjections = null) : IOnlineObjectFramePhase
{
    private readonly CanonInboundEventRouter _incomingSignals = inboundEvents ?? throw new ArgumentNullException(nameof(inboundEvents));
    private readonly CanonAvatarFrameDriver _ownAvatarCycle = localPlayerFrame
            ?? throw new ArgumentNullException(nameof(localPlayerFrame));
    private readonly PickingDealingDriver? _pickInteractions = pickInteractions;
    private readonly OnlineActorCore _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
    private readonly IAvatarIdentitySource _ownAvatar = localPlayer ?? throw new ArgumentNullException(nameof(localPlayer));
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly OnlineActorMotionRota _anims = animations ?? throw new ArgumentNullException(nameof(animations));
    private readonly CanonStaticAnimatingObjectRota _staticAnims = staticAnimations
            ?? throw new ArgumentNullException(nameof(staticAnimations));
    private readonly OnlineActorMotionExhibitor _animPresenter = animationPresenter
            ?? throw new ArgumentNullException(nameof(animationPresenter));
    private readonly OnlineActorMotionEngineView<OnlineActorMotionLedger>
        _animatedEntities = animatedEntities
            ?? throw new ArgumentNullException(nameof(animatedEntities));
    private readonly EquippedChildRenderDriver _equippedDescendants = equippedChildren
            ?? throw new ArgumentNullException(nameof(equippedChildren));
    private readonly OnlineEffectFrameDriver _fxList = effects ?? throw new ArgumentNullException(nameof(effects));
    private readonly HeavensPesActivationTurnstileSlot _heavensPesActivation = new();
    private readonly IOnlineRenderMirrorSink? _rasterizeProjections = rasterizeProjections;
    private readonly StaticRenderMirrorDiary? _staticRasterizeProjections = staticRasterizeProjections;
    private readonly List<RealmActor> _engagedStaticProjTemp = [];

    public void Tick(float diffSecs)
    {
        _incomingSignals.Run(
            this,
            diffSecs,
            static (driver, passed) => driver.TickCore(passed));
    }

    public IDisposable AttachHeavensPesActivationLatchPossessed(IHeavensPesActivationTurnstile latch) =>
        _heavensPesActivation.BindOwned(latch);

    private void TickCore(float diffSecs)
    {
        _ownAvatarCycle.AdvanceBeforeNetwork(diffSecs);
        _pickInteractions?.DrainOutbound();

        Vector3? avatarLocus =
            _onlineActors.TryFetchRealmActor(
                _ownAvatar.SrvOid,
                out RealmActor? avatar)
                ? avatar.Position
                : null;
        var schedules =
            _anims.Tick(
                diffSecs,
                avatarLocus,
                _ownAvatarCycle.ConcealedPiecePostureStale,
                _origin.CenterX,
                _origin.CenterY,
                _animPresenter.ReadyAnim);

        _staticAnims.Tick(diffSecs);
        if (_animatedEntities.Count > 0)
            _animPresenter.Present(schedules);
        _equippedDescendants.Tick();
        _rasterizeProjections?.SynchronizeEngagedSrcs();
        if (_staticRasterizeProjections is not null)
        {
            _staticAnims.DuplicateEngagedDatStaticActorsTo(
                _engagedStaticProjTemp);
            _staticRasterizeProjections.SynchronizeEngagedMovingSrcs(
                _engagedStaticProjTemp);
        }
        _staticAnims.ProcessHooks();
        _heavensPesActivation.Tick();
        _fxList.Tick(diffSecs);
    }
}

internal sealed class OnlineSpatialDisplayReconciler(
    ActorEffectDriver entityEffects,
    EquippedChildRenderDriver equippedChildren,
    ParticleHookTap particleSink,
    OnlineActorLightDriver lights,
    IOnlineRenderMirrorSink? rasterizeProjections = null) : IOnlineSpatialReconcilePhase
{
    private readonly ActorEffectDriver _actorFxList = entityEffects ?? throw new ArgumentNullException(nameof(entityEffects));
    private readonly EquippedChildRenderDriver _equippedDescendants = equippedChildren
            ?? throw new ArgumentNullException(nameof(equippedChildren));
    private readonly ParticleHookTap _moteDrain = particleSink ?? throw new ArgumentNullException(nameof(particleSink));
    private readonly OnlineActorLightDriver _lamps = lights ?? throw new ArgumentNullException(nameof(lights));
    private readonly IOnlineRenderMirrorSink? _rasterizeProjections = rasterizeProjections;

    public void Reconcile()
    {
        _actorFxList.RefreshLiveOwnerPoses();
        _equippedDescendants.ReconcileSpatialMutations();
        _rasterizeProjections?.SynchronizeEngagedSrcs();
        _moteDrain.RefreshAttachedEmitters();
        _lamps.Refresh();
    }
}
