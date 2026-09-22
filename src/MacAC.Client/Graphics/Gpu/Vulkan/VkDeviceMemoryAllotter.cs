using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal readonly unsafe struct VkAllocation(
    DeviceMemory memory,
    ulong shiftOctets,
    ulong byteSize,
    uint memoryKindOrdinal,
    MemoryPropertyFlags memoryProps,
    VkMemoryRange span,
    void* mapped)
{
    internal DeviceMemory Memory { get; } = memory;
    internal ulong ShiftOctets { get; } = shiftOctets;
    internal ulong SizeBytes { get; } = byteSize;
    internal uint MemoryKindOrdinal { get; } = memoryKindOrdinal;
    internal MemoryPropertyFlags MemoryProps { get; } = memoryProps;
    internal VkMemoryRange Range { get; } = span;

    // First mapped byte of this allocation, or null on device-local memory
    internal void* Mapped { get; } = mapped;

    internal bool IsMapped => Mapped is not null;

    internal Span<byte> AsSpan()
    {
        return IsMapped
        ? new Span<byte>(Mapped, checked((int)SizeBytes))
        : throw new InvalidOperationException(
            "This allocation lives in device-local memory and has no CPU mapping");
    }
}

internal interface IVkDeviceMemoryBackend
{
    Result ReserveMemory(uint kindOrdinal, ulong byteSize, out DeviceMemory memory);

    Result ChartMemory(DeviceMemory memory, ulong byteSize, out nint ptr);

    void UnmapMemory(DeviceMemory memory);

    void ReleaseMemory(DeviceMemory memory);
}

// The real backend: the same four Vulkan entry points, unchanged
internal sealed unsafe class ClientVkDeviceMemoryBackend(Silk.NET.Vulkan.Vk vk, Device dev)
    : IVkDeviceMemoryBackend
{
    private readonly Silk.NET.Vulkan.Vk _vk = vk ?? throw new ArgumentNullException(nameof(vk));
    private readonly Device _device = dev;

    public Result ReserveMemory(uint kindOrdinal, ulong byteSize, out DeviceMemory memory)
    {
        MemoryAllocateInfo reserve = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = byteSize,
            MemoryTypeIndex = kindOrdinal,
        };
        return _vk.AllocateMemory(_device, &reserve, null, out memory);
    }

    public Result ChartMemory(DeviceMemory memory, ulong byteSize, out nint ptr)
    {
        void* val = null;
        Result outcome = _vk.MapMemory(_device, memory, 0, byteSize, 0, &val);
        ptr = (nint)val;
        return outcome;
    }

    public void UnmapMemory(DeviceMemory memory) => _vk.UnmapMemory(_device, memory);

    public void ReleaseMemory(DeviceMemory memory) => _vk.FreeMemory(_device, memory, null);
}

internal sealed unsafe class VkDeviceMemoryAllotter : IDisposable
{
    private readonly IVkDeviceMemoryBackend _backend;
    private readonly MemoryPropertyFlags[] _memoryKindProps;
    private readonly ulong _chunkByteSize;
    private readonly ulong _dedicatedThresholdOctets;
    private readonly object _synchronize = new();

    private readonly Dictionary<uint, VkMemoryTypePool> _reservoirs = [];
    private readonly Dictionary<(uint TypeIndex, int BlockIndex), ChunkMemory> _chunkMemory = [];

    private readonly HashSet<uint> _exhaustedKinds = [];

    private bool _destroyed;

    private readonly record struct ChunkMemory(DeviceMemory Memory, nint Mapped, ulong CapacityBytes);

    internal VkDeviceMemoryAllotter(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        ulong chunkByteSize = VkMemoryTypePool.DefaultChunkByteSize,
        ulong dedicatedThresholdOctets = VkMemoryTypePool.DefaultDedicatedThresholdOctets)
        : this(
            ScanMemoryKindProps(vk ?? throw new ArgumentNullException(nameof(vk)), physicalDev),
            new ClientVkDeviceMemoryBackend(vk, dev),
            chunkByteSize,
            dedicatedThresholdOctets)
    {
    }

