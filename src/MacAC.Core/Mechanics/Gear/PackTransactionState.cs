namespace MacAC.Mechanics.Gear;

public enum PackRequestKind
{
    Pickup,
    PutInContainer,
    SplitToContainer,
    Merge,
    Move,
    DropToWorld,
    SplitToWorld,
    Wield,
    Give,
}

public readonly record struct PackRequestInFlight(
    ulong Token,
    PackRequestKind Kind,
    uint ItemId,
    ClientThing? ItemIdentity,
    bool Dispatched);

/// <summary>A busy-count hold for an item use that is being sent; released if the send never happens.</summary>
public sealed class ItemUseHold
{
    private readonly Action<bool> _settle;
    private int _settled;

    internal ItemUseHold(Action<bool> resolve) => _settle = resolve ?? throw new ArgumentNullException(nameof(resolve));

    public void FlagDispatched() => Settle(dispatched: true);

    public void AbortPriorRelay() => Settle(dispatched: false);

    private void Settle(bool dispatched)
    {
        if (Interlocked.Exchange(ref _settled, 1) is 0)
            _settle(dispatched);
    }
}

public sealed class PackTransactionState : IDisposable
{
    private readonly ClientThingChart _objects;
    private ulong _ticketSeed;
    private ulong _gripGen;
    private PackRequestInFlight? _queued;
    private int _occupied;
    private long _watcherMisses;
    private bool _destroyed;

    public PackTransactionState(ClientThingChart objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _objects.ObjectMoved += OnMoved;
        _objects.MoveRequestFailed += OnRelocateRefused;
        _objects.ObjectRemoved += OnRemoved;
        _objects.StackSizeUpdated += OnRestacked;
        _objects.Cleared += OnChartCleared;
    }

    public event Action? StateChanged;

    public event Action<PackRequestInFlight>? RequestCompleted;

    public event Action<PackRequestInFlight, uint>? RequestFailed;

    public event Action? ObjectTableCleared;

    public ClientThingChart Objects => _objects;

    public int OccupiedCount => _occupied;

    public bool HasQueuedReq => _queued is not null;

    public bool CanCommenceReq => _occupied is 0 && _queued is null;

    public bool IsDisposed => _destroyed;

    public long RelayFailureCount => Interlocked.Read(ref _watcherMisses);

    public Exception? PreviousDispatchFailure { get; private set; }

    public bool TryReserve(PackRequestKind sort, uint gearIdent, out PackRequestInFlight queued, Action<PackRequestInFlight>? priorPhaseAltered = null)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (gearIdent is 0u || !CanCommenceReq)
        {
            queued = default;
            return false;
        }

        queued = new PackRequestInFlight(UpcomingTicket(), sort, gearIdent, _objects.Get(gearIdent), Dispatched: false);
        _queued = queued;

        if (priorPhaseAltered is not null)
        {
            try
            {
                priorPhaseAltered(queued);
            }
            catch
            {
                if (IsStillQueued(queued))
                    _queued = null;
                throw;
            }
        }

