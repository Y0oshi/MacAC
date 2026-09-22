using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static unsafe partial class VkActiveDeviceProbe
{
    private static byte[] PaintAndScanBack(
        Silk.NET.Vulkan.Vk vk,
        PhysicalDevice physicalDev,
        Device dev,
        Queue fifo,
        uint visualsClan)
    {
        Image image = default;
        DeviceMemory imageMemory = default;
        ImageView lens = default;
        Silk.NET.Vulkan.Buffer readback = default;
        DeviceMemory readbackMemory = default;
        CommandPool directiveReservoir = default;
        Semaphore timeline = default;

        try
        {
            (image, imageMemory) = BuildTintMark(vk, physicalDev, dev);
            lens = BuildImageLens(vk, dev, image);
            uint byteTally = SensorReach * SensorReach * 4;
            (readback, readbackMemory) = BuildReadbackBuf(vk, physicalDev, dev, byteTally);

            CommandPoolCreateInfo reservoirBuild = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                QueueFamilyIndex = visualsClan,
                Flags = CommandPoolCreateFlags.TransientBit,
            };
            VkInterop.Check(
                vk.CreateCommandPool(dev, &reservoirBuild, null, out directiveReservoir),
                "vkCreateCommandPool");

            var reserve = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = directiveReservoir,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            VkInterop.Check(
                vk.AllocateCommandBuffers(dev, &reserve, out CommandBuffer directives),
                "vkAllocateCommandBuffers");

            CaptureWipeAndDuplicate(vk, directives, image, lens, readback);

            SemaphoreTypeCreateInfo semaphoreKind = new SemaphoreTypeCreateInfo
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = 0,
            };
            SemaphoreCreateInfo semaphoreBuild = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo,
                PNext = &semaphoreKind,
            };
            VkInterop.Check(
                vk.CreateSemaphore(dev, &semaphoreBuild, null, out timeline),
                "vkCreateSemaphore (timeline)");

            CommandBufferSubmitInfo directiveSubmit = new CommandBufferSubmitInfo
            {
                SType = StructureType.CommandBufferSubmitInfo,
                CommandBuffer = directives,
            };
            SemaphoreSubmitInfo signal = new SemaphoreSubmitInfo
            {
                SType = StructureType.SemaphoreSubmitInfo,
                Semaphore = timeline,
                Value = 1,
                StageMask = PipelineStageFlags2.AllCommandsBit,
            };
            SubmitInfo2 submit = new SubmitInfo2
            {
                SType = StructureType.SubmitInfo2,
                CommandBufferInfoCount = 1,
                PCommandBufferInfos = &directiveSubmit,
                SignalSemaphoreInfoCount = 1,
                PSignalSemaphoreInfos = &signal,
            };
            VkInterop.Check(
                vk.QueueSubmit2(fifo, 1, &submit, default),
                "vkQueueSubmit2");

            ulong pauseVal = 1;
            Semaphore pauseSemaphore = timeline;
            SemaphoreWaitInfo pause = new SemaphoreWaitInfo
            {
                SType = StructureType.SemaphoreWaitInfo,
                SemaphoreCount = 1,
                PSemaphores = &pauseSemaphore,
                PValues = &pauseVal,
            };
            VkInterop.Check(
                vk.WaitSemaphores(dev, &pause, 5_000_000_000ul),
                "vkWaitSemaphores (timeline)");

            void* mapped = null;
            VkInterop.Check(
                vk.MapMemory(dev, readbackMemory, 0, byteTally, 0, &mapped),
                "vkMapMemory (readback)");
            try
            {
                byte[] px = new byte[byteTally];
                new ReadOnlySpan<byte>(mapped, (int)byteTally).CopyTo(px);
                return px;
            }
            finally
            {
                vk.UnmapMemory(dev, readbackMemory);
            }
        }
        finally
        {
            if (timeline.Handle is not 0)
                vk.DestroySemaphore(dev, timeline, null);
            if (directiveReservoir.Handle is not 0)
                vk.DestroyCommandPool(dev, directiveReservoir, null);
            if (readback.Handle is not 0)
                vk.DestroyBuffer(dev, readback, null);
            if (readbackMemory.Handle is not 0)
                vk.FreeMemory(dev, readbackMemory, null);
            if (lens.Handle is not 0)
                vk.DestroyImageView(dev, lens, null);
            if (image.Handle is not 0)
                vk.DestroyImage(dev, image, null);
            if (imageMemory.Handle is not 0)
                vk.FreeMemory(dev, imageMemory, null);
        }
    }
}
