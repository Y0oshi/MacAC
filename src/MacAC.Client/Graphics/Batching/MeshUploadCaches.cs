using System.Collections.Concurrent;
using MacAC.Assets;

namespace MacAC.Client.Graphics.Batching;

internal enum TriMeshJunctureOutcome
{
    Staged,
    AlreadyStaged,
    HighWater,
    NotOwned,
}

internal readonly record struct MeshUploadQueueGear(
    HarvestedMesh Data,
    long SourceBytes,
    ulong Generation);

// Deduplicated CPU-to-GPU staging queue
internal sealed class TriMeshPushLoadingFifo
{
    internal const int DefaultCeilingTally = 256;
    internal const long DefaultCeilingOctets = 128L * 1024 * 1024;
    internal const long DefaultCeilingSingleListingOctets = 128L * 1024 * 1024;

    private readonly object _latch = new();
    private readonly Queue<Listing> _fifo = new();
    private readonly Dictionary<ulong, ulong> _genByIdent = [];
    private readonly long _ceilingSingleListingOctets;
    private long _queuedOctets;
    private long _claimedOctets;
    private ulong _upcomingGen;

    private readonly record struct Listing(HarvestedMesh Data, long Bytes, ulong Generation)
    {
        public MeshUploadQueueGear Item => new(Data, Bytes, Generation);
    }

