using Silk.NET.Vulkan;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal static class VkViewportMapping
{
    internal static Viewport ToVulkan(
        int x,
        int y,
        int width,
        int height,
        uint affixHeight,
        float lowerZDepth = 0f,
        float upperZDepth = 1f)
    {
        return new()
        {
            X = x,
            Y = affixHeight - (float)y,
            Width = width,
            Height = -height,
            MinDepth = lowerZDepth,
            MaxDepth = upperZDepth,
        };
    }

    internal static Rect2D ScissorToVulkan(int x, int y, int width, int height, uint affixHeight)
    {
        int top = (int)affixHeight - (y + height);
        int clampedTop = Math.Max(0, top);
        int clampedHeight = Math.Max(0, Math.Min(height + Math.Min(0, top), (int)affixHeight - clampedTop));
        int clampedX = Math.Max(0, x);
        int clampedWidth = Math.Max(0, width + Math.Min(0, x));
        return new Rect2D(
            new Offset2D(clampedX, clampedTop),
            new Extent2D((uint)clampedWidth, (uint)clampedHeight));
    }

    internal static FrontFace ToVulkan(GpuFrontFacet frontFace)
    {
        return frontFace switch
        {
            GpuFrontFacet.CounterClockwise => FrontFace.CounterClockwise,
            GpuFrontFacet.Clockwise => FrontFace.Clockwise,
            _ => throw new ArgumentOutOfRangeException(nameof(frontFace), frontFace, "Unrecognized winding"),
        };
    }

    internal static CullModeFlags ToVulkan(GpuPruneManner cullMode)
    {
        return cullMode switch
        {
            GpuPruneManner.None => CullModeFlags.None,
            GpuPruneManner.Back => CullModeFlags.BackBit,
            GpuPruneManner.Front => CullModeFlags.FrontBit,
            _ => throw new ArgumentOutOfRangeException(nameof(cullMode), cullMode, "Unrecognized cull mode"),
        };
    }

    internal static CompareOp ToVulkan(GpuContrastOp compare)
    {
        return compare switch
        {
            GpuContrastOp.Never => CompareOp.Never,
            GpuContrastOp.Less => CompareOp.Less,
            GpuContrastOp.LessOrEqual => CompareOp.LessOrEqual,
            GpuContrastOp.Equal => CompareOp.Equal,
            GpuContrastOp.Greater => CompareOp.Greater,
            GpuContrastOp.GreaterOrEqual => CompareOp.GreaterOrEqual,
            GpuContrastOp.Always => CompareOp.Always,
            _ => throw new ArgumentOutOfRangeException(nameof(compare), compare, "Unrecognized compare op"),
        };
    }

    internal static PrimitiveTopology ToVulkan(GpuPrimitiveWiring topology)
    {
        return topology switch
        {
            GpuPrimitiveWiring.TriangleList => PrimitiveTopology.TriangleList,
            GpuPrimitiveWiring.LineList => PrimitiveTopology.LineList,
            _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unrecognized topology"),
        };
    }

    internal static IndexType ToVulkan(GpuOrdinalKind indexType)
    {
        return indexType switch
        {
            GpuOrdinalKind.UInt16 => IndexType.Uint16,
            GpuOrdinalKind.UInt32 => IndexType.Uint32,
            _ => throw new ArgumentOutOfRangeException(nameof(indexType), indexType, "Unrecognized index type"),
        };
    }

    internal static Format ToVulkan(GpuVertFmt format)
    {
        return format switch
        {
            GpuVertFmt.Float1 => Format.R32Sfloat,
            GpuVertFmt.Float2 => Format.R32G32Sfloat,
            GpuVertFmt.Float3 => Format.R32G32B32Sfloat,
            GpuVertFmt.Float4 => Format.R32G32B32A32Sfloat,
            GpuVertFmt.UByte4Normalized => Format.R8G8B8A8Unorm,
            GpuVertFmt.UByte4UInt => Format.R8G8B8A8Uint,
            GpuVertFmt.UInt1 => Format.R32Uint,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unrecognized vertex format"),
        };
    }

    internal static StencilOp ToVulkan(ClientGpuStencilOp op)
    {
        return op switch
        {
            ClientGpuStencilOp.Keep => StencilOp.Keep,
            ClientGpuStencilOp.Zero => StencilOp.Zero,
            ClientGpuStencilOp.Replace => StencilOp.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unrecognized stencil operation"),
        };
    }

    internal static (BlendFactor Source, BlendFactor Destination) BlendFactorsOf(GpuBlendManner blend)
    {
        return blend switch
        {
            GpuBlendManner.StraightAlpha => (BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha),
            GpuBlendManner.PremultipliedAlpha => (BlendFactor.One, BlendFactor.OneMinusSrcAlpha),
            GpuBlendManner.Additive => (BlendFactor.SrcAlpha, BlendFactor.One),
            GpuBlendManner.RawAdditive => (BlendFactor.One, BlendFactor.One),
            GpuBlendManner.InverseAdditive => (BlendFactor.OneMinusSrcAlpha, BlendFactor.One),
            GpuBlendManner.InverseAlpha => (BlendFactor.OneMinusSrcAlpha, BlendFactor.SrcAlpha),
            GpuBlendManner.None => (BlendFactor.One, BlendFactor.Zero),
            _ => throw new ArgumentOutOfRangeException(nameof(blend), blend, "Unrecognized blend mode"),
        };
    }

    internal static AttachmentLoadOp ToVulkan(GpuPullOp load)
    {
        return load switch
        {
            GpuPullOp.DontCare => AttachmentLoadOp.DontCare,
            GpuPullOp.Clear => AttachmentLoadOp.Clear,
            GpuPullOp.Load => AttachmentLoadOp.Load,
            _ => throw new ArgumentOutOfRangeException(nameof(load), load, "Unrecognized load op"),
        };
    }

    internal static AttachmentStoreOp ToVulkan(GpuVaultOp store)
    {
        return store switch
        {
            GpuVaultOp.DontCare or GpuVaultOp.Resolve => AttachmentStoreOp.DontCare,
            GpuVaultOp.Store => AttachmentStoreOp.Store,
            _ => throw new ArgumentOutOfRangeException(nameof(store), store, "Unrecognized store op"),
        };
    }
}
