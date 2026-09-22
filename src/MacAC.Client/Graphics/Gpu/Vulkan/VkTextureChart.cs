using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkTextureSlotAllotter
{
    private readonly PriorityQueue<uint, uint> _release = new();
    private readonly HashSet<uint> _online = [];

    internal VkTextureSlotAllotter(uint cap)
    {
        ArgumentOutOfRangeException.ThrowIfZero(cap);
        _cap = cap;
    }

    private readonly uint _cap;

    internal uint Capacity => _cap;
    internal int OnlineTally => _online.Count;

    internal int ReleaseTally => _release.Count;

    // Highest slot index ever handed out, plus one
    internal uint HiWater { get; private set; }

    internal uint Reserve()
    {
        uint socket;
        if (_release.Count > 0)
        {
            socket = _release.Dequeue();
        }
        else
        {
            if (HiWater >= _cap)
            {
                throw new InvalidOperationException(
                    $"The Vulkan texture table is full at {_cap} slots. Raise " +
                    "GpuBindingModel.TextureTableCapacity and the capability gate's limit together");
            }

            socket = HiWater++;
        }

        _online.Add(socket);
        return socket;
    }

    internal void Release(uint socket)
    {
        if (!_online.Remove(socket))
        {
            throw new InvalidOperationException(
                $"Texture table slot {socket} isn't live; releasing it twice would let two textures " +
                "share one index");
        }

        _release.Enqueue(socket, socket);
    }

    internal bool IsOnline(uint socket) => _online.Contains(socket);
}

internal sealed unsafe class VkTextureChart : IDisposable
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VkTextureSlotAllotter _sockets;
    private readonly DescriptorPool _reservoir;
    private readonly DescriptorSet _set;
    private readonly object _synchronize = new();

    private ImageView _defaultLens;
    private Sampler _defaultSampler;
    private ImageLayout _defaultArrangement = ImageLayout.ShaderReadOnlyOptimal;
    private bool _destroyed;

    internal VkTextureChart(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        DescriptorSetLayout arrangement,
        uint cap)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _sockets = new VkTextureSlotAllotter(cap);
        Capacity = cap;

        DescriptorPoolSize reservoirDims = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = cap,
        };
        DescriptorPoolCreateInfo reservoirBuild = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &reservoirDims,
        };
        VkInterop.Check(
            _vk.CreateDescriptorPool(_device, &reservoirBuild, null, out _reservoir),
            "vkCreateDescriptorPool (texture table)");

        uint variableTally = cap;
        var setArrangement = arrangement;
        var variable = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &variableTally,
        };
        var reserve = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = &variable,
            DescriptorPool = _reservoir,
            DescriptorSetCount = 1,
            PSetLayouts = &setArrangement,
        };
        VkInterop.Check(
            _vk.AllocateDescriptorSets(_device, &reserve, out _set),
            "vkAllocateDescriptorSets (texture table)");
    }

    internal uint Capacity { get; }

    internal DescriptorSet Set => _set;

    internal int OnlineSocketTally
    {
        get
        {
            lock (_synchronize)
                return _sockets.OnlineTally;
        }
    }

    internal uint HiWater
    {
        get
        {
            lock (_synchronize)
                return _sockets.HiWater;
        }
    }

    public void Dispose()
    {
        lock (_synchronize)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            if (_reservoir.Handle is not 0)
                _vk.DestroyDescriptorPool(_device, _reservoir, null);
        }
    }

    // Records the (view, sampler) pair written into a slot when it is scrubbed
    internal void AssignScrubMark(
        ImageView lens,
        Sampler sampler,
        ImageLayout arrangement = ImageLayout.ShaderReadOnlyOptimal)
    {
        _defaultLens = lens;
        _defaultSampler = sampler;
        _defaultArrangement = arrangement;
    }

    internal GpuTextureSlot Register(
        ImageView lens,
        Sampler sampler,
        ImageLayout arrangement = ImageLayout.ShaderReadOnlyOptimal)
    {
        lock (_synchronize)
        {
            ObjectDisposedException.ThrowIf(_destroyed, this);
            uint socket = _sockets.Reserve();
            Write(socket, lens, sampler, arrangement);
            return new GpuTextureSlot(socket);
        }
    }

    internal void FreeInstant(GpuTextureSlot socket)
    {
        lock (_synchronize)
        {
            if (_destroyed || !socket.IsAssigned)
                return;
            if (_defaultLens.Handle is not 0 && _defaultSampler.Handle is not 0)
                Write(socket.Index, _defaultLens, _defaultSampler, _defaultArrangement);
            _sockets.Release(socket.Index);
        }
    }

    internal bool IsOnline(GpuTextureSlot socket)
    {
        lock (_synchronize)
            return socket.IsAssigned && _sockets.IsOnline(socket.Index);
    }

    private void Write(
        uint socket,
        ImageView lens,
        Sampler sampler,
        ImageLayout arrangement)
    {
        DescriptorImageInfo details = new DescriptorImageInfo
        {
            ImageView = lens,
            Sampler = sampler,
            ImageLayout = arrangement,
        };
        WriteDescriptorSet emit = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _set,
            DstBinding = GpuBindingModel.TextureChartMapping,
            DstArrayElement = socket,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &details,
        };
        _vk.UpdateDescriptorSets(_device, 1, &emit, 0, null);
    }
}
