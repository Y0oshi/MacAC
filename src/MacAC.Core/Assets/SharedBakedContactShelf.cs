using MacAC.Assets.Pak;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

public readonly record struct SharedBakedContactShelfFrame(int Count, int Capacity, long Hits, long Misses, bool IsDisposed)
{
    public bool IsBounded => Count >= 0 && Count <= Capacity;
}

public sealed class SharedBakedContactShelf : IBakedContactSource
{
    public const int DefaultCap = 4096;

    private readonly record struct ShelfKey(PakAssetKind Type, uint SourceFileId);

    private sealed record Outcome(ShelfKey Key, BakedAssetReadStatus Status, object? Data);

    private readonly Lock _latch = new();
    private readonly IBakedContactSource _src;
    private readonly int _cap;
    private readonly Dictionary<ShelfKey, LinkedListNode<Outcome>> _ordinal = new();
    private readonly LinkedList<Outcome> _recency = new();   // newest first
    private long _sensors;
    private long _reads;
    private long _fetched;
    private long _absent;
    private long _corrupt;
    private long _strikes;
    private long _misses;
    private bool _destroyed;

    public SharedBakedContactShelf(IBakedContactSource source, int capacity = DefaultCap)
    {
        _src = source ?? throw new ArgumentNullException(nameof(source));
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _cap = capacity;
    }

    public BakedContactSourceStats ImpactStats
    {
        get
        {
            return new(
        Interlocked.Read(ref _sensors),
        Interlocked.Read(ref _reads),
        Interlocked.Read(ref _fetched),
        Interlocked.Read(ref _absent),
        Interlocked.Read(ref _corrupt));
        }
    }

    public SharedBakedContactShelfFrame GrabCapture()
    {
        lock (_latch)
            return new SharedBakedContactShelfFrame(_ordinal.Count, _cap, Interlocked.Read(ref _strikes), Interlocked.Read(ref _misses), _destroyed);
    }

    public BakedAssetPresence InspectImpact(PakAssetKind kind, uint srcFileIdent)
    {
        BakedContactTypeContract.Validate(kind);
        Interlocked.Increment(ref _sensors);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            if (!_ordinal.TryGetValue(new ShelfKey(kind, srcFileIdent), out LinkedListNode<Outcome>? joint))
                return _src.InspectImpact(kind, srcFileIdent);

            Touch(joint);
            return joint.Value.Status switch
            {
                BakedAssetReadStatus.Loaded => BakedAssetPresence.Available,
                BakedAssetReadStatus.Corrupt => BakedAssetPresence.Corrupt,
                _ => BakedAssetPresence.Missing,
            };
        }
    }

    public BakedContactRead<PackedGfxObjContactAsset> ScanGfxObjRefImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Read(PakAssetKind.GfxObjCollision, srcFileIdent, ticket => _src.ScanGfxObjRefImpact(srcFileIdent, ticket), abortTicket);
    }

    public BakedContactRead<PackedSetupContact> ReadSetupCollision(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Read(PakAssetKind.SetupCollision, srcFileIdent, ticket => _src.ReadSetupCollision(srcFileIdent, ticket), abortTicket);
    }

    public BakedContactRead<PackedCellStructContactAsset> ScanChamberStructureImpact(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Read(PakAssetKind.CellStructureCollision, srcFileIdent, ticket => _src.ScanChamberStructureImpact(srcFileIdent, ticket), abortTicket);
    }

    public BakedContactRead<PackedEnvCellTopology> ScanEnvironChamberWiring(uint srcFileIdent, CancellationToken abortTicket = default)
    {
        return Read(PakAssetKind.EnvCellTopology, srcFileIdent, ticket => _src.ScanEnvironChamberWiring(srcFileIdent, ticket), abortTicket);
    }

    public void Dispose()
    {
        lock (_latch)
        {
            if (_destroyed)
                return;
            _ordinal.Clear();
            _recency.Clear();
            _destroyed = true;
        }
    }

    private BakedContactRead<T> Read<T>(PakAssetKind kind, uint srcFileIdent, Func<CancellationToken, BakedContactRead<T>> pull, CancellationToken abortTicket)
        where T : class
    {
        abortTicket.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            ShelfKey tag = new ShelfKey(kind, srcFileIdent);
            if (_ordinal.TryGetValue(tag, out LinkedListNode<Outcome>? joint))
            {
                Touch(joint);
                Interlocked.Increment(ref _strikes);
                return Replay<T>(joint.Value);
            }

            Interlocked.Increment(ref _misses);
            var fresh = pull(abortTicket);
            abortTicket.ThrowIfCancellationRequested();
            Outcome verdict = new Outcome(tag, fresh.Status, fresh.Data);
            _ordinal.Add(tag, _recency.AddFirst(verdict));
            EvictToCap();
            return Replay<T>(verdict);
        }
    }

    // Turns a shelved outcome back into a typed read, counting it as this call's result
    private BakedContactRead<T> Replay<T>(Outcome verdict) where T : class
    {
        switch (verdict.Status)
        {
            case BakedAssetReadStatus.Loaded when verdict.Data is T blob:
                Interlocked.Increment(ref _fetched);
                return BakedContactRead<T>.Loaded(blob);
            case BakedAssetReadStatus.Missing:
                Interlocked.Increment(ref _absent);
                return BakedContactRead<T>.Missing;
            case BakedAssetReadStatus.Corrupt:
                Interlocked.Increment(ref _corrupt);
                return BakedContactRead<T>.Corrupt;
            default:
                throw new InvalidDataException($"Prepared collision cache entry {verdict.Key} has an not valid payload");
        }
    }

    private void Touch(LinkedListNode<Outcome> joint)
    {
        if (ReferenceEquals(_recency.First, joint))
            return;
        _recency.Remove(joint);
        _recency.AddFirst(joint);
    }

    private void EvictToCap()
    {
        while (_ordinal.Count > _cap)
        {
            LinkedListNode<Outcome> oldest = _recency.Last ?? throw new InvalidOperationException("Prepared collision LRU lost its terminal entry");
            _ordinal.Remove(oldest.Value.Key);
            _recency.RemoveLast();
        }
    }
}
