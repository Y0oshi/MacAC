using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkRealmPassScope : IRealmPassScope
{
    private readonly ClientPublication _bulletin;

    internal VkRealmPassScope(int sampleCount)
    {
        if (sampleCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        SampleCount = sampleCount;
        _bulletin = new ClientPublication(this);
    }

    public int SampleCount { get; }

    public RealmFrameSections Sections { get; } = new();

    public IGpuSweepCoder? LatestCoder { get; private set; }

    public int AffixWidth =>
        LatestCoder is VkGpuPassEncoder vulkan ? vulkan.AffixWidth : 0;

    public int AffixHeight =>
        LatestCoder is VkGpuPassEncoder vulkan ? vulkan.AffixHeight : 0;

    public IGpuSweepCoder DemandCoder()
    {
        return LatestCoder ?? throw new InvalidOperationException(
            "The Vulkan world renderers record into the pass VulkanRealmTableauStage opens; "
            + "no pass is open. The phase must bracket every world draw");
    }

    public IDisposable Publish(IGpuSweepCoder coder) =>
        BroadcastCore(coder, preserveReadiedSections: false);

    public void WipeInteriorZDepth()
    {
        if (DemandCoder() is not VkGpuPassEncoder coder)
        {
            throw new InvalidOperationException(
                "The Vulkan world pass scope was published with a non-Vulkan encoder");
        }

        if (!coder.HasZDepthAffix)
            return;

        ClearAttachment affix = new ClearAttachment
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue
            {
                DepthStencil = new ClearDepthStencilValue(depth: 1f, stencil: 0),
            },
        };
        ClearRect rect = new ClearRect
        {
            Rect = new Rect2D(
                new Offset2D(0, 0),
                new Extent2D((uint)coder.AffixWidth, (uint)coder.AffixHeight)),
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        coder.WipeAffixes(1, &affix, 1, &rect);
    }

    internal IDisposable BroadcastReadied(IGpuSweepCoder coder) =>
        BroadcastCore(coder, preserveReadiedSections: true);

    private IDisposable BroadcastCore(
        IGpuSweepCoder coder,
        bool preserveReadiedSections)
    {
        ArgumentNullException.ThrowIfNull(coder);
        if (LatestCoder is not null)
        {
            throw new InvalidOperationException(
                "A Vulkan world pass is by now published; passes do not nest");
        }

        LatestCoder = coder;
        if (!preserveReadiedSections)
            Sections.Reset();
        return _bulletin;
    }

    private sealed class ClientPublication : IDisposable
    {
        private readonly VkRealmPassScope _holder;

        internal ClientPublication(VkRealmPassScope holder) => _holder = holder;

        public void Dispose()
        {
            _holder.LatestCoder = null;
            _holder.Sections.Reset();
        }
    }
}
