using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static unsafe partial class VulkanPipeArrangements
{
    internal static CreatedDef Create(Silk.NET.Vulkan.Vk vk, Device dev)
    {
        ArgumentNullException.ThrowIfNull(vk);

        DescriptorSetLayout depot = default;
        DescriptorSetLayout uniform = default;
        DescriptorSetLayout chart = default;
        try
        {
            depot = BuildDepotSetArrangement(vk, dev);
            uniform = BuildUniformSetArrangement(vk, dev);
            chart = BuildTextureChartSetArrangement(vk, dev);
            var arrangement = BuildPipeArrangement(vk, dev, depot, uniform, chart);
            return new CreatedDef(vk, dev, depot, uniform, chart, arrangement);
        }
        catch
        {
            if (chart.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, chart, null);
            if (uniform.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, uniform, null);
            if (depot.Handle is not 0)
                vk.DestroyDescriptorSetLayout(dev, depot, null);
            throw;
        }
    }

    internal static DescriptorSetLayout BuildDepotSetArrangement(Silk.NET.Vulkan.Vk vk, Device dev)
    {
        int tally = (int)GpuBindingModel.DepotMappingTally;
        DescriptorSetLayoutBinding* mappings = stackalloc DescriptorSetLayoutBinding[tally];
        for (int idx = 0; idx < tally; ++idx)
        {
            mappings[idx] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)idx,
                DescriptorType = IsDynamicDepotMapping((uint)idx)
                    ? DescriptorType.StorageBufferDynamic
                    : DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            };
        }

        var build = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)tally,
            PBindings = mappings,
        };
        VkInterop.Check(
            vk.CreateDescriptorSetLayout(dev, &build, null, out DescriptorSetLayout arrangement),
            "vkCreateDescriptorSetLayout (set 0, storage)");
        return arrangement;
    }

    internal static DescriptorSetLayout BuildUniformSetArrangement(Silk.NET.Vulkan.Vk vk, Device dev)
    {
        uint[] declared = DeclaredUniformMappings;
        DescriptorSetLayoutBinding* mappings = stackalloc DescriptorSetLayoutBinding[declared.Length];
        for (int idx = 0; idx < declared.Length; ++idx)
        {
            mappings[idx] = new DescriptorSetLayoutBinding
            {
                Binding = declared[idx],
                DescriptorType = DescriptorType.UniformBufferDynamic,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            };
        }

        var build = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)declared.Length,
            PBindings = mappings,
        };
        VkInterop.Check(
            vk.CreateDescriptorSetLayout(dev, &build, null, out DescriptorSetLayout arrangement),
            "vkCreateDescriptorSetLayout (set 1, uniform)");
        return arrangement;
    }

    internal static DescriptorSetLayout BuildBundleUniformSetArrangement(
        Silk.NET.Vulkan.Vk vk,
        Device dev)
    {
        DescriptorSetLayoutBinding* mappings = stackalloc DescriptorSetLayoutBinding[(int)BundleUniformMappingTally];
        for (uint idx = 0; idx < BundleUniformMappingTally; ++idx)
        {
            mappings[idx] = new DescriptorSetLayoutBinding
            {
                Binding = GpuBindingModel.UniformAtmosphericCycle + idx,
                DescriptorType = DescriptorType.UniformBufferDynamic,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            };
        }

        var build = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = BundleUniformMappingTally,
            PBindings = mappings,
        };
        VkInterop.Check(
            vk.CreateDescriptorSetLayout(dev, &build, null, out DescriptorSetLayout arrangement),
            "vkCreateDescriptorSetLayout (set 3, render-pack uniforms)");
        return arrangement;
    }

    internal static DescriptorSetLayout BuildTextureChartSetArrangement(Silk.NET.Vulkan.Vk vk, Device dev)
    {
        var mapping = new DescriptorSetLayoutBinding
        {
            Binding = GpuBindingModel.TextureChartMapping,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = GpuBindingModel.TextureTableCapacity,
            StageFlags = ShaderStageFlags.FragmentBit,
        };
        var flagSet =
            DescriptorBindingFlags.PartiallyBoundBit
            | DescriptorBindingFlags.UpdateAfterBindBit
            | DescriptorBindingFlags.UpdateUnusedWhilePendingBit
            | DescriptorBindingFlags.VariableDescriptorCountBit;

        var mappingFlagSet = new DescriptorSetLayoutBindingFlagsCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = 1,
            PBindingFlags = &flagSet,
        };
        var build = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            PNext = &mappingFlagSet,
            Flags = DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit,
            BindingCount = 1,
            PBindings = &mapping,
        };
        VkInterop.Check(
            vk.CreateDescriptorSetLayout(dev, &build, null, out DescriptorSetLayout arrangement),
            "vkCreateDescriptorSetLayout (set 2, texture table)");
        return arrangement;
    }

    internal static PipelineLayout BuildPipeArrangement(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        DescriptorSetLayout depot,
        DescriptorSetLayout uniform,
        DescriptorSetLayout chart)
    {
        DescriptorSetLayout* sets = stackalloc DescriptorSetLayout[3];
        sets[0] = depot;
        sets[1] = uniform;
        sets[2] = chart;

        PushConstantRange pushConstants = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = GpuBindingModel.PushConstantOctets,
        };
        PipelineLayoutCreateInfo build = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 3,
            PSetLayouts = sets,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstants,
        };
        VkInterop.Check(
            vk.CreatePipelineLayout(dev, &build, null, out PipelineLayout arrangement),
            "vkCreatePipelineLayout");
        return arrangement;
    }

    internal static PipelineLayout BuildBundlePipeArrangement(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        DescriptorSetLayout depot,
        DescriptorSetLayout uniform,
        DescriptorSetLayout chart,
        DescriptorSetLayout bundleUniform)
    {
        DescriptorSetLayout* sets = stackalloc DescriptorSetLayout[4];
        sets[0] = depot;
        sets[1] = uniform;
        sets[2] = chart;
        sets[3] = bundleUniform;

        PushConstantRange pushConstants = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = GpuBindingModel.PushConstantOctets,
        };
        PipelineLayoutCreateInfo build = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 4,
            PSetLayouts = sets,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstants,
        };
        VkInterop.Check(
            vk.CreatePipelineLayout(dev, &build, null, out PipelineLayout arrangement),
            "vkCreatePipelineLayout (render-pack ABI)");
        return arrangement;
    }
}
