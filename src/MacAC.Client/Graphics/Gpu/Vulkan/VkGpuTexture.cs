using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGpuTexture : IGpuBitmap
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VkDeviceMemoryAllotter _allocator;
    private readonly VkUploadQueue _uploads;
    private readonly IGpuAssetSunsetFifo _sunset;
    private readonly VkAllocation _allocation;
    private bool _destroyed;

    internal VkGpuTexture(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkUploadQueue uploads,
        IGpuAssetSunsetFifo retirement,
        VkDebugNames diagLabels,
        in GpuBitmapSpec blurb,
        int specimenTally = 1,
        bool rasterizeMark = false,
        bool sampleable = true,
        Format fmtOverride = Silk.NET.Vulkan.Format.Undefined)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _sunset = retirement ?? throw new ArgumentNullException(nameof(retirement));
        ArgumentException.ThrowIfNullOrWhiteSpace(blurb.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.LayerCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blurb.MipLevelCount);
        if (specimenTally > 1 && sampleable)
        {
            throw new ArgumentException(
                "A multisampled image can't be registered in macac's single-sampled texture table; "
                + "create a separate single-sampled resolve image",
                nameof(sampleable));
        }

        Name = blurb.Name;
        Kind = blurb.Kind;
        Format = blurb.Format;
        Width = blurb.Width;
        Height = blurb.Height;
        StratumTally = blurb.LayerCount;
        MipTierTally = blurb.MipLevelCount;
        SampleCount = specimenTally;
        IsSampleable = sampleable;
        VkFmt = fmtOverride != Silk.NET.Vulkan.Format.Undefined
            ? fmtOverride
            : VkTextureFormatMapping.ComposeOf(blurb.Format);
        Aspect = VkTextureFormatMapping.AspectOf(blurb.Format);

        bool zDepthStencil = VkTextureFormatMapping.IsZDepthStencil(blurb.Format);
        ImageUsageFlags usage = zDepthStencil
            ? ImageUsageFlags.DepthStencilAttachmentBit
            : ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit;
        if (sampleable)
            usage |= ImageUsageFlags.SampledBit;
        if (rasterizeMark && !zDepthStencil)
            usage |= ImageUsageFlags.ColorAttachmentBit;
        if (specimenTally > 1)
        {
            usage = zDepthStencil
                ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransientAttachmentBit
                : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit;
        }

        ImageCreateInfo build = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = VkFmt,
            Extent = new Extent3D((uint)blurb.Width, (uint)blurb.Height, 1),
            MipLevels = (uint)blurb.MipLevelCount,
            ArrayLayers = (uint)blurb.LayerCount,
            Samples = VkTextureFormatMapping.ProbeTallyOf(specimenTally),
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkInterop.Check(
            _vk.CreateImage(_device, &build, null, out Image image),
            $"vkCreateImage ('{blurb.Name}')");
        Image = image;

        try
        {
            _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
            _allocation = _allocator.Reserve(requirements, GpuMemoryTenancy.DeviceLocal, blurb.Name);
            VkInterop.Check(
                _vk.BindImageMemory(_device, image, _allocation.Memory, _allocation.ShiftOctets),
                $"vkBindImageMemory ('{blurb.Name}')");

            ImageViewCreateInfo lensBuild = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = rasterizeMark
                    ? VkTextureFormatMapping.LensKindOf(blurb.Kind)
                    : VkTextureFormatMapping.SampledLensKindOf(blurb.Kind),
                Format = VkFmt,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = Aspect,
                    BaseMipLevel = 0,
                    LevelCount = (uint)blurb.MipLevelCount,
                    BaseArrayLayer = 0,
                    LayerCount = (uint)blurb.LayerCount,
                },
            };
            VkInterop.Check(
                _vk.CreateImageView(_device, &lensBuild, null, out ImageView lens),
                $"vkCreateImageView ('{blurb.Name}')");
            View = lens;
            SampledLens = rasterizeMark && !sampleable ? default : lens;

            if (rasterizeMark && sampleable)
            {
                lensBuild.ViewType = VkTextureFormatMapping.SampledLensKindOf(blurb.Kind);
                if (zDepthStencil)
                    lensBuild.SubresourceRange.AspectMask = ImageAspectFlags.DepthBit;
                VkInterop.Check(
                    _vk.CreateImageView(_device, &lensBuild, null, out ImageView sampled),
                    $"vkCreateImageView ('{blurb.Name}', sampled)");
                SampledLens = sampled;
                diagLabels.LabelImageLens(sampled, $"{blurb.Name}-sampled-view");
            }
        }
        catch
        {
            _vk.DestroyImage(_device, image, null);
            throw;
        }

        diagLabels.LabelImage(image, blurb.Name);
        diagLabels.LabelImageLens(View, $"{blurb.Name}-view");
    }

    public string Name { get; }
    public GpuBitmapFlavor Kind { get; }
    public GpuBitmapFmt Format { get; }
    public int Width { get; }
    public int Height { get; }
    public int StratumTally { get; }
    public int MipTierTally { get; }

    internal int SampleCount { get; }
    internal bool IsSampleable { get; }
    internal Image Image { get; }

    // The view a pass names as an attachment, and the only view a non-attachment has
    internal ImageView View { get; }

    internal ImageView SampledLens { get; }
    internal ImageLayout SampledArrangement
    {
        get
        {
            return VkTextureFormatMapping.IsZDepthStencil(Format)
            ? ImageLayout.DepthStencilReadOnlyOptimal
            : ImageLayout.ShaderReadOnlyOptimal;
        }
    }

    internal Format VkFmt { get; }
    internal ImageAspectFlags Aspect { get; }

    internal ImageLayout CurrentLayout { get; private set; } = ImageLayout.Undefined;

    public void Upload(int mipTier, int stratum, ReadOnlySpan<byte> data)
    {
        HurlIfDestroyed();
        ArgumentOutOfRangeException.ThrowIfNegative(mipTier);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(mipTier, MipTierTally);
        ArgumentOutOfRangeException.ThrowIfNegative(stratum);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stratum, StratumTally);
        if (data.IsEmpty)
            return;

        (int width, int height) = VkTextureFormatMapping.TierReach(Width, Height, mipTier);
        int anticipated = VkTextureFormatMapping.TierByteSize(Format, width, height);
        if (data.Length < anticipated)
        {
            throw new ArgumentException(
                $"Mip {mipTier} of '{Name}' is {width}x{height} and needs {anticipated} bytes; " +
                $"{data.Length} were supplied",
                nameof(data));
        }

        _uploads.StageImageWrite(Image, mipTier, stratum, width, height, CurrentLayout, data, Name);
        CurrentLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    public void ProduceMipChain()
    {
        HurlIfDestroyed();
        if (MipTierTally <= 1)
            return;

        if (ChunkCompressionCodec.IsChunkCompressed(Format))
        {
            throw new NotSupportedException(
                $"'{Name}' is {Format}, and Vulkan can't blit into a block-compressed image. " +
                "Build the chain on the CPU with ChunkCompressionMipSequence and upload each level " +
                "through Upload(mipLevel, layer, data). The GL path's reliance on driver-defined " +
                "glGenerateMipmap for compressed arrays is deliberately not carried forward");
        }

        _uploads.QueueMipBlit(Image, Width, Height, MipTierTally, StratumTally, CurrentLayout);
        CurrentLayout = ImageLayout.ShaderReadOnlyOptimal;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        Image image = Image;
        ImageView lens = View;
        ImageView sampledLens = SampledLens;
        var alloc = _allocation;
        _sunset.Retire(() =>
        {
            if (sampledLens.Handle is not 0 && sampledLens.Handle != lens.Handle)
                _vk.DestroyImageView(_device, sampledLens, null);
            _vk.DestroyImageView(_device, lens, null);
            _vk.DestroyImage(_device, image, null);
            _allocator.Release(alloc);
        });
    }

    internal void FlagArrangement(ImageLayout arrangement) => CurrentLayout = arrangement;

    private void HurlIfDestroyed() => ObjectDisposedException.ThrowIf(_destroyed, this);
}

