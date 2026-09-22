using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Effects;
using MacAC.Client.Graphics.Stage;
using MacAC.Client.Paging;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal sealed class OnlineActorRelationshipMirror(
    EquippedChildRenderDriver children) : IOnlineActorRelationshipMirror
{
    private readonly EquippedChildRenderDriver _descendants =
        children ?? throw new ArgumentNullException(nameof(children));

    public void OnSpawn(RealmSession.MoverSpawn summon) => _descendants.OnSummon(summon);
    public void OnAncestor(AncestorSignal.Parsed refresh) => _descendants.OnAncestorSignal(refresh);
    public void OnBuildParentAccepted(CreateAnchorUpdate refresh) =>
        _descendants.OnBuildAncestorApproved(refresh);
    public DescendantUnparentDisposition OnChildBecameUnparented(uint descendantOid) =>
        _descendants.OnDescendantBecameUnparented(descendantOid);
    public bool TryEnactAffixedLooks(
        OnlineActorRecord capture,
        ulong objRefDscArbiterVer) =>
        _descendants.TryEnactAffixedLooks(capture, objRefDscArbiterVer);
}

internal sealed class DeferredOnlineActorParentAcceptance
{
    private Func<AncestorSignal.Parsed, bool>? _admit;

    public void Bind(Func<AncestorSignal.Parsed, bool> admit)
    {
        ArgumentNullException.ThrowIfNull(admit);
        if (Interlocked.CompareExchange(ref _admit, admit, null) is not null)
            throw new InvalidOperationException("Live parent acceptance is by now bound");
    }

    public IDisposable BindOwned(Func<AncestorSignal.Parsed, bool> admit)
    {
        Bind(admit);
        return new Binding(this, admit);
    }

    public bool TryAdmit(AncestorSignal.Parsed refresh)
    {
        return (_admit ?? throw new InvalidOperationException(
            "Live parent acceptance has to be bound prior to a session starts"))(refresh);
    }

    private void Loosen(Func<AncestorSignal.Parsed, bool> anticipated) => _ = Interlocked.CompareExchange(ref _admit, null, anticipated);

    private sealed class Binding(
        DeferredOnlineActorParentAcceptance holder,
        Func<AncestorSignal.Parsed, bool> anticipated) : IDisposable
    {
        private DeferredOnlineActorParentAcceptance? _holder = holder;
        private readonly Func<AncestorSignal.Parsed, bool> _anticipated = anticipated;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Loosen(_anticipated);
    }
}

internal sealed class OnlineActorReadyHerald : IOnlineActorReadyHerald
{
    private readonly OnlineActorCore _runtime;
    private readonly Func<OnlineActorRecord, bool> _readyFxList;
    private readonly Func<OnlineActorRecord, bool> _presentPhase;
    private readonly Func<OnlineActorRecord, bool> _rerunFxList;
    private readonly IOnlineRenderMirrorSink? _rasterizeProj;

    public OnlineActorReadyHerald(
        OnlineActorCore core,
        ActorEffectDriver fxList,
        OnlineActorDisplayDriver exhibit,
        IOnlineRenderMirrorSink? rasterizeProj = null)
        : this(
            core,
            capture => fxList.ReadyOnlineActorHolder(capture.ServerOid),
            capture => exhibit.OnLiveEntityPrimed(capture.ServerOid),
            capture => fxList.RerunQueuedForOnlineActor(capture.ServerOid),
            rasterizeProj)
    {
        ArgumentNullException.ThrowIfNull(fxList);
        ArgumentNullException.ThrowIfNull(exhibit);
    }

    internal OnlineActorReadyHerald(
        OnlineActorCore runtime,
        Func<OnlineActorRecord, bool> prepareEffects,
        Func<OnlineActorRecord, bool> presentState,
        Func<OnlineActorRecord, bool> replayEffects,
        IOnlineRenderMirrorSink? rasterizeProj = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _readyFxList = prepareEffects
            ?? throw new ArgumentNullException(nameof(prepareEffects));
        _presentPhase = presentState
            ?? throw new ArgumentNullException(nameof(presentState));
        _rerunFxList = replayEffects
            ?? throw new ArgumentNullException(nameof(replayEffects));
        _rasterizeProj = rasterizeProj;
    }

