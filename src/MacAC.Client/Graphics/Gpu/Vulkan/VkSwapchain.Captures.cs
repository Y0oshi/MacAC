using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe partial class VkSwapchain
{
    internal byte[] GrabImage(
        Queue visualsFifo,
        uint visualsClan,
        uint imageOrdinal)
    {
        if (Configuration is not { } configuration)
            throw new InvalidOperationException("The swapchain hasn't been created");

        uint width = configuration.Width;
        uint height = configuration.Height;
        uint byteTally = width * height * 4;

        VkInterop.Check(_vk.DeviceWaitIdle(_device), "vkDeviceWaitIdle (screenshot)");

        Silk.NET.Vulkan.Buffer buf = default;
        DeviceMemory memory = default;
        CommandPool reservoir = default;
        try
        {
            BufferCreateInfo bufBuild = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = byteTally,
                Usage = BufferUsageFlags.TransferDstBit,
                SharingMode = SharingMode.Exclusive,
            };
            VkInterop.Check(
                _vk.CreateBuffer(_device, &bufBuild, null, out buf),
                "vkCreateBuffer (screenshot)");
            _vk.GetBufferMemoryRequirements(_device, buf, out MemoryRequirements requirements);
            uint? kindOrdinal = VkActiveDeviceProbe.SeekMemoryKind(
                _vk,
                _physicalDev,
                requirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            if (kindOrdinal is not { } ordinal)
            {
                throw new NotSupportedException(
                    "No host-visible Vulkan memory type is available for screenshot readback");
            }

            MemoryAllocateInfo reserve = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = ordinal,
            };
            VkInterop.Check(
                _vk.AllocateMemory(_device, &reserve, null, out memory),
                "vkAllocateMemory (screenshot)");
            VkInterop.Check(
                _vk.BindBufferMemory(_device, buf, memory, 0),
                "vkBindBufferMemory (screenshot)");

            CommandPoolCreateInfo reservoirBuild = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = visualsClan,
                Flags = CommandPoolCreateFlags.TransientBit,
            };
            VkInterop.Check(
                _vk.CreateCommandPool(_device, &reservoirBuild, null, out reservoir),
                "vkCreateCommandPool (screenshot)");
            var reserveDirectives = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = reservoir,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VkInterop.Check(
                _vk.AllocateCommandBuffers(_device, &reserveDirectives, out CommandBuffer directives),
                "vkAllocateCommandBuffers (screenshot)");

            CaptureGrab(directives, _images[imageOrdinal], buf, width, height);

            CommandBufferSubmitInfo directiveSubmit = new CommandBufferSubmitInfo
            {
                SType = StructureType.CommandBufferSubmitInfo,
                CommandBuffer = directives,
            };
            SubmitInfo2 submit = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &directiveSubmit,
            };
            VkInterop.Check(
                _vk.QueueSubmit2(visualsFifo, 1, &submit, default),
                "vkQueueSubmit2 (screenshot)");
            VkInterop.Check(
                _vk.QueueWaitIdle(visualsFifo),
                "vkQueueWaitIdle (screenshot)");

            void* mapped = null;
            VkInterop.Check(
                _vk.MapMemory(_device, memory, 0, byteTally, 0, &mapped),
                "vkMapMemory (screenshot)");
            try
            {
                return VkBackbufferSwizzle.ToGlOriginRgba(
                    new ReadOnlySpan<byte>(mapped, (int)byteTally),
                    (int)width,
                    (int)height,
                    (int)width * 4);
            }
            finally
            {
                _vk.UnmapMemory(_device, memory);
            }
        }
        finally
        {
            if (reservoir.Handle is not 0)
                _vk.DestroyCommandPool(_device, reservoir, null);
            if (buf.Handle is not 0)
                _vk.DestroyBuffer(_device, buf, null);
            if (memory.Handle is not 0)
                _vk.FreeMemory(_device, memory, null);
        }
    }
}
