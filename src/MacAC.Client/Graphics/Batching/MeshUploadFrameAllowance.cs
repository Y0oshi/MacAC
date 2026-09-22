namespace MacAC.Client.Graphics.Batching;

internal readonly record struct TriMeshPushPrice(
    long SourceBytes,
    long ArrayAllocationBytes,
    long MipmapBytes,
    int NewArrayCount,
    long BufferUploadBytes = 0,
    long BufferAllocationBytes = 0,
    long BufferCopyBytes = 0,
    int NewBufferCount = 0,
    ulong AdmissionKey = 0);

internal readonly record struct MeshUploadAllowanceLimits(
    int MaximumObjects,
    long MaximumSourceBytes,
    long MaximumArrayAllocationBytes,
    long MaximumMipmapBytes,
    int MaximumNewArrays,
    long MaximumBufferUploadBytes = long.MaxValue,
    long MaximumBufferAllocationBytes = long.MaxValue,
    long MaximumBufferCopyBytes = long.MaxValue,
    int MaximumNewBuffers = int.MaxValue,
    long MaximumSingleSourceBytes = long.MaxValue,
    long MaximumSingleArrayAllocationBytes = long.MaxValue,
    long MaximumSingleMipmapBytes = long.MaxValue,
    int MaximumSingleNewArrays = int.MaxValue,
    long MaximumSingleBufferUploadBytes = long.MaxValue);

internal sealed class MeshUploadFrameAllowance
{
    private readonly MeshUploadAllowanceLimits _thresholds;

