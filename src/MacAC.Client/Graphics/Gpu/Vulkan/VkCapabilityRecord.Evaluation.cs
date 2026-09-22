using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static partial class VkCapabilityRequirements
{
    internal static IReadOnlyList<string> Evaluate(VkCapabilityRecord capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        List<string> misses = new List<string>();

        uint major = VulkanApiVer.Major(capabilities.DeviceApiVersionPacked);
        uint minor = VulkanApiVer.Minor(capabilities.DeviceApiVersionPacked);
        if (major < NeededApiMajor || (major == NeededApiMajor && minor < NeededApiMinor))
        {
            misses.Add(
                $"Vulkan {NeededApiMajor}.{NeededApiMinor} is required; " +
                $"the selected device reports {major}.{minor}.");
        }

        var features = capabilities.Features;
        if (!features.MultiDrawIndirect)
            misses.Add("multiDrawIndirect is required to submit world geometry.");
        if (!features.DrawIndirectFirstInstance)
            misses.Add("drawIndirectFirstInstance is required; indirect commands carry a per-group instance base.");
        if (!features.ShaderDrawParameters)
            misses.Add("shaderDrawParameters (gl_DrawID) is required to select per-draw batch data.");
        if (!features.ShaderClipDistance)
            misses.Add("shaderClipDistance is required by the per-cell clip gate.");
        if (!features.TextureCompressionBc)
            misses.Add("textureCompressionBC is required to upload DAT surfaces without transcoding.");
        if (!features.SamplerAnisotropy)
            misses.Add("samplerAnisotropy is required for sampler-quality parity.");
        if (!features.TimelineSemaphore)
            misses.Add("timelineSemaphore is required; the frame serial is the semaphore value.");
        if (!features.HostQueryReset)
            misses.Add("hostQueryReset is required to reset timestamp pools from the CPU.");
        if (!features.RuntimeDescriptorArray)
            misses.Add("runtimeDescriptorArray is required by the global texture table.");
        if (!features.DescriptorBindingPartiallyBound)
            misses.Add("descriptorBindingPartiallyBound is required; unregistered texture slots are legitimately absent.");
        if (!features.DescriptorBindingSampledImageUpdateAfterBind)
            misses.Add("descriptorBindingSampledImageUpdateAfterBind is required to register textures without rebuilding the set.");
        if (!features.DescriptorBindingUpdateUnusedWhilePending)
            misses.Add("descriptorBindingUpdateUnusedWhilePending is required to recycle texture slots while frames are in flight.");
        if (!features.DescriptorBindingVariableDescriptorCount)
            misses.Add("descriptorBindingVariableDescriptorCount is required to size the texture table.");
        if (!features.ShaderSampledImageArrayNonUniformIndexing)
            misses.Add("shaderSampledImageArrayNonUniformIndexing is required; one indirect dispatch reads different texture slots per draw.");
        if (!features.DynamicRendering)
            misses.Add("dynamicRendering is required; the frame uses no render-pass or framebuffer objects.");
        if (!features.Synchronization2)
            misses.Add("synchronization2 is required; every barrier in the frame is a barrier2.");
        if (!features.Maintenance4)
            misses.Add("maintenance4 is required for the relaxed shader interface rules the shared GLSL relies on.");
        if (!features.ShaderDemoteToHelperInvocation)
            misses.Add("shaderDemoteToHelperInvocation is required; the SPIR-V 1.6 fragment modules lower discard to OpDemoteToHelperInvocation.");

        var thresholds = capabilities.Limits;
        if (thresholds.MaxPushConstantsSize < GpuBindingModel.PushConstantOctets)
        {
            misses.Add(
                $"{GpuBindingModel.PushConstantOctets} push-constant bytes are required; " +
                $"this device provides {thresholds.MaxPushConstantsSize}.");
        }
        if (thresholds.MaxClipDistances < GpuBindingModel.ClipPlanesPerSocket)
        {
            misses.Add(
                $"{GpuBindingModel.ClipPlanesPerSocket} clip distances are required by the per-cell clip gate; " +
                $"this device provides {thresholds.MaxClipDistances}.");
        }
        if (thresholds.MaxBoundDescriptorSets < VulkanDescriptorSetTally)
        {
            misses.Add(
                $"{VulkanDescriptorSetTally} simultaneously bound descriptor sets are required " +
                $"(storage, uniform, texture table); this device provides {thresholds.MaxBoundDescriptorSets}.");
        }
        if (thresholds.MaxDescriptorSetStorageBuffersDynamic < VulkanPipeArrangements.DynamicDepotMappingTally)
        {
            misses.Add(
                $"set 0 declares {VulkanPipeArrangements.DynamicDepotMappingTally} dynamic storage bindings " +
                $"(Vulkan guarantees 4); this device provides {thresholds.MaxDescriptorSetStorageBuffersDynamic}.");
        }
        if (thresholds.MaxDescriptorSetStorageBuffers < GpuBindingModel.DepotMappingTally)
        {
            misses.Add(
                $"set 0 declares {GpuBindingModel.DepotMappingTally} total storage bindings; " +
                $"this device provides {thresholds.MaxDescriptorSetStorageBuffers} per set.");
        }
        if (thresholds.MaxPerStageDescriptorStorageBuffers < GpuBindingModel.DepotMappingTally)
        {
            misses.Add(
                $"set 0 exposes {GpuBindingModel.DepotMappingTally} storage bindings to each shader stage; " +
                $"this device provides {thresholds.MaxPerStageDescriptorStorageBuffers} per stage.");
        }
        if (thresholds.MaxDescriptorSetUniformBuffersDynamic < VulkanCycleMappings.DynamicUniformMappingTally)
        {
            misses.Add(
                $"set 1 declares {VulkanCycleMappings.DynamicUniformMappingTally} dynamic uniform bindings " +
                $"(Vulkan guarantees 8); this device provides {thresholds.MaxDescriptorSetUniformBuffersDynamic}.");
        }
        if (thresholds.MaxDescriptorSetUpdateAfterBindSampledImages < GpuBindingModel.TextureTableCapacity)
        {
            misses.Add(
                $"the texture table needs {GpuBindingModel.TextureTableCapacity} update-after-bind sampled images; " +
                $"this device provides {thresholds.MaxDescriptorSetUpdateAfterBindSampledImages} per set.");
        }
        if (thresholds.MaxPerStageDescriptorUpdateAfterBindSampledImages < GpuBindingModel.TextureTableCapacity)
        {
            misses.Add(
                $"the texture table needs {GpuBindingModel.TextureTableCapacity} update-after-bind sampled images " +
                $"in the fragment stage; this device provides {thresholds.MaxPerStageDescriptorUpdateAfterBindSampledImages}.");
        }
        if (!thresholds.TimestampComputeAndGraphics)
            misses.Add("graphics-queue timestamps are required by the frame profiler.");

        var formats = capabilities.Formats;
        if (!formats.SwapchainUnormFormat)
        {
            misses.Add(
                "the presentation surface must offer B8G8R8A8_UNORM; the renderer is " +
                "plain UNORM end to end and an sRGB swapchain would re-encode every frame.");
        }
        if (formats.DepthStencilFormat == Format.Undefined)
            misses.Add("a combined depth+stencil format is required by the portal aperture punch.");
        if (!formats.Bc1Sampled || !formats.Bc2Sampled || !formats.Bc3Sampled)
            misses.Add("BC1, BC2 and BC3 sampled-image support is required to upload DAT surfaces.");

        if (capabilities.Surface is { } canvas)
        {
            if (!canvas.PresentSupported)
                misses.Add("the selected device cannot present to the window surface.");
            if (!canvas.SupportsTransferSource)
                misses.Add("the swapchain must support TRANSFER_SRC usage for screenshot capture.");
        }

        if (capabilities.FunctionProbe.Failures.Count is not 0)
        {
            misses.AddRange(
                capabilities.FunctionProbe.Failures.Select(
                    miss => $"Vulkan device probe: {miss}"));
        }
        else
        {
            EvaluateBranch(capabilities, misses);
        }

        return misses;
    }

    private static void EvaluateBranch(VkCapabilityRecord capabilities, List<string> misses)
    {
        if (!capabilities.FunctionProbe.DeviceCreation)
            misses.Add("the Vulkan device-creation probe did not pass.");
        if (!capabilities.FunctionProbe.DescriptorIndexingLayout)
            misses.Add("the descriptor-indexing layout probe did not pass.");
        if (!capabilities.FunctionProbe.PushConstantLayout)
            misses.Add("the push-constant pipeline-layout probe did not pass.");
        if (!capabilities.FunctionProbe.DynamicRenderingClear)
            misses.Add("the dynamic-rendering clear probe did not pass.");
        EvaluateTail(capabilities, misses);
    }

    private static void EvaluateTail(VkCapabilityRecord capabilities, List<string> misses)
    {
        if (!capabilities.FunctionProbe.TimelineSemaphoreWait)
            misses.Add("the timeline-semaphore wait probe did not pass.");
        if (!capabilities.FunctionProbe.HostQueryReset)
            misses.Add("the host query-reset probe did not pass.");
        if (!capabilities.FunctionProbe.OffscreenReadback)
            misses.Add("the offscreen readback probe did not return the expected pixels.");
    }
}
