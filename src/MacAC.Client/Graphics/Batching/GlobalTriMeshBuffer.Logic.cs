using System.Runtime.InteropServices;
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class GlobalTriMeshBuffer
{

    public void Dispose()
    {
        if (_destroyed)
            return;
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();

        if (_teardownAssetList is null)
        {
            var releases = new List<(string Name, Action Release)>();
            if (_migration is { } migration)
            {
                var free =
                    BuildRetryableVaultDeletion(
                        migration.NewBuffer,
                        migration.NewCapacityBytes,
                        $"deleting staged global {migration.Kind} arena buffer '{migration.NewBuffer.Name}'");
                releases.Add(("staged-migration-buffer", free.Run));
            }

            if (VertVault is { } vertVault)
            {
                var free =
                    BuildRetryableVaultDeletion(
                        vertVault,
                        (long)_verts.Capacity * VertLocusNormBitmap.Size,
                        $"deleting global mesh vertex buffer '{vertVault.Name}'");
                releases.Add(("global-vbo", free.Run));
            }
            if (OrdinalVault is { } ordinalVault)
            {
                var free =
                    BuildRetryableVaultDeletion(
                        ordinalVault,
                        (long)_ordinals.Capacity * sizeof(ushort),
                        $"deleting global mesh index buffer '{ordinalVault.Name}'");
                releases.Add(("global-ibo", free.Run));
            }
            _teardownAssetList = new RetryableAssetFreeRegister(releases);
        }

        var attempt = _teardownAssetList.Advance();
        if (!_teardownAssetList.IsComplete)
            throw attempt.ToException(
                "One or more global mesh-buffer resources could not be released.");

        _migration = null;
        _migrationCancel = null;
        VertVault = null;
        OrdinalVault = null;
        _teardownAssetList = null;
        _destroyed = true;

        if (attempt.HasMisses)
            throw attempt.ToException(
                "Global mesh-buffer resources released with exceptional committed outcomes.");
    }

    internal IClientGpuBuffer? VertVault { get; private set; }

    internal int VertHiWaterFlag => _verts.HiWaterMark;

    internal IClientGpuBuffer? OrdinalVault { get; private set; }

    internal int OrdinalHiWaterFlag => _ordinals.HiWaterMark;

    // True once both backing stores exist
    internal bool HasStores => VertVault is not null && OrdinalVault is not null;

    internal bool HasQueuedReclamation
    {
        get
        {
            return _migration is not null
        || _verts.QueuedFreeTally is not 0
        || _ordinals.QueuedFreeTally is not 0
        || RetiredBackingOctets is not 0;
        }
    }

    internal long PushTally { get; private set; }

    internal GlobalTriMeshAlloc PushTriMesh(
        VertLocusNormBitmap[] vertices,
        ushort[] ordinals,
        ReadOnlySpan<(int Offset, int Count)> indexBatches)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();
        if (_migration is not null)
            throw new InvalidOperationException("A mesh upload can't mutate the arena while a backing-buffer migration is in progress");
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(ordinals);
        if (vertices.Length is 0)
            throw new ArgumentException("A global mesh allocation needs vertices", nameof(vertices));

        int sumOrdinals = 0;
        for (int idx = 0; idx < indexBatches.Length; ++idx)
        {
            (int shift, int tally) = indexBatches[idx];
            if (shift < 0 || tally <= 0 || (long)shift + tally > ordinals.Length)
            {
                throw new ArgumentException(
                    $"Index batch {idx} ({shift}, {tally}) is beyond the shared index array ({ordinals.Length}).",
                    nameof(indexBatches));
            }
            sumOrdinals = checked(sumOrdinals + tally);
        }
        if (sumOrdinals is 0)
            throw new ArgumentException("A global mesh allocation needs indices", nameof(indexBatches));

        var vertSpan = ReserveVerts(vertices.Length);
        TriMeshBufferSpan ordinalSpan;
        try
        {
            ordinalSpan = ReserveOrdinals(sumOrdinals);
        }
        catch
        {
            _verts.FreeUnsubmitted(vertSpan);
            throw;
        }

        int[] leadOrdinals = new int[indexBatches.Length];
        try
        {
            long vertShiftOctets = checked((long)vertSpan.Offset * VertLocusNormBitmap.Size);
            DemandVault(VertVault).Upload(
                vertShiftOctets,
                MemoryMarshal.AsBytes(new ReadOnlySpan<VertLocusNormBitmap>(vertices)));

            IClientGpuBuffer ordinalVault = DemandVault(OrdinalVault);
            int ordinalShift = ordinalSpan.Offset;
            for (int idx = 0; idx < indexBatches.Length; ++idx)
            {
                (int shift, int tally) = indexBatches[idx];
                leadOrdinals[idx] = ordinalShift;
                long ordinalShiftOctets = checked((long)ordinalShift * sizeof(ushort));
                ordinalVault.Upload(
                    ordinalShiftOctets,
                    MemoryMarshal.AsBytes(new ReadOnlySpan<ushort>(ordinals, shift, tally)));
                ordinalShift = checked(ordinalShift + tally);
            }
        }
        catch
        {
            _ordinals.FreeUnsubmitted(ordinalSpan);
            _verts.FreeUnsubmitted(vertSpan);
            throw;
        }

        ++PushTally;
        UploadedOctets = checked(UploadedOctets
            + checked((long)vertices.Length * VertLocusNormBitmap.Size)
            + checked((long)sumOrdinals * sizeof(ushort)));
        return new GlobalTriMeshAlloc(vertSpan, ordinalSpan, leadOrdinals);
    }

    internal long UploadedOctets { get; private set; }

    internal long CapOctets
    {
        get
        {
            return (long)_verts.Capacity * VertLocusNormBitmap.Size
        + (long)_ordinals.Capacity * sizeof(ushort);
        }
    }

    internal GlobalTriMeshPushScheme PlanPush(int vertTally, int ordinalTally)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();
        if (_migration is not null)
            throw new InvalidOperationException("Upload planning is not available while a backing-buffer migration is in progress");
        ArgumentOutOfRangeException.ThrowIfNegative(vertTally);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinalTally);
        long allocOctets = 0;
        long duplicateOctets = 0;
        int newBufs = 0;

        if (vertTally > _verts.LargestSpareRange)
        {
            int newCap = DeriveGrowthCap(
                _verts.Capacity, _verts.TrailingSpareLength,
                vertTally, VertGrowthQuantum, CeilingVertCap);
            allocOctets = checked(allocOctets
                + (long)newCap * VertLocusNormBitmap.Size);
            duplicateOctets = checked(duplicateOctets
                + (long)_verts.HiWaterMark * VertLocusNormBitmap.Size);
            ++newBufs;
        }
        if (ordinalTally > _ordinals.LargestSpareRange)
        {
            int newCap = DeriveGrowthCap(
                _ordinals.Capacity, _ordinals.TrailingSpareLength,
                ordinalTally, OrdinalGrowthQuantum, CeilingOrdinalCap);
            allocOctets = checked(allocOctets + (long)newCap * sizeof(ushort));
            duplicateOctets = checked(duplicateOctets + (long)_ordinals.HiWaterMark * sizeof(ushort));
            ++newBufs;
        }

        return new GlobalTriMeshPushScheme(
            checked((long)vertTally * VertLocusNormBitmap.Size
                + (long)ordinalTally * sizeof(ushort)),
            allocOctets,
            duplicateOctets,
            newBufs);
    }

    internal long PhysicalCapOctets
    {
        get
        {
            return checked(
        CapOctets + (_migration?.NewCapacityBytes ?? 0) + RetiredBackingOctets);
        }
    }

    internal bool IsMigrationInHeadway => _migration is not null;

    internal long ConsumedOctets
    {
        get
        {
            return checked(
        (long)_verts.Used * VertLocusNormBitmap.Size
        + (long)_ordinals.Used * sizeof(ushort));
        }
    }

    internal long LargestSpareOctets
    {
        get
        {
            return checked(
        (long)_verts.LargestSpareRange * VertLocusNormBitmap.Size
        + (long)_ordinals.LargestSpareRange * sizeof(ushort));
        }
    }

    internal long QueuedSpanSunsetOctets
    {
        get
        {
            return checked(
        (long)_verts.QueuedFreeLen * VertLocusNormBitmap.Size
        + (long)_ordinals.QueuedFreeLen * sizeof(ushort));
        }
    }

    internal long AskedMigrationOctets => _migration?.NewCapacityBytes ?? 0;

    internal long RetiredBackingOctets { get; private set; }

    internal long HousedCapOctets
    {
        get
        {
            return checked(
        CapOctets - QueuedSpanSunsetOctets);
        }
    }

    internal void Release(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _ordinals.FreeFollowingGpuUse(alloc.Indices);
        _verts.FreeFollowingGpuUse(alloc.Vertices);
    }

    internal void FreeOrdinalSpan(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _ordinals.FreeFollowingGpuUse(alloc.Indices);
    }

    internal void FreeVertSpan(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _verts.FreeFollowingGpuUse(alloc.Vertices);
    }

    internal void Cancel(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _ordinals.FreeUnsubmitted(alloc.Indices);
        _verts.FreeUnsubmitted(alloc.Vertices);
    }

    internal void CancelOrdinalSpan(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _ordinals.FreeUnsubmitted(alloc.Indices);
    }

    internal void CancelVertSpan(GlobalTriMeshAlloc alloc)
    {
        ArgumentNullException.ThrowIfNull(alloc);
        _verts.FreeUnsubmitted(alloc.Vertices);
    }

    internal static int DeriveGrowthCap(
        int cap,
        int trailingSpareLen,
        int neededContiguousLen,
        int growthQuantum,
        int ceilingCap = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        ArgumentOutOfRangeException.ThrowIfNegative(trailingSpareLen);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trailingSpareLen, cap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(neededContiguousLen);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthQuantum);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingCap, cap);

        long absent = Math.Max(0L, (long)neededContiguousLen - trailingSpareLen);
        if (absent is 0)
            return cap;

        long floor = checked((long)cap + absent);
        if (floor > ceilingCap)
            throw new NotSupportedException(
                $"A contiguous range of {neededContiguousLen:N0} elements exceeds the supported arena capacity {ceilingCap:N0}.");

        long geometric = checked((long)cap + Math.Max((long)growthQuantum, cap / 2L));
        long mark = Math.Min(ceilingCap, Math.Max(floor, geometric));
        return RoundUpToThreshold(mark, growthQuantum, ceilingCap);
    }

    internal static long DeriveDuplicateChunk(long sumOctets, long copiedOctets, long ceilingDuplicateOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sumOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(copiedOctets);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(copiedOctets, sumOctets);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceilingDuplicateOctets);
        return Math.Min(sumOctets - copiedOctets, ceilingDuplicateOctets);
    }

    internal static bool TryDeriveTrimCap(
        int cap,
        int hiWaterFlag,
        int startingCap,
        int growthQuantum,
        out int trimmedCap)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
        ArgumentOutOfRangeException.ThrowIfNegative(hiWaterFlag);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hiWaterFlag, cap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startingCap);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(growthQuantum);

        trimmedCap = cap;
        if (cap <= startingCap)
            return false;

        long withHeadroom = checked(
            hiWaterFlag + Math.Max((long)growthQuantum, hiWaterFlag));
        int mark = Math.Max(
            startingCap,
            RoundUpToThreshold(withHeadroom, growthQuantum, int.MaxValue));
        if (mark > cap / 3)
            return false;

        trimmedCap = mark;
        return true;
    }

    internal GlobalTriMeshCapOutcome SecurePushCap(
        int vertTally,
        int ordinalTally,
        out GlobalTriMeshMaintenanceHop hop)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();
        ArgumentOutOfRangeException.ThrowIfNegative(vertTally);
        ArgumentOutOfRangeException.ThrowIfNegative(ordinalTally);
        if (vertTally > CeilingVertCap)
            throw new NotSupportedException(
                $"Mesh needs {vertTally:N0} vertices; the supported per-arena maximum is {CeilingVertCap:N0}.");
        if (ordinalTally > CeilingOrdinalCap)
            throw new NotSupportedException(
                $"Mesh needs {ordinalTally:N0} indices; the supported per-arena maximum is {CeilingOrdinalCap:N0}.");

        hop = default;
        if (_migration is not null)
            return GlobalTriMeshCapOutcome.MigrationInProgress;
        if (vertTally <= _verts.LargestSpareRange
            && ordinalTally <= _ordinals.LargestSpareRange)

            return GlobalTriMeshCapOutcome.Ready;

        BufferFlavor sort;
        int markCap;
        long duplicateOctets;
        if (vertTally > _verts.LargestSpareRange)
        {
            long floor = checked(
                (long)_verts.Capacity
                + Math.Max(0L, (long)vertTally - _verts.TrailingSpareLength));
            if (floor > CeilingVertCap)
                return GlobalTriMeshCapOutcome.NeedsReclamation;
            sort = BufferFlavor.Vertices;
            markCap = DeriveGrowthCap(
                _verts.Capacity,
                _verts.TrailingSpareLength,
                vertTally,
                VertGrowthQuantum,
                CeilingVertCap);
            duplicateOctets = checked((long)_verts.HiWaterMark * VertLocusNormBitmap.Size);
        }
        else
        {
            long floor = checked(
                (long)_ordinals.Capacity
                + Math.Max(0L, (long)ordinalTally - _ordinals.TrailingSpareLength));
            if (floor > CeilingOrdinalCap)
                return GlobalTriMeshCapOutcome.NeedsReclamation;
            sort = BufferFlavor.Indices;
            markCap = DeriveGrowthCap(
                _ordinals.Capacity,
                _ordinals.TrailingSpareLength,
                ordinalTally,
                OrdinalGrowthQuantum,
                CeilingOrdinalCap);
            duplicateOctets = checked((long)_ordinals.HiWaterMark * sizeof(ushort));
        }

        long newOctets = CapOctetsFor(sort, markCap);
        if (newOctets > CeilingPhysicalArenaOctets - PhysicalCapOctets)
            return GlobalTriMeshCapOutcome.NeedsReclamation;

        CommenceMigration(sort, markCap, duplicateOctets);
        hop = new GlobalTriMeshMaintenanceHop(newOctets, 0, 1, false);
        return GlobalTriMeshCapOutcome.MigrationStarted;
    }

    internal GlobalTriMeshMaintenanceHop ProgressMigration(long ceilingDuplicateOctets)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceilingDuplicateOctets);
        var migration = _migration;
        if (migration is null)
            return default;

        long chunk = DeriveDuplicateChunk(
            migration.CopyBytes,
            migration.CopiedOctets,
            ceilingDuplicateOctets);
        try
        {
            if (chunk is not 0)
            {
                migration.OldBuffer.ReplicateTo(
                    migration.NewBuffer,
                    migration.CopiedOctets,
                    migration.CopiedOctets,
                    chunk);
                migration.CopiedOctets = checked(migration.CopiedOctets + chunk);
            }

            bool done = migration.CopiedOctets == migration.CopyBytes;
            if (done)
                SealMigration(migration);
            return new GlobalTriMeshMaintenanceHop(0, chunk, 0, done);
        }
        catch (Exception migrationProblem)
        {
            try
            {
                CancelMigration(migration);
            }
            catch (Exception cancelProblem)
            {
                throw new AggregateException(
                    "Global mesh migration failed and its staged buffer could not yet be released",
                    migrationProblem,
                    cancelProblem);
            }
            throw;
        }
    }

    internal bool TryTrimUnusedRear(out GlobalTriMeshMaintenanceHop hop)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _sunsetRegister.ReattemptPendingPublications();
        ReattemptQueuedMigrationCancel();
        hop = default;
        if (_migration is not null)
            return false;
        bool trimVerts = TryDeriveTrimCap(
            _verts.Capacity, _verts.HiWaterMark,
            StartingVertCap, VertGrowthQuantum,
            out int vertCap);
        bool trimOrdinals = TryDeriveTrimCap(
            _ordinals.Capacity, _ordinals.HiWaterMark,
            StartingOrdinalCap, OrdinalGrowthQuantum,
            out int ordinalCap);

        long vertSaving = trimVerts
            ? (long)(_verts.Capacity - vertCap) * VertLocusNormBitmap.Size
            : 0;
        long ordinalSaving = trimOrdinals
            ? (long)(_ordinals.Capacity - ordinalCap) * sizeof(ushort)
            : 0;
        if (vertSaving is 0 && ordinalSaving is 0)
            return false;

        BufferFlavor sort;
        int cap;
        long duplicateOctets;
        if (vertSaving >= ordinalSaving)
        {
            sort = BufferFlavor.Vertices;
            cap = vertCap;
            duplicateOctets = checked((long)_verts.HiWaterMark * VertLocusNormBitmap.Size);
        }
        else
        {
            sort = BufferFlavor.Indices;
            cap = ordinalCap;
            duplicateOctets = checked((long)_ordinals.HiWaterMark * sizeof(ushort));
        }

        long newOctets = CapOctetsFor(sort, cap);
        if (newOctets > CeilingPhysicalArenaOctets - PhysicalCapOctets)
            return false;
        CommenceMigration(sort, cap, duplicateOctets);
        hop = new GlobalTriMeshMaintenanceHop(newOctets, 0, 1, false);
        return true;
    }
    private static IClientGpuBuffer DemandVault(IClientGpuBuffer? vault)
    {
        return vault ?? throw new InvalidOperationException(
            "The global mesh arena has no live backing store");
    }

    private static long CapOctetsFor(BufferFlavor kind, int cap)
    {
        return kind switch
        {
            BufferFlavor.Vertices => checked((long)cap * VertLocusNormBitmap.Size),
            BufferFlavor.Indices => checked((long)cap * sizeof(ushort)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static GpuBufferSpec DepictVault(BufferFlavor sort, long byteSize, int gen)
    {
        return new(
            sort == BufferFlavor.Vertices
                ? $"mesh-arena-vertex-{gen}"
                : $"mesh-arena-index-{gen}",
            byteSize,
            (sort == BufferFlavor.Vertices ? GpuBufferPurpose.Vertex : GpuBufferPurpose.Index)
                | GpuBufferPurpose.TransferSource
                | GpuBufferPurpose.TransferDestination,
            GpuMemoryTenancy.DeviceLocal);
    }

    private void PrimeBufs()
    {
        IClientGpuBuffer? vbo = null;
        IClientGpuBuffer? ibo = null;
        long vertOctets = (long)_verts.Capacity * VertLocusNormBitmap.Size;
        long ordinalOctets = (long)_ordinals.Capacity * sizeof(ushort);
        bool vertFollowed = false;
        bool ordinalFollowed = false;

        try
        {
            vbo = _device.BuildBuf(DepictVault(BufferFlavor.Vertices, vertOctets, _vaultGen));
            ibo = _device.BuildBuf(DepictVault(BufferFlavor.Indices, ordinalOctets, _vaultGen));

            GpuMemoryLedger.FollowAssetAlloc(GpuAssetKind.Buffer);
            GpuMemoryLedger.FollowAlloc(vertOctets, GpuAssetKind.Buffer);
            vertFollowed = true;
            GpuMemoryLedger.FollowAssetAlloc(GpuAssetKind.Buffer);
            GpuMemoryLedger.FollowAlloc(ordinalOctets, GpuAssetKind.Buffer);
            ordinalFollowed = true;

            VertVault = vbo;
            OrdinalVault = ibo;
        }
        catch
        {
            ibo?.Dispose();
            vbo?.Dispose();
            if (ordinalFollowed)
            {
                GpuMemoryLedger.FollowDeallocation(ordinalOctets, GpuAssetKind.Buffer);
                GpuMemoryLedger.FollowAssetDeallocation(GpuAssetKind.Buffer);
            }
            if (vertFollowed)
            {
                GpuMemoryLedger.FollowDeallocation(vertOctets, GpuAssetKind.Buffer);
                GpuMemoryLedger.FollowAssetDeallocation(GpuAssetKind.Buffer);
            }
            throw;
        }
    }

    private void CancelMigration(BufferMove migration)
    {
        if (!ReferenceEquals(_migration, migration))
            return;
        _migrationCancel ??= new GlobalTriMeshMoveCancelTicket(
            migration.NewBuffer,
            migration.NewCapacityBytes,
            BuildRetryableVaultDeletion(
                migration.NewBuffer,
                migration.NewCapacityBytes,
                $"aborting staged global {migration.Kind} arena buffer '{migration.NewBuffer.Name}'"));
        ReattemptQueuedMigrationCancel();
    }

    private TriMeshBufferSpan ReserveVerts(int tally)
    {
        return _verts.TryAllocate(tally, out TriMeshBufferSpan alloc)
            ? alloc
            : throw new InvalidOperationException(
            "Vertex capacity wasn't migrated prior to the staged mesh upload was admitted");
    }

    private TriMeshBufferSpan ReserveOrdinals(int tally)
    {
        return _ordinals.TryAllocate(tally, out TriMeshBufferSpan alloc)
            ? alloc
            : throw new InvalidOperationException(
            "Index capacity wasn't migrated prior to the staged mesh upload was admitted");
    }

    private void CommenceMigration(BufferFlavor sort, int newCap, long duplicateOctets)
    {
        if (_migration is not null || _migrationCancel is not null)
            throw new InvalidOperationException("Only one global mesh backing buffer may migrate at a time");
        int formerCap = sort == BufferFlavor.Vertices ? _verts.Capacity : _ordinals.Capacity;
        IClientGpuBuffer formerBuf = DemandVault(
            sort == BufferFlavor.Vertices ? VertVault : OrdinalVault);
        long formerOctets = CapOctetsFor(sort, formerCap);
        long newOctets = CapOctetsFor(sort, newCap);

        IClientGpuBuffer newBuf = _device.BuildBuf(
            DepictVault(sort, newOctets, checked(++_vaultGen)));

        GpuMemoryLedger.FollowAssetAlloc(GpuAssetKind.Buffer);
        GpuMemoryLedger.FollowAlloc(newOctets, GpuAssetKind.Buffer);
        _migration = new BufferMove(
            sort,
            formerBuf,
            newBuf,
            formerCap,
            newCap,
            formerOctets,
            newOctets,
            duplicateOctets);
    }

    private void SealMigration(BufferMove migration)
    {
        if (migration.Kind == BufferFlavor.Vertices)
        {
            VertVault = migration.NewBuffer;
            if (migration.NewCapacity > migration.OldCapacity)
                _verts.Enlarge(migration.NewCapacity);
            else
                _verts.Trim(migration.NewCapacity);
        }
        else
        {
            OrdinalVault = migration.NewBuffer;
            if (migration.NewCapacity > migration.OldCapacity)
                _ordinals.Enlarge(migration.NewCapacity);
            else
                _ordinals.Trim(migration.NewCapacity);
        }

        _migration = null;
        RetiredBackingOctets = checked(RetiredBackingOctets + migration.OldCapacityBytes);
        var formerBufFree =
            BuildRetryableVaultDeletion(
                migration.OldBuffer,
                migration.OldCapacityBytes,
                $"retiring replaced global {migration.Kind} arena buffer '{migration.OldBuffer.Name}'");
        _sunsetRegister.Retire(new RetryableGpuAssetFree(
            formerBufFree.Run,
            () => RetiredBackingOctets = checked(
                RetiredBackingOctets - migration.OldCapacityBytes)));
    }

    private RetryableGpuAssetFree BuildRetryableVaultDeletion(
        IClientGpuBuffer buf,
        long capOctets,
        string ctx)
    {
        ArgumentNullException.ThrowIfNull(buf);
        ArgumentOutOfRangeException.ThrowIfNegative(capOctets);
        return new RetryableGpuAssetFree(
            () => { },
            buf.Dispose,
            () =>
            {
                if (capOctets is not 0)
                    GpuMemoryLedger.FollowDeallocation(capOctets, GpuAssetKind.Buffer);
            },
            () => GpuMemoryLedger.FollowAssetDeallocation(GpuAssetKind.Buffer));
    }

    private void ReattemptQueuedMigrationCancel()
    {
        var ticket = _migrationCancel;
        if (ticket is null)
            return;

        try
        {
            ticket.Advance();
        }
        finally
        {
            if (ticket.IsDone)
            {
                BufferMove migration = _migration
                    ?? throw new InvalidOperationException(
                        "A staged-buffer abort ticket outlived its migration record");
                if (!ReferenceEquals(migration.NewBuffer, ticket.Buffer)
                    || migration.NewCapacityBytes != ticket.CapOctets)
                {
                    throw new InvalidOperationException(
                        "A staged-buffer abort ticket no longer matches its migration record");
                }

                _migrationCancel = null;
                _migration = null;
            }
        }
    }

    private static int RoundUpToThreshold(long val, int quantum, int ceiling)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(val);
        long remainder = val % quantum;
        long rounded = remainder is 0 ? val : checked(val + quantum - remainder);
        if (rounded > ceiling)
            rounded = ceiling;
        return checked((int)rounded);
    }
}
