using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkMeshPipelineDevice(IGpuAssetSunsetFifo resourceRetirement) : ITriMeshPipeDevice
{

    public IGpuAssetSunsetFifo ResourceRetirement { get; } = resourceRetirement
            ?? throw new ArgumentNullException(nameof(resourceRetirement));

    public uint InstanceVBO => 0;

    public bool HasBindless => true;

    public bool HasOpenGL43 => true;

    public bool HasPendingWork => false;

    public void ProcessQueue()
    {
    }

    public void Dispose()
    {
    }
}