    public bool Publish(OnlineActorReadyCandidate contender)
    {
        var anticipatedCapture = contender.Record;
        ArgumentNullException.ThrowIfNull(anticipatedCapture);
        if (!contender.IsLatest(_runtime)
            || !_readyFxList(anticipatedCapture)
            || !contender.IsLatest(_runtime))
            return false;

        if (!_presentPhase(anticipatedCapture)
            || !contender.IsLatest(_runtime))

            return false;

        return !_rerunFxList(anticipatedCapture)
            || !contender.IsLatest(_runtime)
            ? false
            : (_rasterizeProj?.OnActorPrimed(contender) ?? true)
            && contender.IsLatest(_runtime);
    }
}

internal sealed class OnlineActorRealmOriginMarshal(
    OnlineRealmOriginLedger origin,
    PagingDriver streaming,
    GpuRealmPhase worldState,
    RealmRevealMarshal worldReveal,
    IAvatarIdentitySource identity,
    ISealedDungeonChamberClassifier sealedDungeonCells,
    Action<string>? probe = null) : IOnlineActorRealmOriginMarshal
{
    private readonly OnlineRealmOriginLedger _origin = origin ?? throw new ArgumentNullException(nameof(origin));
    private readonly PagingDriver _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly GpuRealmPhase _realmPhase = worldState ?? throw new ArgumentNullException(nameof(worldState));
    private readonly RealmRevealMarshal _worldReveal = worldReveal ?? throw new ArgumentNullException(nameof(worldReveal));
    private sealed class DelegatePersonaOrigin(Func<uint> read) : IAvatarIdentitySource
    {
        private readonly Func<uint> _scan = read ?? throw new ArgumentNullException(nameof(read));

        public uint SrvOid => _scan();
    }

    private readonly IAvatarIdentitySource _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    private readonly ISealedDungeonChamberClassifier _sealedDungeonChambers = sealedDungeonCells
            ?? throw new ArgumentNullException(nameof(sealedDungeonCells));
    private readonly Action<string>? _diagnostic = probe;

    internal OnlineActorRealmOriginMarshal(
        OnlineRealmOriginLedger origin,
        PagingDriver paging,
        GpuRealmPhase realmPhase,
        RealmRevealMarshal realmUnveil,
        Func<uint> avatarOid,
        ISealedDungeonChamberClassifier sealedDungeonChambers,
        Action<string>? probe = null)
        : this(
            origin,
            paging,
            realmPhase,
            realmUnveil,
            new DelegatePersonaOrigin(avatarOid),
            sealedDungeonChambers,
            probe)
    {
    }

    public bool IsKnown => _origin.IsKnown;

    public OnlineActorOriginInitialization TryBootstrap(
        RealmSession.MoverSpawn summon)
    {
        if (summon.Guid != _identity.SrvOid
            || summon.Position is not { } locus)

            return new(_origin.IsKnown, Array.Empty<uint>());

        int lbX = (int)((locus.LandblockId >> 24) & 0xFFu);
        int lbY = (int)((locus.LandblockId >> 16) & 0xFFu);
        int formerMiddleX = _origin.CenterX;
        int formerMiddleY = _origin.CenterY;
        if (!_origin.TryInitialize(lbX, lbY))
            return new(true, Array.Empty<uint>());

        if (lbX != formerMiddleX || lbY != formerMiddleY)
        {
            _diagnostic?.Invoke(
                $"live: first player position — recentering streaming from ({formerMiddleX},{formerMiddleY}) "
                + $"to ({lbX},{lbY}) @0x{locus.LandblockId:X8}");
        }

        _paging.BootstrapRecognizedSigninMiddle(
            lbX,
            lbY,
            isSealedDungeon: _sealedDungeonChambers.IsSealedDungeon(
                locus.LandblockId));
        _worldReveal.CommenceSignin(locus.LandblockId);

        return new(
            true,
            _realmPhase.FetchedLbIdents.ToArray());
    }
}