    internal VkDeviceMemoryAllotter(
        IVkDeviceMemoryBackend backend,
        IReadOnlyList<MemoryPropertyFlags> memoryTypeProperties,
        ulong chunkByteSize = VkMemoryTypePool.DefaultChunkByteSize,
        ulong dedicatedThresholdOctets = VkMemoryTypePool.DefaultDedicatedThresholdOctets)
        : this(
            [.. memoryTypeProperties ?? throw new ArgumentNullException(nameof(memoryTypeProperties))],
            backend ?? throw new ArgumentNullException(nameof(backend)),
            chunkByteSize,
            dedicatedThresholdOctets)
    {
    }

    private VkDeviceMemoryAllotter(
        MemoryPropertyFlags[] memoryKindProps,
        IVkDeviceMemoryBackend backend,
        ulong chunkByteSize,
        ulong dedicatedThresholdOctets)
    {
        _memoryKindProps = memoryKindProps;
        _backend = backend;
        _chunkByteSize = chunkByteSize;
        _dedicatedThresholdOctets = dedicatedThresholdOctets;
    }

    public void Dispose()
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;

            foreach (ChunkMemory chunk in _chunkMemory.Values)
            {
                if (chunk.Mapped != 0)
                    _backend.UnmapMemory(chunk.Memory);
                _backend.ReleaseMemory(chunk.Memory);
            }

