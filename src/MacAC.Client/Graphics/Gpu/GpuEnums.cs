namespace MacAC.Client.Graphics.Gpu;

// Which RHI backend is servicing the device
internal enum GpuBackendFlavor
{
    Recording,

    Vulkan,
}

[Flags]
internal enum GpuBufferPurpose
{
    None = 0,
    Vertex = 1 << 0,
    Index = 1 << 1,
    Storage = 1 << 2,
    Uniform = 1 << 3,
    Indirect = 1 << 4,
    TransferSource = 1 << 5,
    TransferDestination = 1 << 6,
}

internal enum GpuMemoryTenancy
{
    // Device-local, written only through staged transfers
    DeviceLocal,

    // Persistently mapped and CPU-writable
    HostWritable,

    // Mapped and CPU-readable
    HostReadable,
}

// Which alignment and usage a per-frame ring allocation must satisfy
internal enum GpuLoopPurpose
{
    Storage,
    Uniform,
    Indirect,
    Vertex,
    Index,
}

internal enum GpuBitmapFmt
{
    Rgba8Unorm,
    R8Unorm,
    Bc1Unorm,
    Bc2Unorm,
    Bc3Unorm,

    Rgba8UnormRenderTarget,

    Rgba16FloatRenderTarget,

    Depth24Stencil8,
}

// Texture shape. macac uses 2D for UI art and 2D arrays for every world material.
internal enum GpuBitmapFlavor
{
    Texture2D,
    Texture2DArray,
}

internal enum GpuSift
{
    Nearest,
    Linear,
}

internal enum GpuMipSift
{
    None,
    Nearest,
    Linear,
}

internal enum GpuAddressManner
{
    Repeat,
    ClampToEdge,
}

internal enum GpuBlendManner
{
    // Opaque: blending disabled
    None,

    // Straight alpha: SrcAlpha, OneMinusSrcAlpha
    StraightAlpha,

    PremultipliedAlpha,

    // Additive: SrcAlpha, One
    Additive,

    RawAdditive,

    InverseAdditive,

    InverseAlpha,
}

internal enum GpuContrastOp
{
    Never,
    Less,
    LessOrEqual,
    Equal,
    Greater,
    GreaterOrEqual,
    Always,
}

internal enum GpuPruneManner
{
    None,
    Back,
    Front,
}

internal enum ClientGpuStencilOp
{
    // Leave the stored value alone
    Keep,

    // Store zero
    Zero,

    // Store the reference value
    Replace,
}

// Triangle winding treated as front-facing
internal enum GpuFrontFacet
{
    CounterClockwise,
    Clockwise,
}

internal enum GpuPrimitiveWiring
{
    TriangleList,
    LineList,
}

internal enum GpuOrdinalKind
{
    UInt16,
    UInt32,
}

internal enum GpuPullOp
{
    DontCare,

    Clear,

    Load,
}

internal enum GpuVaultOp
{
    DontCare,

    Store,

    Resolve,
}
