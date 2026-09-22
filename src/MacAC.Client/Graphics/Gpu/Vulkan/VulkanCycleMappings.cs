using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VulkanCycleMappings : IDisposable
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VulkanPipeArrangements.CreatedDef _arrangements;
    private readonly VkWiringScopeArena _arena;
    private readonly uint _upperDepotBufSpanOctets;
    private readonly List<DescriptorPool> _reservoirs = [];
    private readonly List<(DescriptorSet Storage, DescriptorSet Uniform)> _sets = [];
    private readonly ulong[] _bundleBufs = new ulong[VulkanPipeArrangements.BundleUniformMappingTally];
    private readonly uint[] _bundleShifts = new uint[VulkanPipeArrangements.BundleUniformMappingTally];
    private readonly uint[] _bundleSpans = new uint[VulkanPipeArrangements.BundleUniformMappingTally];
    private readonly Dictionary<PackWiringKey, int> _bundleSocketsByPhase = [];
    private readonly List<DescriptorSet> _bundleSets = [];
    private int _bundleOnlineTally;
    private int _bundleGen = -1;

    private bool _destroyed;

    // Set pairs one descriptor pool serves
    private const int DuosPerReservoir = 16;

    // Set 0's dynamic-offset slots, in binding order - the order vkCmdBindDescriptorSets requires
    private static readonly uint[] DynamicDepotMappings = AssembleDynamicDepotMappings();

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        foreach (DescriptorPool reservoir in _reservoirs)
        {
            if (reservoir.Handle is not 0)
                _vk.DestroyDescriptorPool(_device, reservoir, null);
        }

        _reservoirs.Clear();
        _sets.Clear();
        _bundleSocketsByPhase.Clear();
        _bundleSets.Clear();
    }

    internal const int UniformMappingTally = 5;

    internal static uint DynamicUniformMappingTally { get; } =
        (uint)VulkanPipeArrangements.DeclaredUniformMappings.Length;

    internal VulkanCycleMappings(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VulkanPipeArrangements.CreatedDef layouts,
        VkGpuBuffer loop,
        VkGpuBuffer dummy,
        uint upperDepotBufSpanOctets)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _arrangements = layouts ?? throw new ArgumentNullException(nameof(layouts));
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(dummy);
        ArgumentOutOfRangeException.ThrowIfLessThan(upperDepotBufSpanOctets, 16u);

        Ring = loop;
        Dummy = dummy;
        _upperDepotBufSpanOctets = upperDepotBufSpanOctets;
        _arena = new VkWiringScopeArena(
            (int)GpuBindingModel.DepotMappingTally,
            UniformMappingTally,
            VulkanPipeArrangements.IsDynamicDepotMapping);

        uint dummyDepotSpan = (uint)Math.Min(dummy.SizeBytes, _upperDepotBufSpanOctets);
        for (uint mapping = 0; mapping < GpuBindingModel.DepotMappingTally; ++mapping)
            _arena.SeedDepot(mapping, dummy.Handle.Handle, shiftOctets: 0, dummyDepotSpan);

        uint dummyUniformSpan = (uint)Math.Min(dummy.SizeBytes, 65536);
        for (uint mapping = 0; mapping < UniformMappingTally; ++mapping)
            _arena.SeedUniform(mapping, dummy.Handle.Handle, dummyUniformSpan);
        for (int mapping = 0; mapping < _bundleBufs.Length; ++mapping)
        {
            _bundleBufs[mapping] = dummy.Handle.Handle;
            _bundleSpans[mapping] = dummyUniformSpan;
        }
    }

    internal VkGpuBuffer Ring { get; }

    internal VkGpuBuffer Dummy { get; }

    internal int AmbitTally => _arena.Count;

    // Recycles the arena for a new frame on this slot
    internal void BeginFrame()
    {
        _arena.BeginFrame();
        _bundleSocketsByPhase.Clear();
        _bundleOnlineTally = 0;
    }

    internal void AssignDepot(uint mapping, VkGpuBuffer buf, uint shiftOctets, uint byteSize)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mapping, GpuBindingModel.DepotMappingTally);
        uint descriptorShift = VulkanPipeArrangements.IsDynamicDepotMapping(mapping) ? 0 : shiftOctets;
        _arena.AssignDepot(
            mapping,
            buf.Handle.Handle,
            shiftOctets,
            LimitSpan(buf, byteSize, descriptorShift));
    }

    internal void AssignUniform(uint mapping, VkGpuBuffer buf, uint shiftOctets, uint byteSize)
    {
        if (mapping < UniformMappingTally)
        {
            _arena.AssignUniform(
                mapping,
                buf.Handle.Handle,
                shiftOctets,
                Math.Min(LimitSpan(buf, byteSize, offsetBytes: 0), 65536));
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(mapping, GpuBindingModel.UniformAtmosphericCycle);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mapping, GpuBindingModel.UniformBundlePrefs);
        int bundleMapping = (int)(mapping - GpuBindingModel.UniformAtmosphericCycle);
        _bundleBufs[bundleMapping] = buf.Handle.Handle;
        _bundleShifts[bundleMapping] = shiftOctets;
        _bundleSpans[bundleMapping] = Math.Min(LimitSpan(buf, byteSize, offsetBytes: 0), 65536);
    }

    internal void Bind(
        CommandBuffer directives,
        ClientVulkanGpuDevice dev,
        PipelineLayout pipeArrangement,
        VulkanPipeArrangements.CreatedDef.BundlePhase? bundlePhase = null)
    {
        (int ordinal, int socket, bool needsEmit) = _arena.Resolve();
        if (socket < 0)
        {
            socket = _sets.Count;
            _sets.Add(ReserveDuo());
            _arena.AssignSocket(ordinal, socket);
        }

        if (needsEmit)
            EmitDuo(_sets[socket]);

        DescriptorSet* sets = stackalloc DescriptorSet[3];
        sets[0] = _sets[socket].Storage;
        sets[1] = _sets[socket].Uniform;
        sets[2] = dev.TextureChart.Set;

        uint[] declaredUniforms = VulkanPipeArrangements.DeclaredUniformMappings;
        int dynamicTally = DynamicDepotMappings.Length + declaredUniforms.Length;
        uint* shifts = stackalloc uint[dynamicTally];
        for (int idx = 0; idx < DynamicDepotMappings.Length; ++idx)
            shifts[idx] = _arena.DepotShift(DynamicDepotMappings[idx]);
        for (int idx = 0; idx < declaredUniforms.Length; ++idx)
            shifts[DynamicDepotMappings.Length + idx] = _arena.UniformShift(declaredUniforms[idx]);

        _vk.CmdBindDescriptorSets(
            directives,
            PipelineBindPoint.Graphics,
            pipeArrangement,
            0,
            3,
            sets,
            (uint)dynamicTally,
            shifts);

        if (bundlePhase is not null)
            AttachBundleSet(directives, pipeArrangement, bundlePhase);
    }

    private static uint[] AssembleDynamicDepotMappings()
    {
        List<uint> mappings = new List<uint>((int)GpuBindingModel.DepotMappingTally);
        for (uint mapping = 0; mapping < GpuBindingModel.DepotMappingTally; ++mapping)
        {
            if (VulkanPipeArrangements.IsDynamicDepotMapping(mapping))
                mappings.Add(mapping);
        }

        return [.. mappings];
    }

    private void AttachBundleSet(
        CommandBuffer directives,
        PipelineLayout pipeArrangement,
        VulkanPipeArrangements.CreatedDef.BundlePhase phase)
    {
        if (_bundleGen != phase.Generation)
        {
            _bundleGen = phase.Generation;
            _bundleSets.Clear();
            _bundleSocketsByPhase.Clear();
            _bundleOnlineTally = 0;
        }

        var tag = LatestBundleTag();
        if (!_bundleSocketsByPhase.TryGetValue(tag, out int socket))
        {
            socket = _bundleOnlineTally++;
            _bundleSocketsByPhase.Add(tag, socket);
            if (socket == _bundleSets.Count)
                _bundleSets.Add(phase.ReserveDescriptorSet());
            EmitBundleSet(_bundleSets[socket]);
        }

        var set = _bundleSets[socket];
        uint* shifts = stackalloc uint[(int)VulkanPipeArrangements.BundleUniformMappingTally];
        for (int idx = 0; idx < _bundleShifts.Length; ++idx)
            shifts[idx] = _bundleShifts[idx];
        _vk.CmdBindDescriptorSets(
            directives,
            PipelineBindPoint.Graphics,
            pipeArrangement,
            GpuBindingModel.RasterizeBundleUniformSet,
            1,
            &set,
            VulkanPipeArrangements.BundleUniformMappingTally,
            shifts);
    }

    private PackWiringKey LatestBundleTag()
    {
        return new(
        _bundleBufs[0], _bundleSpans[0],
        _bundleBufs[1], _bundleSpans[1],
        _bundleBufs[2], _bundleSpans[2],
        _bundleBufs[3], _bundleSpans[3]);
    }

    private void EmitBundleSet(DescriptorSet set)
    {
        for (int idx = 0; idx < _bundleBufs.Length; ++idx)
        {
            EmitUniform(
                set,
                GpuBindingModel.UniformAtmosphericCycle + (uint)idx,
                new Silk.NET.Vulkan.Buffer(_bundleBufs[idx]),
                _bundleSpans[idx]);
        }
    }

    private void EmitDuo((DescriptorSet Storage, DescriptorSet Uniform) duo)
    {
        for (uint mapping = 0; mapping < GpuBindingModel.DepotMappingTally; ++mapping)
        {
            EmitDepot(
                duo.Storage,
                mapping,
                new Silk.NET.Vulkan.Buffer(_arena.DepotBuf(mapping)),
                _arena.DepotDescriptorShift(mapping),
                _arena.DepotSpan(mapping),
                VulkanPipeArrangements.IsDynamicDepotMapping(mapping));
        }

        foreach (uint mapping in VulkanPipeArrangements.DeclaredUniformMappings)
        {
            EmitUniform(
                duo.Uniform,
                mapping,
                new Silk.NET.Vulkan.Buffer(_arena.UniformBuf(mapping)),
                _arena.UniformSpan(mapping));
        }
    }

    private (DescriptorSet Storage, DescriptorSet Uniform) ReserveDuo()
    {
        if (_sets.Count % DuosPerReservoir is 0)
            _reservoirs.Add(BuildReservoir());
        var reservoir = _reservoirs[^1];
        return (Reserve(reservoir, _arrangements.Depot), Reserve(reservoir, _arrangements.Uniform));
    }

    private DescriptorPool BuildReservoir()
    {
        DescriptorPoolSize* dimsList = stackalloc DescriptorPoolSize[3];
        dimsList[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBufferDynamic,
            DescriptorCount = VulkanPipeArrangements.DynamicDepotMappingTally * DuosPerReservoir,
        };
        dimsList[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount =
                (GpuBindingModel.DepotMappingTally - VulkanPipeArrangements.DynamicDepotMappingTally)
                * DuosPerReservoir,
        };
        dimsList[2] = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBufferDynamic,
            DescriptorCount = DynamicUniformMappingTally * DuosPerReservoir,
        };
        DescriptorPoolCreateInfo reservoirBuild = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 2 * DuosPerReservoir,
            PoolSizeCount = 3,
            PPoolSizes = dimsList,
        };
        VkInterop.Check(
            _vk.CreateDescriptorPool(_device, &reservoirBuild, null, out DescriptorPool reservoir),
            "vkCreateDescriptorPool (frame bindings)");
        return reservoir;
    }

    private uint LimitSpan(VkGpuBuffer buf, uint asked, uint offsetBytes)
    {
        long leftover = buf.SizeBytes - offsetBytes;
        if (leftover <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsetBytes),
                offsetBytes,
                $"A storage binding was pointed past the end of its {buf.SizeBytes}-byte buffer. " +
                "A descriptor range of zero isn't representable in Vulkan");
        }

        uint onHand = (uint)Math.Min(leftover, _upperDepotBufSpanOctets);
        return asked is 0 ? onHand : Math.Min(Math.Max(asked, 16), onHand);
    }

    private DescriptorSet Reserve(DescriptorPool reservoir, DescriptorSetLayout arrangement)
    {
        var hnd = arrangement;
        var reserve = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = reservoir,
            DescriptorSetCount = 1,
            PSetLayouts = &hnd,
        };
        VkInterop.Check(
            _vk.AllocateDescriptorSets(_device, &reserve, out DescriptorSet set),
            "vkAllocateDescriptorSets (frame bindings)");
        return set;
    }

    private void EmitDepot(
        DescriptorSet set,
        uint mapping,
        Silk.NET.Vulkan.Buffer buf,
        uint shiftOctets,
        uint spanOctets,
        bool dynamic)
    {
        DescriptorBufferInfo details = new DescriptorBufferInfo
        {
            Buffer = buf,
            Offset = shiftOctets,
            Range = spanOctets,
        };
        WriteDescriptorSet emit = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = mapping,
            DescriptorCount = 1,
            DescriptorType = dynamic
                ? DescriptorType.StorageBufferDynamic
                : DescriptorType.StorageBuffer,
            PBufferInfo = &details,
        };
        _vk.UpdateDescriptorSets(_device, 1, &emit, 0, null);
    }

    private void EmitUniform(
        DescriptorSet set,
        uint mapping,
        Silk.NET.Vulkan.Buffer buf,
        uint spanOctets)
    {
        DescriptorBufferInfo details = new DescriptorBufferInfo
        {
            Buffer = buf,
            Offset = 0,
            Range = spanOctets,
        };
        WriteDescriptorSet emit = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = mapping,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            PBufferInfo = &details,
        };
        _vk.UpdateDescriptorSets(_device, 1, &emit, 0, null);
    }

    private readonly record struct PackWiringKey(
        ulong Buffer0,
        uint Range0,
        ulong Buffer1,
        uint Range1,
        ulong Buffer2,
        uint Range2,
        ulong Buffer3,
        uint Range3);
}
