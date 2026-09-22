using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal readonly record struct VkDirectionalMultiviewRange(uint BaseLayer, uint LayerCount);

internal static class VkDirectionalMultiviewContract
{
    internal static VkDirectionalMultiviewRange Resolve(uint lensBitmask, int targetLayerCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetLayerCount);
        if (targetLayerCount > 31)
            throw new ArgumentOutOfRangeException(nameof(targetLayerCount));
        uint anticipated = (1u << targetLayerCount) - 1u;
        return lensBitmask != anticipated
            ? throw new NotSupportedException("Directional multiview must cover every contiguous target layer")
            : new VkDirectionalMultiviewRange(0u, (uint)targetLayerCount);
    }
}

internal sealed unsafe class VkDirectionalDepthTarget : IGpuDirectedZDepthMark
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly IGpuAssetSunsetFifo _sunset;
    private readonly ImageView[] _stratumViews;
    private readonly ImageLayout[] _stratumArrangements;
    private bool _destroyed;

    internal VkDirectionalDepthTarget(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkUploadQueue uploads,
        IGpuAssetSunsetFifo retirement,
        VkDebugNames diagLabels,
        in GpuDirectionalDepthTargetSpec blurb,
        Format zDepthStencilFmt)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _sunset = retirement ?? throw new ArgumentNullException(nameof(retirement));
        Description = blurb;

        GpuBitmapSpec textureBlurb = new GpuBitmapSpec(
            blurb.Name,
            GpuBitmapFlavor.Texture2DArray,
            blurb.DepthFormat,
            blurb.Resolution,
            blurb.Resolution,
            blurb.LayerCount,
            MipLevelCount: 1);
        Texture = new VkGpuTexture(
            vk,
            dev,
            allocator,
            uploads,
            retirement,
            diagLabels,
            textureBlurb,
            specimenTally: 1,
            rasterizeMark: true,
            sampleable: true,
            fmtOverride: zDepthStencilFmt);

        _stratumViews = new ImageView[blurb.LayerCount];
        _stratumArrangements = new ImageLayout[blurb.LayerCount];
        try
        {
            for (int stratum = 0; stratum < _stratumViews.Length; ++stratum)
            {
                ImageViewCreateInfo build = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = Texture.Image,
                    ViewType = ImageViewType.Type2D,
                    Format = Texture.VkFmt,
                    SubresourceRange = new ImageSubresourceRange
                    {
                        AspectMask = ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
                        BaseMipLevel = 0,
                        LevelCount = 1,
                        BaseArrayLayer = (uint)stratum,
                        LayerCount = 1,
                    },
                };
                VkInterop.Check(
                    vk.CreateImageView(dev, &build, null, out ImageView lens),
                    $"vkCreateImageView ('{blurb.Name}', layer {stratum})");
                _stratumViews[stratum] = lens;
                diagLabels.LabelImageLens(lens, $"{blurb.Name}-layer-{stratum}");
            }
        }
        catch
        {
            foreach (ImageView lens in _stratumViews)
            {
                if (lens.Handle is not 0)
                    vk.DestroyImageView(dev, lens, null);
            }
            Texture.Dispose();
            throw;
        }
    }

    public GpuDirectionalDepthTargetSpec Description { get; }

    public IGpuBitmap ZDepthTexture => Texture;

    internal VkGpuTexture Texture { get; }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        ImageView[] views = [.. _stratumViews];
        _sunset.Retire(() =>
        {
            foreach (ImageView lens in views)
                _vk.DestroyImageView(_device, lens, null);
        });
        Texture.Dispose();
    }

    internal ImageView LensAt(int stratum)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(stratum);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stratum, _stratumViews.Length);
        return _stratumViews[stratum];
    }

    internal ImageView MultiviewLens(uint lensBitmask)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _ = VkDirectionalMultiviewContract.Resolve(lensBitmask, Description.LayerCount);
        return Texture.View;
    }

    internal int StratumTallyForLensBitmask(uint lensBitmask)
    {
        return checked((int)VkDirectionalMultiviewContract.Resolve(
            lensBitmask,
            Description.LayerCount).LayerCount);
    }

    internal ImageLayout ArrangementAt(int stratum) => _stratumArrangements[stratum];

    internal void FlagArrangement(int stratum, ImageLayout arrangement) => _stratumArrangements[stratum] = arrangement;
}
