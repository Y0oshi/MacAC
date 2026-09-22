using MacAC.Mechanics.Landscape;

namespace MacAC.Client.Graphics;

internal sealed class GpuRetiredLandscapeSlotAllotter(
    int startingCap,
    IGpuAssetSunsetFifo retirement)
{
    private readonly TerrainSlotPool _allocator = new TerrainSlotPool(startingCap);
    private readonly GpuSunsetRegister _sunsetRegister = new GpuSunsetRegister(
            retirement ?? throw new ArgumentNullException(nameof(retirement)));
    private readonly Dictionary<int, RetryableGpuAssetFree> _queuedReleases = [];

    public int Capacity => _allocator.Capacity;
    public int FetchedTally => _allocator.FetchedCount;
    internal int QueuedFreeTally => _queuedReleases.Count;

    public int Reserve(out bool needsExpand) => _allocator.Carve(out needsExpand);

    public void ExpandTo(int newCap) => _allocator.EnlargeTo(newCap);

    public void RelinquishUnsubmitted(int socket)
    {
        if (_queuedReleases.ContainsKey(socket))
        {
            throw new InvalidOperationException(
                "A GPU-submitted terrain slot can't be released as unsubmitted");
        }

        _allocator.Unpin(socket);
    }

    public void ReleaseFollowingGpuUse(int socket)
    {
        if (_queuedReleases.TryGetValue(socket, out RetryableGpuAssetFree? queued))
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

        var free = new RetryableGpuAssetFree(
            () => _allocator.Unpin(socket),
            () =>
            {
                if (!_queuedReleases.Remove(socket))
                {
                    throw new InvalidOperationException(
                        "Terrain-slot retirement lost its ownership record");
                }
            });
        _queuedReleases.Add(socket, free);

        try
        {
            _sunsetRegister.Retire(free);
        }
        catch when (free.IsComplete)
        {
        }
    }

    public void RetryQueuedPublications() =>
        _sunsetRegister.ReattemptPendingPublications();
}
