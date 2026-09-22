using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkGpuRenderTarget : IGpuRasterizeMark
{
    private readonly VkGpuTexture? _multisampleTint;
    private readonly VkGpuTexture? _multisampleDepth;
    private bool _destroyed;

    internal VkGpuRenderTarget(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkUploadQueue uploads,
        IGpuAssetSunsetFifo sunset,
        VkDebugNames diagLabels,
        in GpuRenderTargetSpec description,
        Format devZDepthStencilFmt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description.Name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(description.SampleCount);
        if (description.SampleableDepth && description.DepthFormat is null)
        {
            throw new ArgumentException(
                "SampleableDepth needs a depth format",
                nameof(description));
        }
        Description = description;

        _tint = new VkGpuTexture(
            vk,
            dev,
            allocator,
            uploads,
            sunset,
            diagLabels,
            new GpuBitmapSpec(
                $"{description.Name}-color",
                GpuBitmapFlavor.Texture2D,
                description.ColorFormat,
                description.Width,
                description.Height,
                LayerCount: 1,
                MipLevelCount: 1),
            specimenTally: 1,
            rasterizeMark: true,
            sampleable: true);

        if (description.SampleCount > 1)
        {
            _multisampleTint = new VkGpuTexture(
                vk,
                dev,
                allocator,
                uploads,
                sunset,
                diagLabels,
                new GpuBitmapSpec(
                    $"{description.Name}-color-msaa",
                    GpuBitmapFlavor.Texture2D,
                    description.ColorFormat,
                    description.Width,
                    description.Height,
                    LayerCount: 1,
                    MipLevelCount: 1),
                description.SampleCount,
                rasterizeMark: true,
                sampleable: false);
        }

        if (description.DepthFormat is { } zDepthFmt)
        {
            int keptZDepthSpecimens =
                description.SampleableDepth ? 1 : description.SampleCount;
            _depth = new VkGpuTexture(
                vk,
                dev,
                allocator,
                uploads,
                sunset,
                diagLabels,
                new GpuBitmapSpec(
                    $"{description.Name}-depth",
                    GpuBitmapFlavor.Texture2D,
                    zDepthFmt,
                    description.Width,
                    description.Height,
                    LayerCount: 1,
                    MipLevelCount: 1),
                keptZDepthSpecimens,
                rasterizeMark: true,
                sampleable: description.SampleableDepth,
                fmtOverride: devZDepthStencilFmt);

            if (description.SampleableDepth && description.SampleCount > 1)
            {
                _multisampleDepth = new VkGpuTexture(
                    vk,
                    dev,
                    allocator,
                    uploads,
                    sunset,
                    diagLabels,
                    new GpuBitmapSpec(
                        $"{description.Name}-depth-msaa",
                        GpuBitmapFlavor.Texture2D,
                        zDepthFmt,
                        description.Width,
                        description.Height,
                        LayerCount: 1,
                        MipLevelCount: 1),
                    description.SampleCount,
                    rasterizeMark: true,
                    sampleable: false,
                    fmtOverride: devZDepthStencilFmt);
            }
        }
    }

    public GpuRenderTargetSpec Description { get; }

    public IGpuBitmap ColorTexture => _tint;

    public IGpuBitmap? ZDepthTexture => Description.SampleableDepth ? _depth : null;

    internal VkGpuTexture TintAffix => _multisampleTint ?? _tint;

    // The single-sampled resolve destination, or null at one sample
    internal VkGpuTexture? TintLocate => _multisampleTint is null ? null : _tint;

    // The image written as the pass's depth/stencil attachment
    internal VkGpuTexture? ZDepthAffix => _multisampleDepth ?? _depth;

    // The sampleable depth resolve destination, or null when no resolve is required
    internal VkGpuTexture? DepthResolve =>
        Description.SampleableDepth && _multisampleDepth is not null ? _depth : null;

    private readonly VkGpuTexture _tint;

    internal VkGpuTexture TintOutcome => _tint;
    private readonly VkGpuTexture? _depth;

    internal VkGpuTexture? ZDepthOutcome => _depth;
    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _multisampleDepth?.Dispose();
        _depth?.Dispose();
        _multisampleTint?.Dispose();
        _tint.Dispose();
    }
}

internal sealed unsafe class VkBackbufferAttachments : IDisposable
{
    private readonly Silk.NET.Vulkan.Vk _vk;
    private readonly Device _device;
    private readonly VkDeviceMemoryAllotter _allocator;
    private readonly VkDebugNames _diagLabels;