    public TriMeshPushLoadingFifo(
        int ceilingTally = DefaultCeilingTally,
        long ceilingOctets = DefaultCeilingOctets,
        long ceilingSingleListingOctets = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingTally, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingOctets, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(ceilingSingleListingOctets);
        if (ceilingSingleListingOctets is 0)
            ceilingSingleListingOctets = Math.Max(ceilingOctets, DefaultCeilingSingleListingOctets);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingSingleListingOctets, ceilingOctets);
        CeilingTally = ceilingTally;
        _ceilingOctets = ceilingOctets;
        _ceilingSingleListingOctets = ceilingSingleListingOctets;
    }

    public bool Stage(HarvestedMesh blob)
        => TryJuncture(blob) == TriMeshJunctureOutcome.Staged;

    public TriMeshJunctureOutcome TryJuncture(HarvestedMesh blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        long octets = ThingTriMeshKeeper.GuessPushOctets(blob);
        lock (_latch)
        {
            if (_genByIdent.ContainsKey(blob.ObjectId))
                return TriMeshJunctureOutcome.AlreadyStaged;

            if (octets > _ceilingSingleListingOctets)
                throw new NotSupportedException(
                    $"Mesh 0x{blob.ObjectId:X10} needs {octets:N0} staged bytes; "
                    + $"the supported per-object maximum is {_ceilingSingleListingOctets:N0} bytes.");

            if (_genByIdent.Count is not 0
                && (_genByIdent.Count >= CeilingTally
                    || octets > _ceilingOctets - Math.Min(_claimedOctets, _ceilingOctets)))

                return TriMeshJunctureOutcome.HighWater;

            ulong gen = checked(++_upcomingGen);
            _genByIdent.Add(blob.ObjectId, gen);
            blob.UploadAttempts = 0;
            _fifo.Enqueue(new Listing(blob, octets, gen));
            _queuedOctets = checked(_queuedOctets + octets);
            _claimedOctets = checked(_claimedOctets + octets);
            return TriMeshJunctureOutcome.Staged;
        }
    }

    public bool TryDequeue(out MeshUploadQueueGear gear)
    {
        lock (_latch)
        {
            if (!_fifo.TryDequeue(out Listing listing))
            {
                gear = default;
                return false;
            }
            _queuedOctets -= listing.Bytes;
            gear = listing.Item;
            return true;
        }
    }

    public bool TryGlimpse(out MeshUploadQueueGear gear)
    {
        lock (_latch)
        {
            if (!_fifo.TryPeek(out Listing listing))
            {
                gear = default;
                return false;
            }
            gear = listing.Item;
            return true;
        }
    }

    public int Count { get { lock (_latch) return _fifo.Count; } }
    public int ClaimTally { get { lock (_latch) return _genByIdent.Count; } }
    public long QueuedOctets { get { lock (_latch) return _queuedOctets; } }
    public long ClaimedOctets { get { lock (_latch) return _claimedOctets; } }
    public int CeilingTally { get; }
    private readonly long _ceilingOctets;

    public long CeilingOctets => _ceilingOctets;
    public bool IsAtHiWater
    {
        get
        {
            lock (_latch)
                return _genByIdent.Count >= CeilingTally || _claimedOctets >= _ceilingOctets;
        }
    }

    public void Requeue(MeshUploadQueueGear gear)
    {
        ArgumentNullException.ThrowIfNull(gear.Data);
        lock (_latch)
        {
            VetLatestGen(gear);
            if (gear.SourceBytes > _ceilingSingleListingOctets)
                throw new InvalidOperationException("Can't retry mesh data after staging ownership completed");
            _fifo.Enqueue(new Listing(gear.Data, gear.SourceBytes, gear.Generation));
            _queuedOctets = checked(_queuedOctets + gear.SourceBytes);
        }
    }

    public void Complete(MeshUploadQueueGear gear)
    {
        lock (_latch)
        {
            VetLatestGen(gear);
            _genByIdent.Remove(gear.Data.ObjectId);
            _claimedOctets = checked(_claimedOctets - gear.SourceBytes);
        }
    }

    public bool ConcludeOrRestageIfPossessed(
        MeshUploadQueueGear gear,
        TriMeshOwnershipCounter ownership)
    {
        ArgumentNullException.ThrowIfNull(gear.Data);
        ArgumentNullException.ThrowIfNull(ownership);
        lock (_latch)
        {
            VetLatestGen(gear);
            _genByIdent.Remove(gear.Data.ObjectId);
            _claimedOctets = checked(_claimedOctets - gear.SourceBytes);
            if (!ownership.IsPossessed(gear.Data.ObjectId))
                return false;

            ulong gen = checked(++_upcomingGen);
            _genByIdent.Add(gear.Data.ObjectId, gen);
            gear.Data.UploadAttempts = 0;
            _fifo.Enqueue(new Listing(gear.Data, gear.SourceBytes, gen));
            _queuedOctets = checked(_queuedOctets + gear.SourceBytes);
            _claimedOctets = checked(_claimedOctets + gear.SourceBytes);
            return true;
        }
    }

    public int TossUnownedStem(TriMeshOwnershipCounter ownership, int ceiling)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentOutOfRangeException.ThrowIfNegative(ceiling);
        int discarded = 0;
        lock (_latch)
        {
            while (discarded < ceiling
                && _fifo.TryPeek(out Listing listing)
                && !ownership.IsPossessed(listing.Data.ObjectId))
            {
                _fifo.Dequeue();
                _queuedOctets -= listing.Bytes;
                if (_genByIdent.TryGetValue(listing.Data.ObjectId, out ulong gen)
                    && gen == listing.Generation)
                {
                    _genByIdent.Remove(listing.Data.ObjectId);
                    _claimedOctets = checked(_claimedOctets - listing.Bytes);
                }
                ++discarded;
            }
        }
        return discarded;
    }

    private void VetLatestGen(MeshUploadQueueGear gear)
    {
        if (!_genByIdent.TryGetValue(gear.Data.ObjectId, out ulong latest)
            || latest != gear.Generation)
        {
            throw new InvalidOperationException(
                $"Stale mesh-upload generation {gear.Generation} for 0x{gear.Data.ObjectId:X10}; current={latest}.");
        }
    }
}

internal sealed class TriMeshOwnershipCounter
{
    private readonly ConcurrentDictionary<ulong, int> _counts = new();

    public int Obtain(ulong ident) => _counts.AddOrUpdate(ident, 1, (_, tally) => tally + 1);

    public int Release(ulong ident) => _counts.AddOrUpdate(ident, 0, (_, tally) => Math.Max(0, tally - 1));

