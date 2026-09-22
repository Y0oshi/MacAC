using MacAC.Client.Controls;
using MacAC.Client.Pulse;
using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Stage;

internal interface IOnlineRenderMirrorSink : IRenderMirrorSyncPhase
{
    bool OnActorPrimed(OnlineActorReadyCandidate contender);
    void OnProjVisAltered(OnlineActorRecord capture, bool shown);
    void OnProjPosturePrimed(uint srvOid);
    void OnProjRemoved(OnlineActorRecord capture);
    void OnAssetWithdraw(RealmActor actor);
}

internal sealed class OnlineRenderMirrorDiary(
    OnlineActorCore runtime,
    RenderMirrorDiary journal,
    IRasterizeTraversalOrderingOrigin traversalOrder,
    IAvatarIdentitySource? ownAvatar = null) : IOnlineRenderMirrorSink
{
    private const byte OnlineProjDomain = 3;

    private readonly OnlineActorCore _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    private readonly RenderMirrorDiary _journal = journal ?? throw new ArgumentNullException(nameof(journal));
    private readonly IRasterizeTraversalOrderingOrigin _traversalOrdering = traversalOrder
            ?? throw new ArgumentNullException(nameof(traversalOrder));
    private readonly IAvatarIdentitySource? _ownAvatar = ownAvatar;
    private readonly Dictionary<SimActorKey, TrackedMirror> _byKey = [];
    private readonly List<OnlineActorRecord> _engagedTrunkTemp = [];
    private readonly List<TrackedMirror> _engagedTemp = [];

    public int ProjTally => _byKey.Count;

    public bool OnActorPrimed(OnlineActorReadyCandidate contender)
    {
        if (!contender.IsLatest(_runtime)
            || contender.WorldEntity is not { } actor
            || !contender.Record.ResourcesRegistered)

            return false;

        Upsert(contender.Record, actor, contender.Record.IsSpatiallyVisible);
        return contender.IsLatest(_runtime);
    }

    public void OnProjVisAltered(
        OnlineActorRecord capture,
        bool shown)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (!_runtime.IsLatestCapture(capture)
            || capture.WorldEntity is not { } actor
            || capture.ProjTag is not { } tag
            || !_byKey.ContainsKey(tag))

            return;

        Upsert(capture, actor, shown);
    }

    public void OnProjPosturePrimed(uint srvOid)
    {
        if (!_runtime.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is not { } actor
            || capture.ProjSort is not OnlineActorMirrorKind.Attached)

            return;

        Upsert(capture, actor, capture.IsSpatiallyVisible);
    }

    public void OnProjRemoved(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.ProjTag is { } tag
            && _byKey.TryGetValue(
                tag,
                out TrackedMirror? followed)
            && followed.Projection.ProjectionClass is
                RenderMirrorClass.EquippedChild)
        {
            if (_runtime.IsLatestCapture(followed.Record)
                && ReferenceEquals(
                    followed.Record.WorldEntity,
                    followed.Entity))
            {
                Upsert(followed.Record, followed.Entity, spatiallyShown: false);
            }
            else
            {
                Drop(followed);
            }
        }
    }

    public void OnAssetWithdraw(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        TrackedMirror? followed = null;
        foreach (TrackedMirror contender in _byKey.Values)
        {
            if (ReferenceEquals(contender.Entity, actor))
            {
                followed = contender;
                break;
            }
        }
        if (followed is not null)
            Drop(followed);
    }

    public void SynchronizeEngagedSrcs()
    {
        _runtime.DuplicateSpatialTrunkObjectRecordsTo(_engagedTrunkTemp);
        for (int idx = 0; idx < _engagedTrunkTemp.Count; ++idx)
        {
            var capture = _engagedTrunkTemp[idx];
            if (!capture.ResourcesRegistered
                || capture.WorldEntity is not { } actor
                || !_runtime.IsLatestSpatialTrunkObject(capture))

                continue;

            Upsert(capture, actor, capture.IsSpatiallyVisible);
        }

        _engagedTemp.Clear();
        foreach (TrackedMirror followed in _byKey.Values)
        {
            if (followed.Record.IsSpatiallyProjected
                && followed.Record.ProjSort is
                    OnlineActorMirrorKind.Attached)
                _engagedTemp.Add(followed);
        }

        for (int idx = 0; idx < _engagedTemp.Count; ++idx)
        {
            var followed = _engagedTemp[idx];
            if (!_runtime.IsLatestCapture(followed.Record)
                || !ReferenceEquals(followed.Record.WorldEntity, followed.Entity)
                || followed.Record.ProjTag is not { } tag
                || !_byKey.TryGetValue(
                    tag,
                    out TrackedMirror? latest)
                || !ReferenceEquals(latest, followed))

                continue;

            Upsert(
                followed.Record,
                followed.Entity,
                followed.Record.IsSpatiallyVisible);
        }
    }

    public void RestartTracking()
    {
        _byKey.Clear();
        _engagedTrunkTemp.Clear();
        _engagedTemp.Clear();
    }

    internal bool TryGet(
        uint ownActorIdent,
        out RenderMirrorRecord capture)
    {
        if (_runtime.TryFetchCaptureByOwnActorIdent(
                ownActorIdent,
                out OnlineActorRecord holder)
            && holder.ProjTag is { } tag
            && _byKey.TryGetValue(tag, out TrackedMirror? followed))
        {
            capture = followed.Projection;
            return true;
        }

        capture = default;
        return false;
    }

    internal static RenderMirrorId ProjIdent(uint ownActorIdent)
    {
        return RenderMirrorId.FromRaw(
            ((ulong)OnlineProjDomain << 56) | ownActorIdent);
    }

    private void Upsert(
        OnlineActorRecord capture,
        RealmActor actor,
        bool spatiallyShown)
    {
        var projected = Project(
            capture,
            actor,
            spatiallyShown);
        SimActorKey tag = DemandProjTag(capture);
        if (!_byKey.TryGetValue(tag, out TrackedMirror? followed))
        {
            _journal.Register(in projected);
            _byKey.Add(
                tag,
                new TrackedMirror(capture, actor, projected));
            return;
        }

        if (!ReferenceEquals(followed.Record, capture)
            || !ReferenceEquals(followed.Entity, actor)
            || followed.Projection.OwnerIncarnation
                != projected.OwnerIncarnation
            || followed.Projection.ProjectionClass
                != projected.ProjectionClass)
        {
            _journal.Register(in projected);
            _byKey[tag] =
                new TrackedMirror(capture, actor, projected);
            return;
        }

        projected = projected with
        {
            PreviousTransform = new EarlierRasterizeTransform(
                followed.Projection.Transform.LocalToWorld),
        };
        _journal.AffixDifference(followed.Projection, projected);
        followed.Projection = projected;
    }

    private RenderMirrorRecord Project(
        OnlineActorRecord capture,
        RealmActor actor,
        bool spatiallyShown)
    {
        uint wholeChamberIdent = capture.WholeChamberIdent is not 0
            ? capture.WholeChamberIdent
            : actor.ParentCellId ?? 0;
        uint holderLbIdent = capture.CanonLbIdent is not 0
            ? capture.CanonLbIdent
            : wholeChamberIdent is 0
                ? 0
                : Canonicalize(wholeChamberIdent);
        var projected =
            RenderMirrorRecordMint.ProjectActor(
            ProjIdent(actor.Id),
            capture.ProjSort is OnlineActorMirrorKind.Attached
                ? RenderMirrorClass.EquippedChild
                : RenderMirrorClass.LiveDynamicRoot,
            RasterizeHolderIncarnation.FromRaw(
                ((ulong)capture.Generation << 32) | actor.Id),
            holderLbIdent,
            wholeChamberIdent,
            actor,
            spatiallyShown,
            capture.ProjSort is OnlineActorMirrorKind.Attached
                ? RasterizeInvokerPersonaFlavor.EquippedChild
                : RasterizeInvokerPersonaClassifier.Classify(
                    capture.Snapshot.Guid,
                    capture.Snapshot.ItemType,
                    capture.Snapshot.ObjectDescriptionFlags,
                    _ownAvatar?.SrvOid ?? 0u));
        if (_traversalOrdering.TryFetchTraversalOrderTag(
                actor,
                out RasterizeOrderTag orderTag))

            return projected with { SortKey = orderTag };

        SimActorKey tag = DemandProjTag(capture);
        return _byKey.TryGetValue(
            tag,
            out TrackedMirror? kept)
            ? projected with { SortKey = kept.Projection.SortKey }
            : projected;
    }

    private void Drop(TrackedMirror followed)
    {
        SimActorKey tag = DemandProjTag(followed.Record);
        if (!_byKey.Remove(tag))
            return;
        _journal.Unregister(
            followed.Projection.Id,
            followed.Projection.OwnerIncarnation);
    }

    private static uint Canonicalize(uint chamberOrLbIdent) =>
        (chamberOrLbIdent & 0xFFFF0000u) | 0xFFFFu;

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} " +
            "has no exact projection key");
    }

    private sealed class TrackedMirror(
        OnlineActorRecord capture,
        RealmActor actor,
        RenderMirrorRecord proj)
    {
        public OnlineActorRecord Record { get; } = capture;
        public RealmActor Entity { get; } = actor;
        public RenderMirrorRecord Projection { get; set; } = proj;
    }
}

