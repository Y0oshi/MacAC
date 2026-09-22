using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static unsafe partial class VkPhysicalDeviceInspector
{
    internal static VkDeviceFeatureSupport ScanFeatures(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev)
    {
        ArgumentNullException.ThrowIfNull(vk);

        var vulkan13 = new PhysicalDeviceVulkan13Features
        {
            SType = StructureType.PhysicalDeviceVulkan13Features,
        };
        var vulkan12 = new PhysicalDeviceVulkan12Features
        {
            SType = StructureType.PhysicalDeviceVulkan12Features,
            PNext = &vulkan13,
        };
        var vulkan11 = new PhysicalDeviceVulkan11Features
        {
            SType = StructureType.PhysicalDeviceVulkan11Features,
            PNext = &vulkan12,
        };
        PhysicalDeviceFeatures2 features2 = new PhysicalDeviceFeatures2
        {
            SType = StructureType.PhysicalDeviceFeatures2,
            PNext = &vulkan11,
        };
        vk.GetPhysicalDeviceFeatures2(dev, &features2);

        var core = features2.Features;
        return new VkDeviceFeatureSupport
        {
            MultiDrawIndirect = core.MultiDrawIndirect,
            DrawIndirectFirstInstance = core.DrawIndirectFirstInstance,
            ShaderClipDistance = core.ShaderClipDistance,
            TextureCompressionBc = core.TextureCompressionBC,
            SamplerAnisotropy = core.SamplerAnisotropy,
            ShaderDrawParameters = vulkan11.ShaderDrawParameters,
            Multiview = vulkan11.Multiview,
            TimelineSemaphore = vulkan12.TimelineSemaphore,
            HostQueryReset = vulkan12.HostQueryReset,
            RuntimeDescriptorArray = vulkan12.RuntimeDescriptorArray,
            DescriptorBindingPartiallyBound = vulkan12.DescriptorBindingPartiallyBound,
            DescriptorBindingSampledImageUpdateAfterBind =
                vulkan12.DescriptorBindingSampledImageUpdateAfterBind,
            DescriptorBindingUpdateUnusedWhilePending =
                vulkan12.DescriptorBindingUpdateUnusedWhilePending,
            DescriptorBindingVariableDescriptorCount =
                vulkan12.DescriptorBindingVariableDescriptorCount,
            ShaderSampledImageArrayNonUniformIndexing =
                vulkan12.ShaderSampledImageArrayNonUniformIndexing,
            DynamicRendering = vulkan13.DynamicRendering,
            Synchronization2 = vulkan13.Synchronization2,
            Maintenance4 = vulkan13.Maintenance4,
            ShaderDemoteToHelperInvocation = vulkan13.ShaderDemoteToHelperInvocation,
        };
    }

    internal static VkDeviceLimitSupport ScanThresholds(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev)
    {
        ArgumentNullException.ThrowIfNull(vk);

        var indexing = new PhysicalDeviceDescriptorIndexingProperties
        {
            SType = StructureType.PhysicalDeviceDescriptorIndexingProperties,
        };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &indexing,
        };
        vk.GetPhysicalDeviceProperties2(dev, &properties2);

        var thresholds = properties2.Properties.Limits;
        return new VkDeviceLimitSupport
        {
            MaxPushConstantsSize = thresholds.MaxPushConstantsSize,
            MaxClipDistances = thresholds.MaxClipDistances,
            MaxBoundDescriptorSets = thresholds.MaxBoundDescriptorSets,
            MaxDescriptorSetStorageBuffersDynamic = thresholds.MaxDescriptorSetStorageBuffersDynamic,
            MaxDescriptorSetStorageBuffers = thresholds.MaxDescriptorSetStorageBuffers,
            MaxPerStageDescriptorStorageBuffers = thresholds.MaxPerStageDescriptorStorageBuffers,
            MaxDescriptorSetUniformBuffersDynamic = thresholds.MaxDescriptorSetUniformBuffersDynamic,
            MaxDescriptorSetUpdateAfterBindSampledImages =
                indexing.MaxDescriptorSetUpdateAfterBindSampledImages,
            MaxPerStageDescriptorUpdateAfterBindSampledImages =
                indexing.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            TimestampComputeAndGraphics = thresholds.TimestampComputeAndGraphics,
            MinStorageBufferOffsetAlignment = (uint)thresholds.MinStorageBufferOffsetAlignment,
            MaxStorageBufferRange = thresholds.MaxStorageBufferRange,
            MinUniformBufferOffsetAlignment = (uint)thresholds.MinUniformBufferOffsetAlignment,
            MaxImageDimension2D = thresholds.MaxImageDimension2D,
            MaxImageArrayLayers = thresholds.MaxImageArrayLayers,
            DeviceLocalHeapBytes = LargestDevOwnHeap(vk, dev),
            MaxColorSampleCount = HighestSpecimenTally(
                thresholds.FramebufferColorSampleCounts & thresholds.FramebufferDepthSampleCounts),
        };
    }

    internal static VkFormatSupport ScanFormats(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev,
        bool canvasOffersUnorm)
    {
        ArgumentNullException.ThrowIfNull(vk);

        Format zDepthStencil = SelectZDepthStencilFmt(vk, dev);
        FormatProperties rgba16;
        vk.GetPhysicalDeviceFormatProperties(dev, Format.R16G16B16A16Sfloat, &rgba16);
        var rgba16Features = rgba16.OptimalTilingFeatures;

        return new VkFormatSupport
        {
            SwapchainUnormFormat = canvasOffersUnorm,
            DepthStencilFormat = zDepthStencil,
            DepthStencilSampled =
                zDepthStencil != Format.Undefined
                && SupportsOptimalSampling(vk, dev, zDepthStencil),
            Rgba16FloatColorAttachment =
                rgba16Features.HasFlag(FormatFeatureFlags.ColorAttachmentBit),
            Rgba16FloatSampled =
                rgba16Features.HasFlag(FormatFeatureFlags.SampledImageBit),
            Rgba16FloatLinearFilter =
                rgba16Features.HasFlag(FormatFeatureFlags.SampledImageFilterLinearBit),
            MaxRgba16FloatSampleCount = ScanOptimalTintSpecimenTally(
                vk,
                dev,
                Format.R16G16B16A16Sfloat),
            Bc1Sampled = SupportsOptimalSampling(vk, dev, Format.BC1RgbaUnormBlock),
            Bc2Sampled = SupportsOptimalSampling(vk, dev, Format.BC2UnormBlock),
            Bc3Sampled = SupportsOptimalSampling(vk, dev, Format.BC3UnormBlock),
        };
    }

    internal static uint ScanOptimalTintSpecimenTally(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev,
        Format fmt)
    {
        ImageFormatProperties props;
        Result outcome = vk.GetPhysicalDeviceImageFormatProperties(
            dev,
            fmt,
            ImageType.Type2D,
            ImageTiling.Optimal,
            ImageUsageFlags.ColorAttachmentBit,
            ImageCreateFlags.None,
            &props);
        return outcome == Result.Success
            ? HighestSpecimenTally(props.SampleCounts)
            : 0u;
    }

    // Enumerate queue families, reporting present support only when a surface is supplied
    internal static IReadOnlyList<VkQueueFamilyCandidate> ScanFifoClans(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice dev,
        Silk.NET.Vulkan.Extensions.KHR.KhrSurface? canvasApi,
        SurfaceKHR canvas)
    {
        ArgumentNullException.ThrowIfNull(vk);

        uint tally = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref tally, null);
        if (tally is 0)
            return [];

        QueueFamilyProperties[] props = new QueueFamilyProperties[tally];
        fixed (QueueFamilyProperties* lead = props)
            vk.GetPhysicalDeviceQueueFamilyProperties(dev, ref tally, lead);

        var clans = new List<VkQueueFamilyCandidate>((int)tally);
        for (uint idx = 0; idx < tally; ++idx)
        {
            bool visuals = props[idx].QueueFlags.HasFlag(QueueFlags.GraphicsBit);
            bool present = false;
            if (canvasApi is not null)
            {
                VkInterop.Check(
                    canvasApi.GetPhysicalDeviceSurfaceSupport(
                        dev,
                        idx,
                        canvas,
                        out Bool32 supported),
                    "vkGetPhysicalDeviceSurfaceSupportKHR");
                present = supported;
            }

            clans.Add(new VkQueueFamilyCandidate(idx, visuals, present));
        }

        return clans;
    }
}
