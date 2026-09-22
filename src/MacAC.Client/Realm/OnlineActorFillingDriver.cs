using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal enum OnlineMirrorPurpose
{
    LogicalRegistration,
    SpatialRecovery,
    CreateSupersessionRecovery,
    AppearanceMutation,
}

internal interface IOnlineActorMirrorAssembler
{
    bool TryMaterialize(
        SimActorRecord anticipatedCanon,
        RealmSession.MoverSpawn canonSummon,
        OnlineMirrorPurpose purpose,
        ulong anticipatedBuildIntegrationVer,
        OnlineActorAppearancePulseLedger? looksRefresh = null);

    void RestartSessPhase();
}

internal interface IOnlineActorRelationshipMirror
{
    void OnSpawn(RealmSession.MoverSpawn summon);
    void OnAncestor(AncestorSignal.Parsed refresh);
    void OnBuildParentAccepted(CreateAnchorUpdate refresh);
    DescendantUnparentDisposition OnChildBecameUnparented(uint descendantOid);
    bool TryEnactAffixedLooks(
        OnlineActorRecord capture,
        ulong objRefDscArbiterVer);
}

internal interface IOnlineActorReadyHerald
{
    bool Publish(OnlineActorReadyCandidate contender);
}

internal readonly record struct OnlineActorReadyCandidate(
    OnlineActorRecord Record,
    ulong CreateIntegrationVersion,
    ulong PositionAuthorityVersion,
    ulong ProjectionMutationVersion,
    ulong ObjDescAuthorityVersion,
    OnlineActorMirrorKind ProjectionKind,
    bool IsSpatiallyProjected,
    RealmActor? WorldEntity)
{
    internal static OnlineActorReadyCandidate Capture(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new(
            capture,
            capture.BuildIntegrationVer,
            capture.PositionAuthorityVersion,
            capture.ProjAlterationVer,
            capture.ObjRefDscArbiterVer,
            capture.ProjSort,
            capture.IsSpatiallyProjected,
            capture.WorldEntity);
    }

    internal bool IsLatest(OnlineActorCore core)
    {
        return core.IsLatestBuildIntegration(Record, CreateIntegrationVersion)
        && Record.PositionAuthorityVersion == PositionAuthorityVersion
        && Record.ProjAlterationVer == ProjectionMutationVersion
        && Record.ObjRefDscArbiterVer == ObjDescAuthorityVersion
        && Record.ProjSort == ProjectionKind
        && Record.IsSpatiallyProjected == IsSpatiallyProjected
        && ReferenceEquals(Record.WorldEntity, WorldEntity);
    }
}

internal interface IOnlineActorWirePulseSink
{
    void ImposeSameGen(SameEpochCreateObjectEvents signals);
}

internal sealed class DeferredOnlineActorWirePulseSink : IOnlineActorWirePulseSink
{
    private IOnlineActorWirePulseSink? _interior;

    public void Bind(IOnlineActorWirePulseSink interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        if (Interlocked.CompareExchange(ref _interior, interior, null) is not null)
            throw new InvalidOperationException("The live-entity network sink is by now bound");
    }

    public IDisposable BindOwned(IOnlineActorWirePulseSink interior)
    {
        Bind(interior);
        return new Binding(this, interior);
    }

    public void ImposeSameGen(SameEpochCreateObjectEvents signals)
    {
        (_interior ?? throw new InvalidOperationException(
            "The live-entity network sink has to be bound prior to a session starts"))
        .ImposeSameGen(signals);
    }

    private void Loosen(IOnlineActorWirePulseSink anticipated) => _ = Interlocked.CompareExchange(ref _interior, null, anticipated);

    private sealed class Binding(
        DeferredOnlineActorWirePulseSink holder,
        IOnlineActorWirePulseSink anticipated) : IDisposable
    {
        private DeferredOnlineActorWirePulseSink? _holder = holder;
        private readonly IOnlineActorWirePulseSink _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

internal readonly record struct OnlineActorOriginInitialization(
    bool IsKnown,
    IReadOnlyList<uint> AlreadyLoadedLandblocks);

internal interface IOnlineActorRealmOriginMarshal
{
    bool IsKnown { get; }
    OnlineActorOriginInitialization TryBootstrap(RealmSession.MoverSpawn summon);
}

internal sealed partial class OnlineActorFillingDriver(
    OnlineActorCore runtime,
    SimActorObjectLifetime entityObjects,
    object datLock,
    IOnlineActorMirrorAssembler materializer,
    IOnlineActorRelationshipMirror relationships,
    IOnlineActorReadyHerald ready,
    IOnlineActorRealmOriginMarshal origin,
    IOnlineActorWirePulseSink networkUpdates,
    IAcceptedLocalKineticsTimestampHerald timestamps,
    IAvatarIdentitySource identity,
    OnlineActorDeletionDriver deletion,
    SimDebutPilot? leadListing = null,
    SimGrantedPositionPilot? approvedLocusSteer = null) : IOnlineActorLandblockLoadedSink
{
    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    private readonly SimActorObjectLifetime _entityObjects = entityObjects
            ?? throw new ArgumentNullException(nameof(entityObjects));

    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));

    private readonly IOnlineActorMirrorAssembler _materializer = materializer ?? throw new ArgumentNullException(nameof(materializer));

    private readonly IOnlineActorRelationshipMirror _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));

    private readonly IOnlineActorReadyHerald _primed = ready ?? throw new ArgumentNullException(nameof(ready));

    private readonly IOnlineActorRealmOriginMarshal _origin = origin ?? throw new ArgumentNullException(nameof(origin));

    private readonly IOnlineActorWirePulseSink _networkUpdates = networkUpdates ?? throw new ArgumentNullException(nameof(networkUpdates));

    private readonly IAcceptedLocalKineticsTimestampHerald _timestamps = timestamps ?? throw new ArgumentNullException(nameof(timestamps));

    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));

    private readonly OnlineActorDeletionDriver _deletion = deletion ?? throw new ArgumentNullException(nameof(deletion));

    private readonly SimDebutPilot? _leadListing = leadListing;

    private readonly SimGrantedPositionPilot? _approvedLocusSteer = approvedLocusSteer;

    private readonly Dictionary<SimActorRecord, CanonicalMirrorOperation>
        _projectionOperations =
            new(ReferenceEqualityComparer.Instance);

    private sealed class CanonicalMirrorOperation
    {
        public ulong BuildIntegrationVer { get; set; }
        public bool ReattemptAsked { get; set; }
    }

    internal event Action<uint>? AppearanceApplied;
}