internal static class RasterizeInvokerPersonaClassifier
{
    private const uint AvatarBlurbBit = 0x8u;
    private const uint AvatarOidStem = 0x50000000u;

    internal static RasterizeInvokerPersonaFlavor Classify(
        uint srvOid,
        uint? gearKind,
        uint? objectBlurbFlagSet,
        uint ownAvatarOid)
    {
        if (ownAvatarOid is not 0 && srvOid == ownAvatarOid)
            return RasterizeInvokerPersonaFlavor.LocalPlayer;
        if ((objectBlurbFlagSet.GetValueOrDefault()
                & AvatarBlurbBit) is not 0
            || (srvOid & 0xFF000000u) == AvatarOidStem)

            return RasterizeInvokerPersonaFlavor.RemotePlayer;
        return (gearKind.GetValueOrDefault() & (uint)GearKind.Creature) is not 0
            ? RasterizeInvokerPersonaFlavor.NonPlayerCreature
            : RasterizeInvokerPersonaFlavor.OtherLiveDynamic;
    }
}

internal sealed class OnlineRenderMirrorResourceLifespan(
    IOnlineRenderMirrorSink sink) : IOnlineActorResourceLifespan
{
    private readonly IOnlineRenderMirrorSink _drain =
        sink ?? throw new ArgumentNullException(nameof(sink));

    public void Register(RealmActor actor)
    {
    }

    public void Unregister(RealmActor actor) =>
        _drain.OnAssetWithdraw(actor);
}
