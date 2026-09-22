using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGpuBuffer : IClientGpuBuffer
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VkDeviceMemoryAllotter _allocator;
    private readonly VkUploadQueue _uploads;
    private readonly IGpuAssetSunsetFifo _sunset;
    private readonly VkAllocation _allocation;
    private bool _destroyed;

    internal VkGpuBuffer(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkUploadQueue uploads,
        IGpuAssetSunsetFifo retirement,
        VkDebugNames diagLabels,
        in GpuBufferSpec blurb)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _sunset = retirement ?? throw new ArgumentNullException(nameof(retirement));
        ArgumentException.ThrowIfNullOrWhiteSpace(blurb.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.SizeBytes);

        Name = blurb.Name;
        SizeBytes = blurb.SizeBytes;
        Usage = blurb.Usage;
        Residency = blurb.Residency;

        BufferCreateInfo build = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = (ulong)blurb.SizeBytes,
            Usage = UsageFlagSetOf(blurb.Usage),
            SharingMode = SharingMode.Exclusive,
        };
        VkInterop.Check(
            _vk.CreateBuffer(_device, &build, null, out Buffer hnd),
            $"vkCreateBuffer ('{blurb.Name}')");
        Handle = hnd;

        try
        {
            _vk.GetBufferMemoryRequirements(_device, hnd, out MemoryRequirements requirements);
            _allocation = _allocator.Reserve(requirements, blurb.Residency, blurb.Name);
            VkInterop.Check(
                _vk.BindBufferMemory(_device, hnd, _allocation.Memory, _allocation.ShiftOctets),
                $"vkBindBufferMemory ('{blurb.Name}')");
        }
        catch
        {
            _vk.DestroyBuffer(_device, hnd, null);
            throw;
        }

        diagLabels.LabelBuf(hnd, blurb.Name);
    }

    public string Name { get; }
    public long SizeBytes { get; }
    public GpuBufferPurpose Usage { get; }
    public GpuMemoryTenancy Residency { get; }
    public bool HostWritesAreCoherent =>
        _allocation.MemoryProps.HasFlag(MemoryPropertyFlags.HostCoherentBit);

    internal Buffer Handle { get; }

    // True when this buffer's memory is persistently mapped and directly writable
    internal bool IsMapped => _allocation.IsMapped;

    // The buffer's whole mapped range
    internal Span<byte> MappedSpan => _allocation.AsSpan()[..checked((int)SizeBytes)];

    public void Upload(long shiftOctets, ReadOnlySpan<byte> data)
    {
        HurlIfDestroyed();
        ArgumentOutOfRangeException.ThrowIfNegative(shiftOctets);
        if (data.IsEmpty)
            return;
        if (shiftOctets + data.Length > SizeBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(data),
                $"Writing {data.Length} bytes at offset {shiftOctets} exceeds " +
                $"'{Name}' ({SizeBytes} bytes)");
        }

        if (_allocation.IsMapped)
        {
            data.CopyTo(MappedSpan.Slice((int)shiftOctets, data.Length));
            return;
        }

        _uploads.StageBufferWrite(Handle, (ulong)shiftOctets, data, Name);
    }

    public void ReplicateTo(
        IClientGpuBuffer destination,
        long srcShiftOctets,
        long destShiftOctets,
        long byteCount)
    {
        HurlIfDestroyed();
        ArgumentNullException.ThrowIfNull(destination);
        if (destination is not VkGpuBuffer mark)
        {
            throw new ArgumentException(
                "The Vulkan backend can only copy into a Vulkan buffer",
                nameof(destination));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
        if (byteCount is 0)
            return;
        if (srcShiftOctets + byteCount > SizeBytes)
            throw new ArgumentOutOfRangeException(nameof(byteCount), $"The copy reads past the end of '{Name}'.");
        if (destShiftOctets + byteCount > mark.SizeBytes)
            throw new ArgumentOutOfRangeException(nameof(byteCount), $"The copy writes past the end of '{mark.Name}'.");

        _uploads.EnqueueBufferCopy(
            Handle,
            mark.Handle,
            (ulong)srcShiftOctets,
            (ulong)destShiftOctets,
            (ulong)byteCount);
    }

    public void Read(long shiftOctets, Span<byte> destination)
    {
        HurlIfDestroyed();
        if (Residency != GpuMemoryTenancy.HostReadable)
        {
            throw new InvalidOperationException(
                $"'{Name}' has {Residency} residency; only HostReadable buffers can be read back. " +
                "This path is diagnostics-only by design");
        }

        if (destination.IsEmpty)
            return;
        if (shiftOctets + destination.Length > SizeBytes)
            throw new ArgumentOutOfRangeException(nameof(destination), $"The read runs past the end of '{Name}'.");

        MappedSpan.Slice((int)shiftOctets, destination.Length).CopyTo(destination);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        Buffer hnd = Handle;
        var alloc = _allocation;
        _sunset.Retire(() =>
        {
            _vk.DestroyBuffer(_device, hnd, null);
            _allocator.Release(alloc);
        });
    }

    internal static BufferUsageFlags UsageFlagSetOf(GpuBufferPurpose usage)
    {
        var flagSet = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit;
        if (usage.HasFlag(GpuBufferPurpose.Vertex))
            flagSet |= BufferUsageFlags.VertexBufferBit;
        if (usage.HasFlag(GpuBufferPurpose.Index))
            flagSet |= BufferUsageFlags.IndexBufferBit;
        if (usage.HasFlag(GpuBufferPurpose.Storage))
            flagSet |= BufferUsageFlags.StorageBufferBit;
        if (usage.HasFlag(GpuBufferPurpose.Uniform))
            flagSet |= BufferUsageFlags.UniformBufferBit;
        if (usage.HasFlag(GpuBufferPurpose.Indirect))
            flagSet |= BufferUsageFlags.IndirectBufferBit;
        return flagSet;
    }

    private void HurlIfDestroyed() => ObjectDisposedException.ThrowIf(_destroyed, this);
}