            _chunkMemory.Clear();
            _reservoirs.Clear();
            _exhaustedKinds.Clear();
            AllocatedBytes = 0;
            SealedOctets = 0;
        }
    }

    // Live vkAllocateMemory objects
    internal int DevMemoryObjectTally => _chunkMemory.Count;

    // Blocks the pools still hold, across every memory type
    internal int OnlineChunkTally
    {
        get
        {
            lock (_synchronize)
            {
                int sum = 0;
                foreach (VkMemoryTypePool reservoir in _reservoirs.Values)
                    sum += reservoir.OnlineChunkTally;
                return sum;
            }
        }
    }

    internal ulong AllocatedBytes { get; private set; }

    internal ulong SealedOctets { get; private set; }

    internal IReadOnlyList<MemoryPropertyFlags> MemoryKindProps => _memoryKindProps;

    internal VkAllocation Reserve(
        in MemoryRequirements requirements,
        GpuMemoryTenancy residency,
        string holderLabel)
    {
        lock (_synchronize)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);

            var contenders = VkMemoryTypePicking.SelectAll(
                _memoryKindProps,
                requirements.MemoryTypeBits,
                residency);
            if (contenders.Count is 0)
            {
                throw new NotSupportedException(
                    $"No Vulkan memory type satisfies {residency} for '{holderLabel}'. " +
                    $"Allowed type bits 0x{requirements.MemoryTypeBits:X8}; the device exposes " +
                    $"{_memoryKindProps.Length} memory types");
            }

            ulong dims = requirements.Size;
            ulong alignment = Math.Max(requirements.Alignment, 1);
            Result previousOutcome = Result.ErrorOutOfDeviceMemory;

            foreach (uint kindOrdinal in contenders)
            {
                if (_exhaustedKinds.Contains(kindOrdinal))
                    continue;

                if (!_reservoirs.TryGetValue(kindOrdinal, out VkMemoryTypePool? reservoir))
                {
                    reservoir = new VkMemoryTypePool(kindOrdinal, _chunkByteSize, _dedicatedThresholdOctets);
                    _reservoirs.Add(kindOrdinal, reservoir);
                }

                if (!reservoir.TryReserve(dims, alignment, out VkMemoryRange span))
                {
                    bool dedicated = reservoir.IsDedicatedDims(dims);
                    ulong cap = Math.Max(reservoir.ChunkCapFor(dims), dims);

                    Result built = TryBuildChunkMemory(
                        kindOrdinal,
                        cap,
                        holderLabel,
                        out ChunkMemory memory,
                        out string op);
                    if (built != Result.Success)
                    {
                        previousOutcome = built;
                        if (built is Result.ErrorOutOfDeviceMemory or Result.ErrorOutOfHostMemory)
                        {
                            _exhaustedKinds.Add(kindOrdinal);
                            continue;
                        }

                        throw new VkCallException(op, built);
                    }

                    int chunkOrdinal = reservoir.AppendChunk(cap, dedicated);
                    _chunkMemory[(kindOrdinal, chunkOrdinal)] = memory;
                    SealedOctets += cap;

                    span = dedicated
                        ? reservoir.ReserveWholeChunk(chunkOrdinal, dims)
                        : reservoir.TryReserve(dims, alignment, out VkMemoryRange placed)
                            ? placed
                            : throw new InvalidOperationException(
                                $"A freshly created {cap}-byte block could not satisfy a {dims}-byte " +
                                $"allocation at alignment {alignment} for '{holderLabel}'.");
                }

                ChunkMemory chunk = _chunkMemory[(kindOrdinal, span.BlockIndex)];
                AllocatedBytes += span.SizeBytes;
                void* mapped = chunk.Mapped == 0
                    ? null
                    : (void*)(chunk.Mapped + (nint)span.OffsetBytes);
                return new VkAllocation(
                    chunk.Memory,
                    span.OffsetBytes,
                    span.SizeBytes,
                    kindOrdinal,
                    _memoryKindProps[(int)kindOrdinal],
                    span,
                    mapped);
            }

            throw new VkCallException(
                $"vkAllocateMemory ({dims} bytes for '{holderLabel}', {residency}) - every candidate " +
                $"memory type [{string.Join(", ", contenders)}] refused it " +
                $"(exhausted: [{string.Join(", ", _exhaustedKinds.Order())}])",
                previousOutcome);
        }
    }

    // Returns an allocation's bytes to its pool, freeing the block when a dedicated one empties
    internal void Release(in VkAllocation alloc)
    {
        lock (_synchronize)
        {
            if (_destroyed || alloc.SizeBytes is 0)
                return;
            if (!_reservoirs.TryGetValue(alloc.MemoryKindOrdinal, out VkMemoryTypePool? reservoir))
                return;

            AllocatedBytes -= Math.Min(AllocatedBytes, alloc.Range.SizeBytes);
            if (!reservoir.Release(alloc.Range))
                return;

            var tag = (MemoryTypeIndex: alloc.MemoryKindOrdinal, alloc.Range.BlockIndex);
            if (!_chunkMemory.Remove(tag, out ChunkMemory chunk))
                return;

            if (chunk.Mapped != 0)
                _backend.UnmapMemory(chunk.Memory);
            _backend.ReleaseMemory(chunk.Memory);
            SealedOctets -= Math.Min(SealedOctets, chunk.CapacityBytes);
        }
    }

    // Human-readable accounting for the diagnostics report and for teardown assertions
    internal string Depict()
    {
        lock (_synchronize)
        {
            string blurb = $"{DevMemoryObjectTally} device-memory object(s), "
                + $"{SealedOctets / (1024 * 1024)} MiB committed, "
                + $"{AllocatedBytes / (1024 * 1024)} MiB allocated";
            if (_exhaustedKinds.Count > 0)
                blurb += $"; exhausted memory types: {string.Join(", ", _exhaustedKinds.Order())}";
            return blurb;
        }
    }

    private static MemoryPropertyFlags[] ScanMemoryKindProps(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev)
    {
        vk.GetPhysicalDeviceMemoryProperties(physicalDev, out PhysicalDeviceMemoryProperties props);
        MemoryPropertyFlags[] flagSet = new MemoryPropertyFlags[props.MemoryTypeCount];
        for (uint idx = 0; idx < props.MemoryTypeCount && idx < 32; ++idx)
            flagSet[idx] = props.MemoryTypes[(int)idx].PropertyFlags;
        return flagSet;
    }

    private Result TryBuildChunkMemory(
        uint kindOrdinal,
        ulong capOctets,
        string holderLabel,
        out ChunkMemory chunk,
        out string op)
    {
        chunk = default;
        op = $"vkAllocateMemory ({capOctets} bytes on memory type {kindOrdinal} for '{holderLabel}')";

        Result outcome = _backend.ReserveMemory(kindOrdinal, capOctets, out DeviceMemory memory);
        if (outcome != Result.Success)
            return outcome;

        nint mapped = 0;
        if (_memoryKindProps[(int)kindOrdinal].HasFlag(MemoryPropertyFlags.HostVisibleBit))
        {
            Result lookupOutcome = _backend.ChartMemory(memory, capOctets, out nint ptr);
            if (lookupOutcome != Result.Success)
            {
                _backend.ReleaseMemory(memory);
                op = $"vkMapMemory (block on memory type {kindOrdinal} for '{holderLabel}')";
                return lookupOutcome;
            }

            mapped = ptr;
        }

        chunk = new ChunkMemory(memory, mapped, capOctets);
        return Result.Success;
    }
}
