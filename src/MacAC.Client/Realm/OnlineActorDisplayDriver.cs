using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Client.Realm;

public sealed class OnlineActorDisplayDriver : IDisposable
{
    public const uint UnConcealProgramKind = 0x75u;
    public const uint ConcealedProgramKind = 0x76u;

    private readonly OnlineActorCore _onlineActors;
    private readonly ProxyRegistry _shades;
    private readonly Func<uint, uint, float, bool> _playTyped;
    private readonly Action<uint, bool> _setStraightDescendantsNoPaint;
    private readonly Action<uint> _wipeInvalidMark;
    private readonly Func<(int X, int Y)> _onlineMiddle;
    private readonly Action<uint>? _onShadeRestored;
    private readonly OnlineActorPartArrayEnterRealmPort _pieceArrJoinRealm;
    private readonly HashSet<SimActorKey> _readyOwners = [];
    private readonly HashSet<SimActorKey> _suspendedShadowOwners = [];
    private readonly HashSet<OnlineActorRecord> _drainingRecords = [];
    private bool _destroyed;

    public OnlineActorDisplayDriver(
        OnlineActorCore liveEntities,
        ProxyRegistry shadows,
        Func<uint, uint, float, bool> playTyped,
        OnlineActorPartArrayEnterRealmPort partArrayEnterWorld,
        Action<uint, bool>? setStraightDescendantsNoPaint = null,
        Action<uint>? wipeInvalidMark = null,
        Func<(int X, int Y)>? onlineMiddle = null,
        Action<uint>? onShadeRestored = null)
    {
        _onlineActors = liveEntities ?? throw new ArgumentNullException(nameof(liveEntities));
        _shades = shadows ?? throw new ArgumentNullException(nameof(shadows));
        _playTyped = playTyped ?? throw new ArgumentNullException(nameof(playTyped));
        _pieceArrJoinRealm = partArrayEnterWorld
            ?? throw new ArgumentNullException(nameof(partArrayEnterWorld));
        _setStraightDescendantsNoPaint = setStraightDescendantsNoPaint ?? ((_, _) => { });
        _wipeInvalidMark = wipeInvalidMark ?? (_ => { });
        _onlineMiddle = onlineMiddle ?? (() => (0, 0));
        _onShadeRestored = onShadeRestored;
        _onlineActors.ProjectionVisibilityChanged += OnProjVisAltered;
    }

    public bool OnLiveEntityPrimed(uint srvOid)
    {
        if (!_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || capture.WorldEntity is null
            || !capture.ResourcesRegistered)

            return false;

        SimActorKey tag = DemandProjTag(capture);
        _readyOwners.Add(tag);
        if (!ImposeQueuedChangeovers(capture)
            || capture.WorldEntity is not { } actor
            || !IsLatest(capture, actor))

            return false;

        SuspendPlainShadeBeyondProj(capture, actor);
        return IsLatest(capture, actor);
    }

    public bool OnPhaseApproved(uint srvOid)
    {
        return !_onlineActors.TryFetchRecord(srvOid, out OnlineActorRecord capture)
            || !TryFetchProjTag(capture, out SimActorKey tag)
            || !_readyOwners.Contains(tag)
            ? false
            : ImposeQueuedChangeovers(capture);
    }

    public void Forget(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (TryFetchProjTag(capture, out SimActorKey tag))
        {
            _readyOwners.Remove(tag);
            _suspendedShadowOwners.Remove(tag);
        }
        _drainingRecords.Remove(capture);
    }

    internal int PrimedHolderTally => _readyOwners.Count;
    internal int PostponedShadeRevertTally => _suspendedShadowOwners.Count;

    public void Clear()
    {
        _readyOwners.Clear();
        _suspendedShadowOwners.Clear();
        _drainingRecords.Clear();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _onlineActors.ProjectionVisibilityChanged -= OnProjVisAltered;
        Clear();
    }

    internal bool HasPostponedShadeRevert(uint srvOid)
    {
        return TryFetchLatestProjTag(srvOid, out SimActorKey tag)
        && _suspendedShadowOwners.Contains(tag);
    }