        if (!IsStillQueued(queued))
            return false;
        Proclaim(StateChanged);
        return IsStillQueued(queued);
    }

    public bool TryRelay(PackRequestKind sort, uint gearIdent, Func<bool> relay, ulong reservationTicket = 0u)
    {
        ArgumentNullException.ThrowIfNull(relay);
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (gearIdent is 0u)
            return false;

        PackRequestInFlight reserved;
        if (reservationTicket is not 0u)
        {
            if (_queued is not { } pinned || pinned.Token != reservationTicket || pinned.ItemId != gearIdent || pinned.Kind != sort || pinned.Dispatched)
                return false;
            reserved = pinned;
        }
        else if (!TryReserve(sort, gearIdent, out reserved))
        {
            return false;
        }

        bool sent;
        try
        {
            sent = relay();
        }
        catch
        {
            Discard(reserved.Token);
            throw;
        }
        if (!sent)
        {
            Discard(reserved.Token);
            return false;
        }

        if (_queued is { } latest && latest.Token == reserved.Token)
        {
            _queued = reserved with { Dispatched = true };
            Proclaim(StateChanged);
        }
        return true;
    }

    public bool TryFetchQueued(out PackRequestInFlight queued)
    {
        queued = _queued ?? default;
        return _queued is not null;
    }

    public bool RevokePriorRelay(ulong ticket)
    {
        if (_queued is not { } queued || queued.Token != ticket || queued.Dispatched)
            return false;
        _queued = null;
        Proclaim(StateChanged);
        return true;
    }

    public void IncrementOccupiedCount()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ++_occupied;
        Proclaim(StateChanged);
    }

    public ItemUseHold CommenceUseReqReservation()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ulong gen = _gripGen;
        ++_occupied;
        Proclaim(StateChanged);
        return new ItemUseHold(dispatched =>
        {
            // A hold that was sent stays busy until UseDone; one from a cleared session is moot.
            if (gen != _gripGen || dispatched)
                return;
            if (_occupied > 0)
                --_occupied;
            Proclaim(StateChanged);
        });
    }

    public void FinishUse(uint _)
    {
        if (_occupied is 0)
            return;
        --_occupied;
        Proclaim(StateChanged);
    }

    public void PurgeOccupied()
    {
        if (_occupied is 0)
            return;
        ++_gripGen;
        _occupied = 0;
        Proclaim(StateChanged);
    }

    public void ResetSession()
    {
        bool altered = _queued is not null || _occupied is not 0;
        _queued = null;
        ++_gripGen;
        _occupied = 0;
        if (altered)
            Proclaim(StateChanged);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _objects.Cleared -= OnChartCleared;
        _objects.StackSizeUpdated -= OnRestacked;
        _objects.ObjectRemoved -= OnRemoved;
        _objects.MoveRequestFailed -= OnRelocateRefused;
        _objects.ObjectMoved -= OnMoved;
        _queued = null;
        ++_gripGen;
        _occupied = 0;
        StateChanged = null;
        RequestCompleted = null;
        RequestFailed = null;
        ObjectTableCleared = null;
    }

    private ulong UpcomingTicket()
    {
        ulong ticket = ++_ticketSeed;
        return ticket is 0u ? ++_ticketSeed : ticket;
    }

    private bool IsStillQueued(PackRequestInFlight anticipated)
    {
        return _queued is { } latest
        && latest.Token == anticipated.Token && latest.ItemId == anticipated.ItemId
        && latest.Kind == anticipated.Kind && !latest.Dispatched;
    }

    private void Discard(ulong ticket)
    {
        if (_queued is not { } queued || queued.Token != ticket)
            return;
        _queued = null;
        Proclaim(StateChanged);
    }

    private void OnMoved(ObjectRelocation relocate)
    {
        if (relocate.Origin == ObjectRelocationOrigin.AuthoritativeResponse)
            Settle(relocate.ItemId, relocate.Item);
    }

    private void OnRelocateRefused(RelocationRefusal refusal)
    {
        uint gearIdent = refusal.ItemId;
        // A refusal without a guid can only mean the one pending request
        if (gearIdent is 0u && _queued is { } latest)
            gearIdent = latest.ItemId;
        if (Settle(gearIdent, _objects.Get(gearIdent)) is { } failed)
            Proclaim(RequestFailed, failed, refusal.WeenieError);
    }

    private void OnRemoved(ClientThing gear) => Settle(gear.ObjectId, gear);

    private void OnRestacked(ClientThing gear) => Settle(gear.ObjectId, gear);

    private PackRequestInFlight? Settle(uint gearIdent, ClientThing? persona)
    {
        if (_queued is not { } req || req.ItemId != gearIdent || !SameObject(req.ItemIdentity, gearIdent, persona))
            return null;
        _queued = null;
        Proclaim(RequestCompleted, req);
        Proclaim(StateChanged);
        return req;
    }

    // A response counts only if it is about the same object instance the request was made for
    private bool SameObject(ClientThing? anticipated, uint gearIdent, ClientThing? actual)
    {
        if (anticipated is null)
            return true;
        if (actual is not null)
            return ReferenceEquals(anticipated, actual);
        return _objects.Get(gearIdent) is not { } latest || ReferenceEquals(anticipated, latest);
    }

    private void OnChartCleared()
    {
        bool altered = _queued is not null;
        _queued = null;
        Proclaim(ObjectTableCleared);
        if (altered)
            Proclaim(StateChanged);
    }

    private void Proclaim(Action? watchers)
    {
        if (watchers is null)
            return;
        foreach (Action watcher in watchers.GetInvocationList())
            Guarded(() => watcher());
    }

    private void Proclaim<T>(Action<T>? watchers, T val)
    {
        if (watchers is null)
            return;
        foreach (Action<T> watcher in watchers.GetInvocationList())
            Guarded(() => watcher(val));
    }

    private void Proclaim<T1, T2>(Action<T1, T2>? watchers, T1 a, T2 b)
    {
        if (watchers is null)
            return;
        foreach (Action<T1, T2> watcher in watchers.GetInvocationList())
            Guarded(() => watcher(a, b));
    }

    private void Guarded(Action call)
    {
        try
        {
            call();
        }
        catch (Exception problem)
        {
            Interlocked.Increment(ref _watcherMisses);
            PreviousDispatchFailure = problem;
        }
    }
}