    public int Count(ulong ident) => _counts.TryGetValue(ident, out int tally) ? tally : 0;

    public bool IsPossessed(ulong ident) => Count(ident) > 0;

    public int PossessedIdentTally => _counts.Count(duo => duo.Value > 0);

    public int SumReferenceTally
    {
        get
        {
            return _counts.Sum(
        duo => Math.Max(0, duo.Value));
        }
    }

    // Reports whether freshly uploaded data has a live owner
    public bool FlagPushDone(ulong ident) => IsPossessed(ident);

    public bool Drop(ulong ident) => _counts.TryRemove(ident, out _);
}

internal sealed class CpuMeshUploadShelf
{
    private readonly Dictionary<ulong, HarvestedMesh> _blob = [];
    private readonly Dictionary<ulong, long> _octetsByIdent = [];
    private readonly LinkedList<ulong> _lru = new();
    private long _housedOctets;

    private long _strikes;
    private long _misses;
    private long _evictions;

    internal ShelfStats Stats
    {
        get
        {
            return new(
        Interlocked.Read(ref _strikes),
        Interlocked.Read(ref _misses),
        Interlocked.Read(ref _evictions));
        }
    }

    public CpuMeshUploadShelf(int capacity, long byteCap = 128L * 1024 * 1024)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(byteCap, 1);
        _cap = capacity;
        _byteCap = byteCap;
    }

    internal int Count { get { lock (_blob) return _blob.Count; } }
    internal long HousedOctets { get { lock (_blob) return _housedOctets; } }
    private readonly int _cap;

    internal int Capacity => _cap;
    private readonly long _byteCap;

    internal long ByteCap => _byteCap;
    public bool TryFetchAndJuncturePossessed(
        ulong ident,
        TriMeshPushLoadingFifo loading,
        TriMeshOwnershipCounter ownership,
        out HarvestedMesh? blob,
        out TriMeshJunctureOutcome junctureOutcome)
    {
        ArgumentNullException.ThrowIfNull(loading);
        ArgumentNullException.ThrowIfNull(ownership);
        lock (_blob)
        {
            if (!_blob.TryGetValue(ident, out blob))
            {
                Interlocked.Increment(ref _misses);
                junctureOutcome = default;
                return false;
            }
            Interlocked.Increment(ref _strikes);
            _lru.Remove(ident);
            _lru.AddLast(ident);
            // Prepared payload residency is deliberately independent from live render ownership.
            junctureOutcome = ownership.IsPossessed(ident)
                ? loading.TryJuncture(blob)
                : TriMeshJunctureOutcome.NotOwned;
            return true;
        }
    }

    public bool Store(HarvestedMesh blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        long octets = ThingTriMeshKeeper.GuessPushOctets(blob);
        if (octets > _byteCap)
            return false;
        lock (_blob)
        {
            if (_octetsByIdent.Remove(blob.ObjectId, out long replacedOctets))
                _housedOctets -= replacedOctets;

            while (_lru.Count is not 0
                && (!_blob.ContainsKey(blob.ObjectId) && _blob.Count >= _cap
                    || (_blob.Count is not 0 && octets > _byteCap - _housedOctets)))
            {
                ulong oldest = _lru.First!.Value;
                _lru.RemoveFirst();
                _blob.Remove(oldest);
                if (_octetsByIdent.Remove(oldest, out long oldestOctets))
                    _housedOctets -= oldestOctets;
                Interlocked.Increment(ref _evictions);
            }

            _blob[blob.ObjectId] = blob;
            _octetsByIdent[blob.ObjectId] = octets;
            _housedOctets = checked(_housedOctets + octets);
            _lru.Remove(blob.ObjectId);
            _lru.AddLast(blob.ObjectId);
            return true;
        }
    }

    public void Clear()
    {
        lock (_blob)
        {
            _blob.Clear();
            _octetsByIdent.Clear();
            _lru.Clear();
            _housedOctets = 0;
        }
    }
}
