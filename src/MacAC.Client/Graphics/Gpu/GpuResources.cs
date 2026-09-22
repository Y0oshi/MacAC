namespace MacAC.Client.Graphics.Gpu;

internal interface IClientGpuBuffer : IDisposable
{
    string Name { get; }
    long SizeBytes { get; }
    GpuBufferPurpose Usage { get; }
    GpuMemoryTenancy Residency { get; }

    bool HostWritesAreCoherent { get; }

    void Upload(long shiftOctets, ReadOnlySpan<byte> blob);

    void ReplicateTo(IClientGpuBuffer dest, long srcShiftOctets, long destShiftOctets, long byteTally);

    void Read(long shiftOctets, Span<byte> dest);
}

// A sampled texture or an attachment image
internal interface IGpuBitmap : IDisposable
{
    string Name { get; }
    GpuBitmapFlavor Kind { get; }
    GpuBitmapFmt Format { get; }
    int Width { get; }
    int Height { get; }
    int StratumTally { get; }
    int MipTierTally { get; }

    // Uploads one mip level of one array layer
    void Upload(int mipTier, int stratum, ReadOnlySpan<byte> blob);

    void ProduceMipChain();
}

// Immutable sampler state
internal interface IClientGpuSampler : IDisposable
{
    GpuSamplerSpec Description { get; }
}

internal interface IGpuPipe : IDisposable
{
    GpuPipeSpec Description { get; }
}

internal interface IGpuRasterizeMark : IDisposable
{
    GpuRenderTargetSpec Description { get; }

    IGpuBitmap ColorTexture { get; }

    IGpuBitmap? ZDepthTexture { get; }
}

internal interface IGpuDirectedZDepthMark : IDisposable
{
    GpuDirectionalDepthTargetSpec Description { get; }

    IGpuBitmap ZDepthTexture { get; }
}

internal interface IGpuTickerReservoir
{
    // True when the backend can measure GPU time at all
    bool IsSupported { get; }

    // Milliseconds measured for ambitLabel in the most recent retired frame
    bool TryLocate(string ambitLabel, out double millis);

    bool TryGrabSettled(string ambitLabel, out double millis);
}