    public MeshUploadFrameAllowance(MeshUploadAllowanceLimits thresholds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumObjects, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSourceBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumArrayAllocationBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumMipmapBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumNewArrays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumBufferUploadBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumBufferAllocationBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumBufferCopyBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumNewBuffers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSingleSourceBytes, thresholds.MaximumSourceBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSingleArrayAllocationBytes, thresholds.MaximumArrayAllocationBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSingleMipmapBytes, thresholds.MaximumMipmapBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSingleNewArrays, thresholds.MaximumNewArrays);
        ArgumentOutOfRangeException.ThrowIfLessThan(thresholds.MaximumSingleBufferUploadBytes, thresholds.MaximumBufferUploadBytes);
        _thresholds = thresholds;
    }

    public int ObjectTally { get; private set; }
    public long SrcOctets { get; private set; }
    public long ArrAllocOctets { get; private set; }
    public long MipmapOctets { get; private set; }
    public int NewArrTally { get; private set; }
    public long BufPushOctets { get; private set; }
    public long BufAllocOctets { get; private set; }
    public long BufDuplicateOctets { get; private set; }
    public int NewBufTally { get; private set; }
    public void Reset()
    {
        ObjectTally = 0;
        SrcOctets = 0;
        ArrAllocOctets = 0;
        MipmapOctets = 0;
        NewArrTally = 0;
        BufPushOctets = 0;
        BufAllocOctets = 0;
        BufDuplicateOctets = 0;
        NewBufTally = 0;
    }

    public bool TryAdmit(TriMeshPushPrice price)
    {
        Validate(price);
        if (price.BufferAllocationBytes is not 0
            || price.BufferCopyBytes is not 0
            || price.NewBufferCount is not 0)
        {
            throw new InvalidOperationException(
                "Global-buffer allocation/copy work has to be progressed as real maintenance prior to upload admission");
        }

        bool oversizedFront = ObjectTally is 0 && ExceedsSingleCycle(price);
        if (oversizedFront)
        {
            if (price.SourceBytes > _thresholds.MaximumSingleSourceBytes
                || price.ArrayAllocationBytes > _thresholds.MaximumSingleArrayAllocationBytes
                || price.MipmapBytes > _thresholds.MaximumSingleMipmapBytes
                || price.NewArrayCount > _thresholds.MaximumSingleNewArrays
                || price.BufferUploadBytes > _thresholds.MaximumSingleBufferUploadBytes)
            {
                throw new NotSupportedException(
                    $"Mesh upload generation {price.AdmissionKey} exceeds the supported single-operation ceiling");
            }

            Admit(price);
            return true;
        }

        if (ObjectTally >= _thresholds.MaximumObjects
                || WouldExceed(SrcOctets, price.SourceBytes, _thresholds.MaximumSourceBytes)
                || WouldExceed(ArrAllocOctets, price.ArrayAllocationBytes, _thresholds.MaximumArrayAllocationBytes)
                || WouldExceed(MipmapOctets, price.MipmapBytes, _thresholds.MaximumMipmapBytes)
                || price.NewArrayCount > _thresholds.MaximumNewArrays - NewArrTally
                || WouldExceed(BufPushOctets, price.BufferUploadBytes, _thresholds.MaximumBufferUploadBytes)
                || WouldExceed(BufAllocOctets, price.BufferAllocationBytes, _thresholds.MaximumBufferAllocationBytes)
                || WouldExceed(BufDuplicateOctets, price.BufferCopyBytes, _thresholds.MaximumBufferCopyBytes)
                || price.NewBufferCount > _thresholds.MaximumNewBuffers - NewBufTally)

            return false;

        Admit(price);
        return true;
    }

    // Records work that actually executed this frame
    public void CaptureBufMaintenance(long allocOctets, long duplicateOctets, int newBufTally)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(allocOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(duplicateOctets);
        ArgumentOutOfRangeException.ThrowIfNegative(newBufTally);
        if (WouldExceed(BufAllocOctets, allocOctets, _thresholds.MaximumBufferAllocationBytes)
            || WouldExceed(BufDuplicateOctets, duplicateOctets, _thresholds.MaximumBufferCopyBytes)
            || newBufTally > _thresholds.MaximumNewBuffers - NewBufTally)
        {
            throw new InvalidOperationException(
                "Executed global-buffer maintenance exceeded its real per-frame bound");
        }
        BufAllocOctets = checked(BufAllocOctets + allocOctets);
        BufDuplicateOctets = checked(BufDuplicateOctets + duplicateOctets);
        NewBufTally = checked(NewBufTally + newBufTally);
    }

    private bool ExceedsSingleCycle(TriMeshPushPrice price)
    {
        return price.SourceBytes > _thresholds.MaximumSourceBytes
        || price.ArrayAllocationBytes > _thresholds.MaximumArrayAllocationBytes
        || price.MipmapBytes > _thresholds.MaximumMipmapBytes
        || price.NewArrayCount > _thresholds.MaximumNewArrays
        || price.BufferUploadBytes > _thresholds.MaximumBufferUploadBytes
        || price.BufferAllocationBytes > _thresholds.MaximumBufferAllocationBytes
        || price.BufferCopyBytes > _thresholds.MaximumBufferCopyBytes
        || price.NewBufferCount > _thresholds.MaximumNewBuffers;
    }

    private void Admit(TriMeshPushPrice price)
    {
        ++ObjectTally;
        SrcOctets = checked(SrcOctets + price.SourceBytes);
        ArrAllocOctets = checked(ArrAllocOctets + price.ArrayAllocationBytes);
        MipmapOctets = checked(MipmapOctets + price.MipmapBytes);
        NewArrTally = checked(NewArrTally + price.NewArrayCount);
        BufPushOctets = checked(BufPushOctets + price.BufferUploadBytes);
        BufAllocOctets = checked(BufAllocOctets + price.BufferAllocationBytes);
        BufDuplicateOctets = checked(BufDuplicateOctets + price.BufferCopyBytes);
        NewBufTally = checked(NewBufTally + price.NewBufferCount);
    }

    private static bool WouldExceed(long latest, long added, long ceiling) =>
        added > ceiling - Math.Min(latest, ceiling);

    private static void Validate(TriMeshPushPrice price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price.SourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.ArrayAllocationBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.MipmapBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.NewArrayCount);
        ValidateChecks(price);
    }

    private static void ValidateChecks(TriMeshPushPrice price)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(price.BufferUploadBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.BufferAllocationBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.BufferCopyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(price.NewBufferCount);
    }
}
