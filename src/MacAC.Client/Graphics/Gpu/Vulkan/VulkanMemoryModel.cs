using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal readonly record struct VkMemoryRange(
    int BlockIndex,
    ulong OffsetBytes,
    ulong SizeBytes,
    bool IsDedicated);

internal sealed class VkMemoryBlockFreeList
{
    private readonly List<ClientRange> _release = [];

    private struct ClientRange(ulong shift, ulong dims)
    {
        public ulong Offset = shift;
        public ulong Size = dims;
        public readonly ulong End => Offset + Size;
    }

    internal VkMemoryBlockFreeList(ulong capacityBytes)
    {
        if (capacityBytes is 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes), "A memory block can't be empty");
        CapOctets = capacityBytes;
        _release.Add(new ClientRange(0, capacityBytes));
    }

    internal ulong CapOctets { get; }

    internal ulong ConsumedOctets { get; private set; }

    internal ulong ReleaseOctets => CapOctets - ConsumedOctets;

    internal ulong LargestSpareOctets
    {
        get
        {
            ulong largest = 0;
            foreach (ClientRange span in _release)
                largest = Math.Max(largest, span.Size);
            return largest;
        }
    }

    internal int ReleaseSpanTally => _release.Count;

    internal bool TryReserve(ulong byteSize, ulong alignmentOctets, out ulong shiftOctets)
    {
        shiftOctets = 0;
        if (byteSize is 0)
            return false;

        for (int idx = 0; idx < _release.Count; ++idx)
        {
            ClientRange span = _release[idx];
            ulong aligned = LineUp(span.Offset, alignmentOctets);
            ulong padding = aligned - span.Offset;
            if (padding > span.Size || span.Size - padding < byteSize)
                continue;

            ulong consumed = padding + byteSize;
            if (consumed == span.Size)
            {
                _release.RemoveAt(idx);
            }
            else
            {
                span.Offset += consumed;
                span.Size -= consumed;
                _release[idx] = span;
            }

            ConsumedOctets += consumed;
            shiftOctets = aligned;
            AllocatedPaddingByShift[aligned] = padding;
            return true;
        }

        return false;
    }

    private Dictionary<ulong, ulong> AllocatedPaddingByShift { get; } = [];

    internal void Release(ulong shiftOctets, ulong byteSize)
    {
        if (byteSize is 0)
            return;

        ulong padding = 0;
        if (AllocatedPaddingByShift.Remove(shiftOctets, out ulong recorded))
            padding = recorded;

        ulong begin = shiftOctets - padding;
        ulong finish = shiftOctets + byteSize;
        ulong len = finish - begin;
        if (len > ConsumedOctets)
        {
            throw new InvalidOperationException(
                $"Releasing [{begin}, {finish}) would free more than the {ConsumedOctets} bytes this block " +
                "has handed out - the same range was probably released twice");
        }

        ConsumedOctets -= len;

        int slotAt = _release.FindIndex(span => span.Offset > begin);
        if (slotAt < 0)
            slotAt = _release.Count;
        _release.Insert(slotAt, new ClientRange(begin, len));
        Coalesce(slotAt);
    }

    internal static ulong LineUp(ulong val, ulong alignmentOctets)
    {
        return alignmentOctets <= 1 ? val : (val + alignmentOctets - 1) / alignmentOctets * alignmentOctets;
    }

    private void Coalesce(int ordinal)
    {
        if (ordinal > 0 && _release[ordinal - 1].End == _release[ordinal].Offset)
        {
            ClientRange merged = _release[ordinal - 1];
            merged.Size += _release[ordinal].Size;
            _release[ordinal - 1] = merged;
            _release.RemoveAt(ordinal);
            --ordinal;
        }

        if (ordinal + 1 < _release.Count && _release[ordinal].End == _release[ordinal + 1].Offset)
        {
            ClientRange merged = _release[ordinal];
            merged.Size += _release[ordinal + 1].Size;
            _release[ordinal] = merged;
            _release.RemoveAt(ordinal + 1);
        }
    }
}

internal sealed class VkMemoryTypePool
{
    // Plan §4.2's block size: 128 MiB device-local blocks per memory type
    internal const ulong DefaultChunkByteSize = 128UL * 1024 * 1024;

    // Plan §4.2: allocations at or above this size take a block of their own
    internal const ulong DefaultDedicatedThresholdOctets = 32UL * 1024 * 1024;

    private readonly List<VkMemoryBlockFreeList?> _chunks = [];
    private readonly HashSet<int> _dedicatedChunks = [];

    internal VkMemoryTypePool(
        uint memoryKindOrdinal,
        ulong chunkByteSize = DefaultChunkByteSize,
        ulong dedicatedThresholdOctets = DefaultDedicatedThresholdOctets)
    {
        ArgumentOutOfRangeException.ThrowIfZero(chunkByteSize);
        ArgumentOutOfRangeException.ThrowIfZero(dedicatedThresholdOctets);
        MemoryKindOrdinal = memoryKindOrdinal;
        ChunkByteSize = chunkByteSize;
        DedicatedThresholdOctets = dedicatedThresholdOctets;
    }

    internal uint MemoryKindOrdinal { get; }

    internal ulong ChunkByteSize { get; }

    internal ulong DedicatedThresholdOctets { get; }

    internal int ChunkTally => _chunks.Count;