internal sealed unsafe class VkGpuSampler : IClientGpuSampler
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly IGpuAssetSunsetFifo _sunset;

    internal VkGpuSampler(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        IGpuAssetSunsetFifo retirement,
        VkDebugNames diagLabels,
        in GpuSamplerSpec blurb,
        float upperSupportedAnisotropy)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _sunset = retirement ?? throw new ArgumentNullException(nameof(retirement));
        Description = blurb;

        float anisotropy = Math.Clamp(blurb.MaxAnisotropy, 1f, Math.Max(1f, upperSupportedAnisotropy));
        SamplerCreateInfo build = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MinFilter = VkTextureFormatMapping.SiftOf(blurb.MinFilter),
            MagFilter = VkTextureFormatMapping.SiftOf(blurb.MagFilter),
            MipmapMode = VkTextureFormatMapping.MipmapMannerOf(blurb.MipFilter),
            AddressModeU = VkTextureFormatMapping.AddressMannerOf(blurb.AddressU),
            AddressModeV = VkTextureFormatMapping.AddressMannerOf(blurb.AddressV),
            AddressModeW = VkTextureFormatMapping.AddressMannerOf(blurb.AddressV),
            AnisotropyEnable = anisotropy > 1f,
            MaxAnisotropy = anisotropy,
            MinLod = 0f,
            MaxLod = blurb.MipFilter == GpuMipSift.None ? 0f : Silk.NET.Vulkan.Vk.LodClampNone,
            BorderColor = BorderColor.FloatTransparentBlack,
            CompareEnable = false,
            UnnormalizedCoordinates = false,
        };
        VkInterop.Check(
            _vk.CreateSampler(_device, &build, null, out Sampler sampler),
            "vkCreateSampler");
        Handle = sampler;
        diagLabels.LabelSampler(
            sampler,
            $"sampler-{blurb.MinFilter}-{blurb.MipFilter}-{blurb.AddressU}");
    }

    public GpuSamplerSpec Description { get; }

    internal Sampler Handle { get; }

    internal bool IsDestroyed { get; private set; }

    public void Dispose()
    {
        if (IsDestroyed)
            return;
        IsDestroyed = true;
        Sampler hnd = Handle;
        _sunset.Retire(() => _vk.DestroySampler(_device, hnd, null));
    }
}
