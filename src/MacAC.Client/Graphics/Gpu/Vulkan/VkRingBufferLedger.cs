namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkRingBufferLedger
{
    internal VkRingBufferLedger(ulong capOctets)
    {
        ArgumentOutOfRangeException.ThrowIfZero(capOctets);
        CapOctets = capOctets;
    }

    internal ulong CapOctets { get; }

    internal ulong AllocatedBytes { get; private set; }

    internal ulong PeakAllocatedOctets { get; private set; }

    internal ulong Reserve(int byteTally, ulong alignmentOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteTally);
        ulong aligned = VkMemoryBlockFreeList.LineUp(AllocatedBytes, alignmentOctets);
        ulong finish = aligned + (ulong)byteTally;
        if (finish > CapOctets)
        {
            throw new InvalidOperationException(
                $"Ring allocation of {byteTally} bytes at aligned offset {aligned} needs {finish} bytes; " +
                $"this flight slot's ring is {CapOctets} bytes. Increase the per-slot ring " +
                "capacity (ClientVulkanGpuDevice's ringCapacityBytesPerSlot)");
        }

        AllocatedBytes = finish;
        PeakAllocatedOctets = Math.Max(PeakAllocatedOctets, finish);
        return aligned;
    }

    internal void Reset() => AllocatedBytes = 0;
}

internal sealed class VkStagingRingLedger
{
    private readonly List<QueuedPiece> _queued = [];
    private ulong _front;
    private ulong _rear;

    private readonly record struct QueuedPiece(long Serial, ulong SizeBytes);

    internal VkStagingRingLedger(ulong capOctets)
    {
        ArgumentOutOfRangeException.ThrowIfZero(capOctets);
        CapOctets = capOctets;
    }

    internal ulong CapOctets { get; }

    // Bytes reserved by frames that have not yet retired
    internal ulong OnlineOctets { get; private set; }

    internal int QueuedSegmentTally => _queued.Count;

    internal bool TryReserve(int byteTally, ulong alignmentOctets, long serialNo, out ulong shiftOctets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteTally);
        shiftOctets = 0;
        if (byteTally is 0)
            return true;

        ulong dims = (ulong)byteTally;
        if (dims > CapOctets)
            return false;

        ulong aligned = VkMemoryBlockFreeList.LineUp(_front, alignmentOctets);
        ulong consumed;
        if (aligned + dims > CapOctets)
        {
            ulong wasted = CapOctets - _front;
            aligned = 0;
            consumed = wasted + dims;
        }
        else
        {
            consumed = (aligned - _front) + dims;
        }

        if (OnlineOctets + consumed > CapOctets)
            return false;

        _front = (aligned + dims) % CapOctets;
        OnlineOctets += consumed;
        shiftOctets = aligned;

        if (_queued.Count > 0 && _queued[^1].Serial == serialNo)
        {
            var previous = _queued[^1];
            _queued[^1] = previous with { SizeBytes = previous.SizeBytes + consumed };
        }
        else
        {
            _queued.Add(new QueuedPiece(serialNo, consumed));
        }

        return true;
    }

    internal void Release(long finishedSerialNo)
    {
        int released = 0;
        while (released < _queued.Count && _queued[released].Serial <= finishedSerialNo)
        {
            _rear = (_rear + _queued[released].SizeBytes) % CapOctets;
            OnlineOctets -= Math.Min(OnlineOctets, _queued[released].SizeBytes);
            ++released;
        }

        if (released > 0)
            _queued.RemoveRange(0, released);
    }

    // Drops all bookkeeping
    internal void Reset()
    {
        _queued.Clear();
        _front = 0;
        _rear = 0;
        OnlineOctets = 0;
    }
}
