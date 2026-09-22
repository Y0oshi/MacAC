using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class VkTextureFormatMapping
{
    internal const Format CanonTintAffixFmt = Format.B8G8R8A8Unorm;

    // The Vulkan format macac uploads this surface as
    internal static Format ComposeOf(GpuBitmapFmt format)
    {
        return format switch
        {
            GpuBitmapFmt.Rgba8Unorm => Format.R8G8B8A8Unorm,
            GpuBitmapFmt.R8Unorm => Format.R8Unorm,
            GpuBitmapFmt.Bc1Unorm => Format.BC1RgbaUnormBlock,
            GpuBitmapFmt.Bc2Unorm => Format.BC2UnormBlock,
            GpuBitmapFmt.Bc3Unorm => Format.BC3UnormBlock,
            GpuBitmapFmt.Rgba8UnormRenderTarget => CanonTintAffixFmt,
            GpuBitmapFmt.Rgba16FloatRenderTarget => Format.R16G16B16A16Sfloat,
            GpuBitmapFmt.Depth24Stencil8 => Format.D24UnormS8Uint,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognized texture format"),
        };
    }

    internal static bool IsZDepthStencil(GpuBitmapFmt fmt) =>
        fmt == GpuBitmapFmt.Depth24Stencil8;

    internal static bool IsRasterizeMark(GpuBitmapFmt fmt)
    {
        return fmt is GpuBitmapFmt.Rgba8UnormRenderTarget
            or GpuBitmapFmt.Rgba16FloatRenderTarget
            or GpuBitmapFmt.Depth24Stencil8;
    }

    // Bytes one texel occupies
    internal static int OctetsPerTexel(GpuBitmapFmt format)
    {
        return format switch
        {
            GpuBitmapFmt.Rgba8Unorm or GpuBitmapFmt.Rgba8UnormRenderTarget => 4,
            GpuBitmapFmt.Rgba16FloatRenderTarget => 8,
            GpuBitmapFmt.R8Unorm => 1,
            GpuBitmapFmt.Depth24Stencil8 => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "A block-compressed format has no texel size"),
        };
    }

    // Bytes one mip level of one array layer occupies
    internal static int TierByteSize(GpuBitmapFmt fmt, int width, int height)
    {
        return ChunkCompressionCodec.IsChunkCompressed(fmt)
            ? ChunkCompressionCodec.TierByteSize(fmt, width, height)
            : width * height * OctetsPerTexel(fmt);
    }

    // Dimensions of mip level tier, floored at 1 texel
    internal static (int Width, int Height) TierReach(int width, int height, int tier)
    {
        for (int idx = 0; idx < tier; ++idx)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        return (width, height);
    }

    internal static int WholeMipTierTally(int width, int height)
    {
        int tiers = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            ++tiers;
        }

        return tiers;
    }

    internal static ImageAspectFlags AspectOf(GpuBitmapFmt fmt)
    {
        return IsZDepthStencil(fmt)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.ColorBit;
    }

    internal static SampleCountFlags ProbeTallyOf(int specimenTally)
    {
        return specimenTally switch
        {
            <= 1 => SampleCountFlags.Count1Bit,
            2 => SampleCountFlags.Count2Bit,
            <= 4 => SampleCountFlags.Count4Bit,
            _ => SampleCountFlags.Count8Bit,
        };
    }

    internal static ImageViewType LensKindOf(GpuBitmapFlavor kind)
    {
        return kind switch
        {
            GpuBitmapFlavor.Texture2D => ImageViewType.Type2D,
            GpuBitmapFlavor.Texture2DArray => ImageViewType.Type2DArray,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized texture kind"),
        };
    }

    internal static ImageViewType SampledLensKindOf(GpuBitmapFlavor kind)
    {
        return kind switch
        {
            GpuBitmapFlavor.Texture2D or GpuBitmapFlavor.Texture2DArray => ImageViewType.Type2DArray,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized texture kind"),
        };
    }

    internal static Filter SiftOf(GpuSift filter)
    {
        return filter switch
        {
            GpuSift.Nearest => Filter.Nearest,
            GpuSift.Linear => Filter.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unrecognized filter"),
        };
    }

    internal static SamplerMipmapMode MipmapMannerOf(GpuMipSift filter)
    {
        return filter switch
        {
            GpuMipSift.None or GpuMipSift.Nearest => SamplerMipmapMode.Nearest,
            GpuMipSift.Linear => SamplerMipmapMode.Linear,
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, "Unrecognized mip filter"),
        };
    }

    internal static SamplerAddressMode AddressMannerOf(GpuAddressManner mode)
    {
        return mode switch
        {
            GpuAddressManner.Repeat => SamplerAddressMode.Repeat,
            GpuAddressManner.ClampToEdge => SamplerAddressMode.ClampToEdge,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unrecognized address mode"),
        };
    }
}
