using MacAC.Client.Shell;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Targeting;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Realm;

internal sealed class StashRealmDropMirrorDriver : IDisposable
{
    private readonly GearDealingDriver _dealing;
    private readonly ClientThingChart _objects;
    private readonly OnlineActorCore _runtime;
    private readonly OnlineActorFillingDriver _hydration;
    private readonly PickPhase _pick;
    private readonly PendingSplitToRealmMirror _queued;
    private readonly Func<double> _instant;
    private bool _destroyed;

    public StashRealmDropMirrorDriver(
        GearDealingDriver interaction,
        ClientThingChart objects,
        OnlineActorCore runtime,
        OnlineActorFillingDriver hydration,
        PickPhase selection,
        Func<double> now)
    {
        _dealing = interaction
            ?? throw new ArgumentNullException(nameof(interaction));
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _hydration = hydration
            ?? throw new ArgumentNullException(nameof(hydration));
        _pick = selection ?? throw new ArgumentNullException(nameof(selection));
        _instant = now ?? throw new ArgumentNullException(nameof(now));
        _queued = new PendingSplitToRealmMirror();

        _dealing.WorldDropDispatched += OnRealmDiscardDispatched;
        _objects.MoveRequestFailed += OnRelocateReqFailed;
        _objects.Cleared += OnObjectsCleared;
    }

    public bool TryRecoverUnknownLocus(
        RealmSession.MoverPositionUpdate refresh)
    {
        bool recognized = _runtime.TryGetCapture(refresh.Guid, out _);
        if (recognized
            || !_queued.TryLocate(refresh, _instant(), out RealmSession.MoverSpawn summon))

            return false;

        _hydration.OnCreate(summon);
        bool recovered = _runtime.TryGetCapture(refresh.Guid, out _);
        if (recovered)

            _pick.Select(refresh.Guid, PickChangeSource.System);
        return recovered;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _objects.Cleared -= OnObjectsCleared;
        _objects.MoveRequestFailed -= OnRelocateReqFailed;
        _dealing.WorldDropDispatched -= OnRealmDiscardDispatched;
        _queued.Clear();
    }

    private void OnRealmDiscardDispatched(RealmDropDispatch relay)
    {
        bool hasSrc = _runtime.TryGetCapture(
            relay.Request.ItemId,
            out RealmSession.MoverSpawn src);
        if (relay.Request.Kind is not PackRequestKind.SplitToWorld
            || relay.Amount is 0
            || !hasSrc)

            return;

        _queued.Record(
            relay.Request.Token,
            src,
            relay.Amount,
            _instant());
    }

    private void OnRelocateReqFailed(RelocationRefusal miss) =>
        _queued.WipeSrc(miss.ItemId);

    private void OnObjectsCleared() => _queued.Clear();
}

internal sealed class PendingSplitToRealmMirror
{
    internal const double CanonRecognitionSecs = 10.0;

    private QueuedDivide? _val;

    internal bool HasQueued => _val is not null;

    internal void Record(
        ulong reqTicket,
        RealmSession.MoverSpawn src,
        uint quantity,
        double instant)
    {
        if (reqTicket is 0
            || src.Guid is 0
            || quantity is 0
            || !double.IsFinite(instant))
        {
            _val = null;
            return;
        }

        _val = new QueuedDivide(reqTicket, src, quantity, instant);
    }

    internal bool TryLocate(
        RealmSession.MoverPositionUpdate refresh,
        double instant,
        out RealmSession.MoverSpawn summon)
    {
        summon = default;
        if (_val is not { } queued)
            return false;

        double age = instant - queued.RequestTime;
        if (!double.IsFinite(age)
            || age < 0
            || age >= CanonRecognitionSecs)
        {
            _val = null;
            return false;
        }

        if (refresh.Guid is 0 || refresh.Guid == queued.Source.Guid)
            return false;

        _val = null;
        summon = AssembleSummon(queued.Source, queued.Amount, refresh);
        return true;
    }

    internal void WipeSrc(uint srcOid)
    {
        if (_val is { } queued && queued.Source.Guid == srcOid)
            _val = null;
    }

    internal void Clear() => _val = null;

    private static RealmSession.MoverSpawn AssembleSummon(
        RealmSession.MoverSpawn src,
        uint quantity,
        RealmSession.MoverPositionUpdate refresh)
    {
        KineticSpawnData? kinetics = src.Physics is { } srcKinetics
            ? srcKinetics with
            {
                Position = refresh.Position,
                Parent = null,
                Velocity = refresh.Velocity,
                Timestamps = srcKinetics.Timestamps with
                {
                    Position = refresh.PositionSequence,
                    Teleport = refresh.TeleportSequence,
                    ForcePosition = refresh.ForcePositionSequence,
                    Instance = refresh.InstanceSequence,
                    Movement = 0,
                    ServerControlledMove = 0,
                },
            }
            : null;

        return src with
        {
            Guid = refresh.Guid,
            Position = refresh.Position,
            StackSize = checked((int)quantity),
            ContainerId = 0u,
            WielderId = 0u,
            CurrentWieldedLocation = 0u,
            InstanceSequence = refresh.InstanceSequence,
            MovementSequence = 0,
            ServerControlSequence = 0,
            PositionSequence = refresh.PositionSequence,
            ParentGuid = null,
            ParentLocation = null,
            PlacementId = refresh.PlacementId,
            Physics = kinetics,
        };
    }

    private readonly record struct QueuedDivide(
        ulong RequestToken,
        RealmSession.MoverSpawn Source,
        uint Amount,
        double RequestTime);
}
