using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class VkHostStorageVisibility
{
    internal static BufferMemoryBarrier2 Create(Buffer buf, ulong byteSize)
    {
        ArgumentOutOfRangeException.ThrowIfZero(byteSize);
        return new BufferMemoryBarrier2
        {
            SType = StructureType.BufferMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.HostBit,
            SrcAccessMask = AccessFlags2.HostWriteBit,
            DstStageMask = PipelineStageFlags2.VertexShaderBit,
            DstAccessMask = AccessFlags2.ShaderReadBit,
            SrcQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Silk.NET.Vulkan.Vk.QueueFamilyIgnored,
            Buffer = buf,
            Offset = 0,
            Size = byteSize,
        };
    }
}