    // Blocks that still exist, i.e. have been added and not retired.
    internal int OnlineChunkTally => _chunks.Count(chunk => chunk is not null);

    internal bool IsDedicatedDims(ulong byteSize) => byteSize >= DedicatedThresholdOctets;

    internal ulong ChunkCapFor(ulong byteSize) =>
        IsDedicatedDims(byteSize) ? byteSize : ChunkByteSize;

    internal bool TryReserve(ulong byteSize, ulong alignmentOctets, out VkMemoryRange span)
    {
        span = default;
        if (byteSize is 0)
            return false;

        if (IsDedicatedDims(byteSize))
            return false;

        for (int idx = 0; idx < _chunks.Count; ++idx)
        {
            if (_chunks[idx] is not { } chunk || _dedicatedChunks.Contains(idx))
                continue;
            if (chunk.TryReserve(byteSize, alignmentOctets, out ulong shift))
            {
                span = new VkMemoryRange(idx, shift, byteSize, IsDedicated: false);
                return true;
            }
        }

        return false;
    }

    internal int AppendChunk(ulong capOctets, bool dedicated)
    {
        _chunks.Add(new VkMemoryBlockFreeList(capOctets));
        int ordinal = _chunks.Count - 1;
        if (dedicated)
            _dedicatedChunks.Add(ordinal);
        return ordinal;
    }

    internal VkMemoryRange ReserveWholeChunk(int chunkOrdinal, ulong byteSize)
    {
        var chunk = ChunkAt(chunkOrdinal);
        return !chunk.TryReserve(byteSize, 1, out ulong shift) || shift is not 0
            ? throw new InvalidOperationException(
                $"Block {chunkOrdinal} was created for a dedicated {byteSize}-byte allocation " +
                "but could not satisfy it at offset 0")
            : new VkMemoryRange(chunkOrdinal, 0, byteSize, IsDedicated: true);
    }

    internal bool Release(in VkMemoryRange span)
    {
        var chunk = ChunkAt(span.BlockIndex);
        chunk.Release(span.OffsetBytes, span.SizeBytes);
        if (chunk.ConsumedOctets is not 0 || !_dedicatedChunks.Contains(span.BlockIndex))
            return false;

        _chunks[span.BlockIndex] = null;
        _dedicatedChunks.Remove(span.BlockIndex);
        return true;
    }

    internal ulong ConsumedOctets
    {
        get
        {
            ulong sum = 0;
            foreach (VkMemoryBlockFreeList? chunk in _chunks)
                sum += chunk?.ConsumedOctets ?? 0;
            return sum;
        }
    }

    internal ulong CapOctets
    {
        get
        {
            ulong sum = 0;
            foreach (VkMemoryBlockFreeList? chunk in _chunks)
                sum += chunk?.CapOctets ?? 0;
            return sum;
        }
    }

    private VkMemoryBlockFreeList ChunkAt(int index)
    {
        return index >= 0 && index < _chunks.Count && _chunks[index] is { } chunk
            ? chunk
            : throw new ArgumentOutOfRangeException(
                nameof(index),
                $"Memory block {index} doesn't exist in the pool for memory type {MemoryKindOrdinal}.");
    }
}

internal static class VkMemoryTypePicking
{
    internal static IReadOnlyList<MemoryPropertyFlags> PreferenceOrdering(GpuMemoryTenancy residency)
    {
        return residency switch
        {
            GpuMemoryTenancy.DeviceLocal =>
            [
                MemoryPropertyFlags.DeviceLocalBit,
                0,
            ],
            GpuMemoryTenancy.HostWritable =>
            [
                MemoryPropertyFlags.DeviceLocalBit
                    | MemoryPropertyFlags.HostVisibleBit
                    | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.HostVisibleBit,
            ],
            GpuMemoryTenancy.HostReadable =>
            [
                MemoryPropertyFlags.HostVisibleBit
                    | MemoryPropertyFlags.HostCoherentBit
                    | MemoryPropertyFlags.HostCachedBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                MemoryPropertyFlags.HostVisibleBit,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(residency), residency, "Unrecognized residency class"),
        };
    }

    internal static bool RequiresMapping(GpuMemoryTenancy residency)
    {
        return residency is GpuMemoryTenancy.HostWritable or GpuMemoryTenancy.HostReadable;
    }

    internal static IReadOnlyList<uint> SelectAll(
        IReadOnlyList<MemoryPropertyFlags> memoryKindProps,
        uint allowedKindBitset,
        GpuMemoryTenancy residency)
    {
        ArgumentNullException.ThrowIfNull(memoryKindProps);

        List<uint> contenders = [];
        foreach (MemoryPropertyFlags needed in PreferenceOrdering(residency))
        {
            for (int idx = 0; idx < memoryKindProps.Count && idx < 32; ++idx)
            {
                if ((allowedKindBitset & (1u << idx)) is 0)
                    continue;
                if ((memoryKindProps[idx] & needed) != needed)
                    continue;
                if (!contenders.Contains((uint)idx))
                    contenders.Add((uint)idx);
            }
        }

        return contenders;
    }

    internal static uint? Choose(
        IReadOnlyList<MemoryPropertyFlags> memoryKindProps,
        uint allowedKindBitset,
        GpuMemoryTenancy residency)
    {
        var contenders = SelectAll(memoryKindProps, allowedKindBitset, residency);
        return contenders.Count is 0 ? null : contenders[0];
    }
}
