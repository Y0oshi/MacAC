namespace MacAC.Client.Graphics.Gpu;

internal readonly record struct GpuBufferSpec(
    string Name,
    long SizeBytes,
    GpuBufferPurpose Usage,
    GpuMemoryTenancy Residency);

internal readonly record struct GpuBitmapSpec(
    string Name,
    GpuBitmapFlavor Kind,
    GpuBitmapFmt Format,
    int Width,
    int Height,
    int LayerCount,
    int MipLevelCount);

internal readonly record struct GpuSamplerSpec(
    GpuSift MinFilter,
    GpuSift MagFilter,
    GpuMipSift MipFilter,
    GpuAddressManner AddressU,
    GpuAddressManner AddressV,
    float MaxAnisotropy)
{
    // Trilinear repeat - the default for world materials
    public static GpuSamplerSpec RealmRepeat { get; } = new(
        GpuSift.Linear,
        GpuSift.Linear,
        GpuMipSift.Linear,
        GpuAddressManner.Repeat,
        GpuAddressManner.Repeat,
        MaxAnisotropy: 1f);

    public static GpuSamplerSpec RealmClamp { get; } = new(
        GpuSift.Linear,
        GpuSift.Linear,
        GpuMipSift.Linear,
        GpuAddressManner.ClampToEdge,
        GpuAddressManner.ClampToEdge,
        MaxAnisotropy: 1f);

    public static GpuSamplerSpec WidgetClosest { get; } = new(
        GpuSift.Nearest,
        GpuSift.Nearest,
        GpuMipSift.None,
        GpuAddressManner.ClampToEdge,
        GpuAddressManner.ClampToEdge,
        MaxAnisotropy: 1f);

    public static GpuSamplerSpec ShadeClosestClamp { get; } = new(
        GpuSift.Nearest,
        GpuSift.Nearest,
        GpuMipSift.Nearest,
        GpuAddressManner.ClampToEdge,
        GpuAddressManner.ClampToEdge,
        MaxAnisotropy: 1f);
}

internal readonly record struct GpuRenderTargetSpec(
    string Name,
    int Width,
    int Height,
    GpuBitmapFmt ColorFormat,
    GpuBitmapFmt? DepthFormat,
    int SampleCount,
    bool SampleableDepth = false);

internal readonly record struct GpuDirectionalDepthTargetSpec(
    string Name,
    int Resolution,
    int LayerCount,
    GpuBitmapFmt DepthFormat = GpuBitmapFmt.Depth24Stencil8);

internal readonly record struct GpuTextureSlot(uint Index)
{
    // Sentinel for "no texture assigned"
    public static GpuTextureSlot Unassigned { get; } = new(uint.MaxValue);

    public bool IsAssigned => Index != uint.MaxValue;

    public override string ToString() =>
        IsAssigned ? $"slot#{Index}" : "slot#unassigned";
}
