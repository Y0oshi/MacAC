namespace MacAC.Client.Graphics.Batching;

internal sealed class GpuRetiredRangeAllotter(int cap, IGpuAssetSunsetFifo retirement)
{
    private readonly ContiguousRangeAllotter _allocator = new ContiguousRangeAllotter(cap);
    private readonly GpuSunsetRegister _sunsetRegister = new GpuSunsetRegister(
            retirement ?? throw new ArgumentNullException(nameof(retirement)));
    private readonly Dictionary<TriMeshBufferSpan, RetryableGpuAssetFree> _queuedReleases = [];

    public int Capacity => _allocator.Capacity;
    public int Used => _allocator.Used;
    public int HiWaterMark => _allocator.HiWaterFlag;
    public int LargestSpareRange => _allocator.LargestSpareSpan;
    public int TrailingSpareLength => _allocator.TrailingSpareLen;
    public int QueuedFreeTally { get; private set; }
    public int QueuedFreeLen { get; private set; }

    public bool TryAllocate(int len, out TriMeshBufferSpan alloc) =>
        _allocator.TryReserve(len, out alloc);

    public void Enlarge(int newCap) => _allocator.Expand(newCap);

    public void Trim(int newCap) => _allocator.Contract(newCap);

    public void FreeFollowingGpuUse(TriMeshBufferSpan alloc)
    {
        if (_queuedReleases.TryGetValue(alloc, out RetryableGpuAssetFree? queued))
        {
            try
            {
                _sunsetRegister.ReattemptQueuedBulletin(queued);
            }
            catch when (queued.IsComplete)
            {
            }
            return;
        }

        int upcomingQueuedTally = checked(QueuedFreeTally + 1);
        int upcomingQueuedLen = checked(QueuedFreeLen + alloc.Length);
        var free = new RetryableGpuAssetFree(
            () => _allocator.Release(alloc),
            () => QueuedFreeTally = checked(QueuedFreeTally - 1),
            () => QueuedFreeLen = checked(
                QueuedFreeLen - alloc.Length),
            () =>
            {
                if (!_queuedReleases.Remove(alloc))
                {
                    throw new InvalidOperationException(
                        "GPU buffer range retirement lost its ownership record");
                }
            });
        _queuedReleases.Add(alloc, free);
        QueuedFreeTally = upcomingQueuedTally;
        QueuedFreeLen = upcomingQueuedLen;

        try
        {
            _sunsetRegister.Retire(free);
        }
        catch when (free.IsComplete)
        {
        }
    }

    public void FreeUnsubmitted(TriMeshBufferSpan alloc)
    {
        if (_queuedReleases.ContainsKey(alloc))
        {
            throw new InvalidOperationException(
                "A GPU-submitted range can't be released as unsubmitted");
        }

        _allocator.Release(alloc);
    }
}
