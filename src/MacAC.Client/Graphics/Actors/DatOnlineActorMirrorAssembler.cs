using MacAC.Assets;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Kinetics;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.PluginHosting;
using MacAC.Sim.Realm;

namespace MacAC.Client.Graphics;

internal sealed partial class DatOnlineActorMirrorAssembler(
    EngineKnobs options,
    IDatAccess dats,
    OnlineActorCore runtime,
    OnlineContactAssetHerald collisionAssets,
    IAnimReader animationLoader,
    ActorSummonBridge spawnAdapter,
    BitmapStash textures,
    ActorTaxonomyStash classification,
    ActorEffectPoseRegistry effectPoses,
    EquippedChildRenderDriver equippedChildren,
    RealmPlayPhase worldState,
    RealmSignals worldEvents,
    ProxyRegistry shadows,
    OnlineActorContactAssembler collisionBuilder,
    MissileDriver projectiles,
    OnlineActorMotionExhibitor animationPresenter,
    CanonStaticAnimatingObjectRota staticAnimations,
    OnlineRealmOriginLedger origin,
    IKineticsScriptTimeSource gameTime,
    SimRealmCrossingLedger transit)
        : IOnlineActorMirrorAssembler
{
    private readonly EngineKnobs _knobs = options ?? throw new ArgumentNullException(nameof(options));

    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));

    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    private readonly OnlineContactAssetHerald _impactHoldings = collisionAssets ??
            throw new ArgumentNullException(nameof(collisionAssets));

    private readonly IAnimReader _animFetcher = animationLoader ?? throw new ArgumentNullException(nameof(animationLoader));

    private readonly ActorSummonBridge _summonBridge = spawnAdapter ?? throw new ArgumentNullException(nameof(spawnAdapter));

    private readonly BitmapStash _textures = textures ?? throw new ArgumentNullException(nameof(textures));

    private readonly ActorTaxonomyStash _taxonomy = classification ?? throw new ArgumentNullException(nameof(classification));

    private readonly ActorEffectPoseRegistry _fxPostures = effectPoses ?? throw new ArgumentNullException(nameof(effectPoses));

    private readonly EquippedChildRenderDriver _equippedDescendants = equippedChildren ?? throw new ArgumentNullException(nameof(equippedChildren));

    private readonly RealmPlayPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));

    private readonly RealmSignals _realmSignals = worldEvents ?? throw new ArgumentNullException(nameof(worldEvents));

    private readonly ProxyRegistry _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));

    private readonly OnlineActorContactAssembler _impactBuilder = collisionBuilder ?? throw new ArgumentNullException(nameof(collisionBuilder));

    private readonly MissileDriver _missiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));

    private readonly OnlineActorMotionExhibitor _animPresenter = animationPresenter ?? throw new ArgumentNullException(nameof(animationPresenter));

    private readonly CanonStaticAnimatingObjectRota _staticAnims = staticAnimations ?? throw new ArgumentNullException(nameof(staticAnimations));

    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));

    private readonly IKineticsScriptTimeSource _gameTime = gameTime ?? throw new ArgumentNullException(nameof(gameTime));

    private readonly SimRealmCrossingLedger _passage = transit ?? throw new ArgumentNullException(nameof(transit));
}