    private bool ImposeQueuedChangeovers(OnlineActorRecord capture)
    {
        if (!_drainingRecords.Add(capture))
            return IsLatest(capture, capture.WorldEntity);

        try
        {
            while (capture.TryDequeuePhaseChangeover(out CanonKineticShift changeover))
            {
                if (capture.WorldEntity is not { } actor
                    || !IsLatest(capture, actor))

                    return false;

                _shades.RefreshKineticsPhase(actor.Id, (uint)changeover.FinalState);

                switch (changeover.HiddenTransition)
                {
                    case CanonHiddenShift.BecameHidden:
                        _playTyped(actor.Id, ConcealedProgramKind, 1f);
                        if (!IsLatest(capture, actor))
                            return false;
                        _setStraightDescendantsNoPaint(capture.ServerOid, true);
                        if (!IsLatest(capture, actor))
                            return false;
                        _shades.Suspend(actor.Id);
                        _suspendedShadowOwners.Add(DemandProjTag(capture));
                        _pieceArrJoinRealm.HandleEnterWorld(actor.Id);
                        if (!IsLatest(capture, actor))
                            return false;
                        _wipeInvalidMark(capture.ServerOid);
                        if (!IsLatest(capture, actor))
                            return false;
                        break;

                    case CanonHiddenShift.BecameVisible:
                        _playTyped(actor.Id, UnConcealProgramKind, 1f);
                        if (!IsLatest(capture, actor))
                            return false;
                        _setStraightDescendantsNoPaint(capture.ServerOid, false);
                        if (!IsLatest(capture, actor))
                            return false;
                        _pieceArrJoinRealm.HandleEnterWorld(actor.Id);
                        if (!IsLatest(capture, actor))
                            return false;
                        bool restored = ReinstateShade(capture, actor);
                        if (!IsLatest(capture, actor))
                            return false;
                        if (restored)
                            _suspendedShadowOwners.Remove(DemandProjTag(capture));
                        break;
                }
            }

            return IsLatest(capture, capture.WorldEntity);
        }
        finally
        {
            _drainingRecords.Remove(capture);
        }
    }

    private bool ReinstateShade(OnlineActorRecord capture, MacAC.Mechanics.Realm.RealmActor actor)
    {
        if (!capture.IsSpatiallyProjected
            || !capture.IsSpatiallyVisible
            || capture.WholeChamberIdent is 0
            || _onlineActors.AncestorAffixes.HasSealedAncestor(capture.ServerOid))

            return false;

        (int middleX, int middleY) = _onlineMiddle();
        ProxyPositionSynchronizer.Sync(
            _shades,
            actor.Id,
            actor.Position,
            actor.Rotation,
            capture.WholeChamberIdent,
            middleX,
            middleY);
        _onShadeRestored?.Invoke(capture.ServerOid);
        return true;
    }

    private void OnProjVisAltered(OnlineActorRecord capture, bool shown)
    {
        if (capture.WorldEntity is not { } actor
            || !IsLatest(capture, actor))

            return;

        if (!shown)
        {
            SuspendPlainShadeBeyondProj(capture, actor);
            return;
        }

        if ((capture.FinalKineticsPhase & KineticStateFlags.Hidden) != 0
            || !TryFetchProjTag(capture, out SimActorKey tag)
            || !_readyOwners.Contains(tag)
            || !_suspendedShadowOwners.Contains(tag))

            return;

        bool restored = ReinstateShade(capture, actor);
        if (!IsLatest(capture, actor))
            return;
        if (restored)
            _suspendedShadowOwners.Remove(tag);
    }

    private void SuspendPlainShadeBeyondProj(
        OnlineActorRecord capture,
        MacAC.Mechanics.Realm.RealmActor actor)
    {
        if (capture.IsSpatiallyVisible
            || capture.ProjectileRuntime is not null
            || !TryFetchProjTag(capture, out SimActorKey tag)
            || !_readyOwners.Contains(tag)
            || !_shades.Suspend(actor.Id))

            return;

        _suspendedShadowOwners.Add(tag);
    }

    private bool IsLatest(
        OnlineActorRecord capture,
        MacAC.Mechanics.Realm.RealmActor? actor)
    {
        return actor is not null
        && _onlineActors.TryFetchRecord(capture.ServerOid, out OnlineActorRecord latest)
        && ReferenceEquals(latest, capture)
        && ReferenceEquals(latest.WorldEntity, actor);
    }

    private bool TryFetchLatestProjTag(
        uint srvOid,
        out SimActorKey tag)
    {
        if (_onlineActors.TryFetchRecord(
                srvOid,
                out OnlineActorRecord capture))

            return TryFetchProjTag(capture, out tag);

        tag = default;
        return false;
    }

    private static bool TryFetchProjTag(
        OnlineActorRecord capture,
        out SimActorKey tag)
    {
        if (capture.ProjTag is { } projTag)
        {
            tag = projTag;
            return true;
        }

        tag = default;
        return false;
    }

    private static SimActorKey DemandProjTag(
        OnlineActorRecord capture)
    {
        return capture.ProjTag
        ?? throw new InvalidOperationException(
            $"Live entity 0x{capture.ServerOid:X8}/{capture.Generation} " +
            "has no exact projection key");
    }
}
