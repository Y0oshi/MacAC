using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static unsafe partial class VulkanPipeArrangements
{
    internal sealed class CreatedDef : IDisposable
    {
        private readonly Silk.NET.Vulkan.Vk _vk;
        private readonly Device _device;
        private readonly object _bundleMutex = new();
        private BundlePhase? _bundle;
        private int _bundleReferences;
        private int _upcomingBundleGen;
        private bool _destroyed;

        internal CreatedDef(
            Silk.NET.Vulkan.Vk vk,
            Device dev,
            DescriptorSetLayout depot,
            DescriptorSetLayout uniform,
            DescriptorSetLayout textureChart,
            PipelineLayout pipeArrangement)
        {
            _vk = vk;
            _device = dev;
            Depot = depot;
            Uniform = uniform;
            TextureChart = textureChart;
            PipeArrangement = pipeArrangement;
        }

        internal DescriptorSetLayout Depot { get; }
        internal DescriptorSetLayout Uniform { get; }
        internal DescriptorSetLayout TextureChart { get; }
        internal PipelineLayout PipeArrangement { get; }

        public void Dispose() => _destroyed = true;

        internal PackArrangementLease ObtainBundleArrangement()
        {
            lock (_bundleMutex)
            {
                ObjectDisposedException.ThrowIf(_destroyed, this);
                _bundle ??= BuildBundlePhase(++_upcomingBundleGen);
                ++_bundleReferences;
                return new PackArrangementLease(this, _bundle);
            }
        }

        internal void Destroy(Silk.NET.Vulkan.Vk vk, Device dev)
        {
            if (_destroyed)
                return;
            _destroyed = true;
            lock (_bundleMutex)
            {
                _bundle?.Destroy();
                _bundle = null;
                _bundleReferences = 0;
            }
            if (PipeArrangement.Handle is not 0)
                vk.DestroyPipelineLayout(dev, PipeArrangement, null);
            if (TextureChart.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, TextureChart, null);
            if (Uniform.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, Uniform, null);
            if (Depot.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, Depot, null);
        }

        private BundlePhase BuildBundlePhase(int gen)
        {
            DescriptorSetLayout bundleUniform = default;
            try
            {
                bundleUniform = BuildBundleUniformSetArrangement(_vk, _device);
                var bundlePipe = BuildBundlePipeArrangement(
                    _vk,
                    _device,
                    Depot,
                    Uniform,
                    TextureChart,
                    bundleUniform);
                return new BundlePhase(_vk, _device, gen, bundleUniform, bundlePipe);
            }
            catch
            {
                if (bundleUniform.Handle is not 0)
                    _vk.DestroyDescriptorSetLayout(_device, bundleUniform, null);
                throw;
            }
        }

        private void FreeBundleArrangement(BundlePhase phase)
        {
            lock (_bundleMutex)
            {
                if (_bundle != phase || _bundleReferences <= 0)
                    return;
                --_bundleReferences;
                if (_bundleReferences is not 0)
                    return;
                _bundle = null;
                phase.Destroy();
            }
        }

        internal sealed class PackArrangementLease : IDisposable
        {
            private CreatedDef? _holder;

            internal PackArrangementLease(CreatedDef holder, BundlePhase phase)
            {
                _holder = holder;
                State = phase;
            }

            internal BundlePhase State { get; }
            internal PipelineLayout PipeArrangement => State.PipeArrangement;

            public void Dispose()
            {
                CreatedDef? holder = Interlocked.Exchange(ref _holder, null);
                holder?.FreeBundleArrangement(State);
            }
        }

        // One generation of the pack layout and all descriptor pools allocated against it
        internal sealed unsafe class BundlePhase
        {
            private const int SetsPerReservoir = 32;
            private readonly Silk.NET.Vulkan.Vk _vk;
            private readonly Device _device;
            private readonly List<DescriptorPool> _reservoirs = [];
            private int _setTally;
            private bool _destroyed;

            internal BundlePhase(
                Silk.NET.Vulkan.Vk vk,
                Device dev,
                int gen,
                DescriptorSetLayout descriptorSetArrangement,
                PipelineLayout pipeArrangement)
            {
                _vk = vk;
                _device = dev;
                Generation = gen;
                DescriptorSetArrangement = descriptorSetArrangement;
                PipeArrangement = pipeArrangement;
            }

            internal int Generation { get; }
            internal DescriptorSetLayout DescriptorSetArrangement { get; }
            internal PipelineLayout PipeArrangement { get; }

            internal DescriptorSet ReserveDescriptorSet()
            {
                ObjectDisposedException.ThrowIf(_destroyed, this);
                if (_setTally % SetsPerReservoir is 0)
                    _reservoirs.Add(BuildReservoir());

                var arrangement = DescriptorSetArrangement;
                var reserve = new DescriptorSetAllocateInfo
                {
                    SType = StructureType.DescriptorSetAllocateInfo,
                    DescriptorPool = _reservoirs[^1],
                    DescriptorSetCount = 1,
                    PSetLayouts = &arrangement,
                };
                VkInterop.Check(
                    _vk.AllocateDescriptorSets(_device, &reserve, out DescriptorSet set),
                    "vkAllocateDescriptorSets (render-pack set 3)");
                ++_setTally;
                return set;
            }

            internal void Destroy()
            {
                if (_destroyed)
                    return;
                _destroyed = true;
                foreach (DescriptorPool reservoir in _reservoirs)
                    _vk.DestroyDescriptorPool(_device, reservoir, null);
                _reservoirs.Clear();
                if (PipeArrangement.Handle is not 0)
                    _vk.DestroyPipelineLayout(_device, PipeArrangement, null);
                if (DescriptorSetArrangement.Handle is not 0)
                    _vk.DestroyDescriptorSetLayout(_device, DescriptorSetArrangement, null);
            }

            private DescriptorPool BuildReservoir()
            {
                DescriptorPoolSize dims = new DescriptorPoolSize
                {
                    Type = DescriptorType.UniformBufferDynamic,
                    DescriptorCount = BundleUniformMappingTally * SetsPerReservoir,
                };
                DescriptorPoolCreateInfo build = new DescriptorPoolCreateInfo
                {
                    SType = StructureType.DescriptorPoolCreateInfo,
                    MaxSets = SetsPerReservoir,
                    PoolSizeCount = 1,
                    PPoolSizes = &dims,
                };
                VkInterop.Check(
                    _vk.CreateDescriptorPool(_device, &build, null, out DescriptorPool reservoir),
                    "vkCreateDescriptorPool (render-pack set 3)");
                return reservoir;
            }
        }
    }

    internal static uint DynamicDepotMappingTally { get; } = TallyDynamicDepotMappings();

    internal const uint UniformLandClip = 2;

    // Bindings 5..8 in opt-in set 3
    internal const uint BundleUniformMappingTally = 4;

    // Set 1's declared bindings in ascending order - the order vkCmdBindDescriptorSets requires its
    // dynamic offsets in
    internal static uint[] DeclaredUniformMappings { get; } = AssembleDeclaredUniformMappings();

    internal static bool IsDynamicDepotMapping(uint mapping)
    {
        return mapping switch
        {
            GpuBindingModel.DepotInsts => true,
            GpuBindingModel.DepotLots => true,
            GpuBindingModel.DepotClipSockets => true,
            GpuBindingModel.DepotInstLampSets => true,
            _ => false,
        };
    }

    internal static bool IsDeclaredBundleUniformMapping(uint mapping)
    {
        return mapping is >= GpuBindingModel.UniformAtmosphericCycle
        and <= GpuBindingModel.UniformBundlePrefs;
    }

    internal static bool IsDeclaredUniformMapping(uint mapping)
    {
        return mapping switch
        {
            GpuBindingModel.UniformTableauIllumination => true,
            UniformLandClip => true,
            GpuBindingModel.UniformLandTiling => true,
            GpuBindingModel.UniformHeavensParameters => true,
            _ => false,
        };
    }

    private static uint TallyDynamicDepotMappings()
    {
        uint tally = 0;
        for (uint mapping = 0; mapping < GpuBindingModel.DepotMappingTally; ++mapping)
        {
            if (IsDynamicDepotMapping(mapping))
                ++tally;
        }

        return tally;
    }

    private static uint[] AssembleDeclaredUniformMappings()
    {
        List<uint> mappings = new List<uint>(VulkanCycleMappings.UniformMappingTally);
        for (uint mapping = 0; mapping < VulkanCycleMappings.UniformMappingTally; ++mapping)
        {
            if (IsDeclaredUniformMapping(mapping))
                mappings.Add(mapping);
        }

        return [.. mappings];
    }
}