    private Image _tintImage;
    private ImageView _tintLens;
    private VkAllocation _tintAlloc;
    private Image _zDepthImage;
    private ImageView _zDepthLens;
    private VkAllocation _zDepthAlloc;
    private bool _destroyed;

    internal VkBackbufferAttachments(
        Silk.NET.Vulkan.Vk vk,
        Device dev,
        VkDeviceMemoryAllotter allocator,
        VkDebugNames debugNames,
        Format zDepthStencilFmt)
    {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        _device = dev;
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _diagLabels = debugNames ?? throw new ArgumentNullException(nameof(debugNames));
        ZDepthStencilFmt = zDepthStencilFmt;
    }

    internal Format ZDepthStencilFmt { get; }

    internal uint Width { get; private set; }

    internal uint Height { get; private set; }

    internal int SampleCount { get; private set; } = 1;

    internal Format TintFmt { get; private set; }

    internal ImageView TintLens => _tintLens;

    internal ImageView ZDepthLens => _zDepthLens;

    internal Image TintImage => _tintImage;

    internal Image ZDepthImage => _zDepthImage;

    internal bool HasMultisampledTint => _tintLens.Handle != 0;

    internal bool HasZDepth => _zDepthLens.Handle != 0;

    internal bool TintArrangementInitialized { get; private set; }

    internal bool ZDepthArrangementInitialized { get; private set; }

    internal void FlagTintArrangementInitialized() => TintArrangementInitialized = true;

    internal void FlagZDepthArrangementInitialized() => ZDepthArrangementInitialized = true;

    internal void Configure(uint width, uint height, Format tintFmt, int specimenTally)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (width == 0 || height == 0)
            return;
        if (width == Width && height == Height && tintFmt == TintFmt && specimenTally == SampleCount)
            return;

        DemolishImages();
        Width = width;
        Height = height;
        TintFmt = tintFmt;
        SampleCount = Math.Max(1, specimenTally);

        if (SampleCount > 1)
        {
            (_tintImage, _tintAlloc, _tintLens) = BuildAffix(
                tintFmt,
                ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit,
                ImageAspectFlags.ColorBit,
                "vk-backbuffer-msaa-color");
        }

        if (ZDepthStencilFmt != Format.Undefined)
        {
            (_zDepthImage, _zDepthAlloc, _zDepthLens) = BuildAffix(
                ZDepthStencilFmt,
                ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransientAttachmentBit,
                ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
                "vk-backbuffer-depth");
        }
    }

    private (Image, VkAllocation, ImageView) BuildAffix(
        Format fmt,
        ImageUsageFlags usage,
        ImageAspectFlags aspect,
        string label)
    {
        var build = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = fmt,
            Extent = new Extent3D(Width, Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = VkTextureFormatMapping.ProbeTallyOf(SampleCount),
            Tiling = ImageTiling.Optimal,
            Usage = usage,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        VkInterop.Check(_vk.CreateImage(_device, &build, null, out Image image), $"vkCreateImage ({label})");

        _vk.GetImageMemoryRequirements(_device, image, out MemoryRequirements requirements);
        VkAllocation alloc = _allocator.Reserve(
            requirements,
            GpuMemoryTenancy.DeviceLocal,
            label);
        VkInterop.Check(
            _vk.BindImageMemory(_device, image, alloc.Memory, alloc.ShiftOctets),
            $"vkBindImageMemory ({label})");

        var lensBuild = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = fmt,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = aspect,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        VkInterop.Check(
            _vk.CreateImageView(_device, &lensBuild, null, out ImageView lens),
            $"vkCreateImageView ({label})");

        _diagLabels.LabelImage(image, label);
        _diagLabels.LabelImageLens(lens, $"{label}-view");
        return (image, alloc, lens);
    }

    private void DemolishImages()
    {
        if (_tintLens.Handle != 0)
            _vk.DestroyImageView(_device, _tintLens, null);
        if (_tintImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _tintImage, null);
            _allocator.Release(_tintAlloc);
        }

        if (_zDepthLens.Handle != 0)
            _vk.DestroyImageView(_device, _zDepthLens, null);
        if (_zDepthImage.Handle != 0)
        {
            _vk.DestroyImage(_device, _zDepthImage, null);
            _allocator.Release(_zDepthAlloc);
        }

        _tintLens = default;
        _tintImage = default;
        _tintAlloc = default;
        _zDepthLens = default;
        _zDepthImage = default;
        _zDepthAlloc = default;
        TintArrangementInitialized = false;
        ZDepthArrangementInitialized = false;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        DemolishImages();
    }
}
