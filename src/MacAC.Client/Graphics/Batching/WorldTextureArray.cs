using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Gpu.Vulkan;
using MacAC.Mechanics.Drawing.Batches;
using Microsoft.Extensions.Logging;

namespace MacAC.Client.Graphics.Batching;

internal interface IRealmTextureArray : IDisposable
{
    // Array layers allocated
    int Size { get; }

    long SumDimsInOctets { get; }

    int QueuedRefreshTally { get; }

    bool HasDurableTeardownOwnership { get; }

    bool IsPhysicalSunsetDone { get; }

    void RefreshStratum(int stratum, byte[] blob, PushPixelFmt? pushPixelFmt, PushPixelKind? pushPixelKind);

    long HandleStaleUpdates();

    GpuTextureSlot LocateSocket(bool wrapping);

    void FreeTextureSockets();
}

internal interface IRealmTextureArrayMint
{
    internal static IRealmTextureArrayMint For(
        ITriMeshPipeDevice visualsDev,
        IClientGpuDevice gpuDev,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(visualsDev);
        ArgumentNullException.ThrowIfNull(gpuDev);
        ArgumentNullException.ThrowIfNull(logger);
        return new RhiRealmTextureArrayMint(gpuDev);
    }

    // The retirement queue array layers and images are released through
    IGpuAssetSunsetFifo Retirement { get; }

    IRealmTextureArray BuildClampedArr(TexelLayout fmt, int width, int height, int strata);
}

internal sealed class RhiRealmTextureArrayMint(IClientGpuDevice device) : IRealmTextureArrayMint
{
    private readonly IClientGpuDevice _device = device ?? throw new ArgumentNullException(nameof(device));

    public IGpuAssetSunsetFifo Retirement => _device.Retirement;

    public IRealmTextureArray BuildClampedArr(TexelLayout fmt, int width, int height, int strata) =>
        new RhiRealmTextureArray(_device, fmt, width, height, strata);
}

internal sealed class RhiRealmTextureArray : IRealmTextureArray
{
    private const float RealmArrAnisotropy = 16f;

    private readonly IClientGpuDevice _device;
    private readonly IGpuBitmap _texture;
    private readonly GpuBitmapFmt _fmt;
    private readonly int _width;
    private readonly int _height;
    private readonly int _mipTierTally;
    private readonly List<QueuedStratum> _queued = [];
    private readonly Lock _latch = new();

    private GpuTextureSlot _encloseSocket = GpuTextureSlot.Unassigned;
    private GpuTextureSlot _clampSocket = GpuTextureSlot.Unassigned;

    private readonly record struct QueuedStratum(int Layer, byte[] Data);

    internal RhiRealmTextureArray(
        IClientGpuDevice dev,
        TexelLayout fmt,
        int width,
        int height,
        int strata)
    {
        ArgumentNullException.ThrowIfNull(dev);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(strata, 1);

        _device = dev;
        SrcFmt = fmt;
        _fmt = ChartFmt(fmt);
        _width = width;
        _height = height;
        Size = strata;
        _mipTierTally = MipTiersFor(width, height);
        SumDimsInOctets = checked(
            TextureAtlasKeeper.DeriveMipChainOctets(width, height, fmt) * strata);

        IGpuBitmap? texture = null;
        try
        {
            texture = dev.BuildTexture(new GpuBitmapSpec(
                $"world-atlas-{fmt}-{width}x{height}x{strata}",
                GpuBitmapFlavor.Texture2DArray,
                _fmt,
                width,
                height,
                strata,
                _mipTierTally));
            _texture = texture;

            _clampSocket = dev.EnrollTexture(
                texture,
                dev.BuildSampler(GpuSamplerSpec.RealmClamp with
                {
                    MaxAnisotropy = RealmArrAnisotropy,
                }));
            _encloseSocket = dev.EnrollTexture(
                texture,
                dev.BuildSampler(GpuSamplerSpec.RealmRepeat with
                {
                    MaxAnisotropy = RealmArrAnisotropy,
                }));
        }
        catch
        {
            FreeSocketsQuietly();
            texture?.Dispose();
            throw;
        }
    }

    public int Size { get; }

    public long SumDimsInOctets { get; }

    public int QueuedRefreshTally
    {
        get
        {
            lock (_latch)
                return _queued.Count;
        }
    }

    public bool HasDurableTeardownOwnership => IsPhysicalSunsetDone;

    public bool IsPhysicalSunsetDone { get; private set; }

    public void RefreshStratum(int stratum, byte[] blob, PushPixelFmt? pushPixelFmt, PushPixelKind? pushPixelKind)
    {
        ObjectDisposedException.ThrowIf(IsPhysicalSunsetDone, this);
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentOutOfRangeException.ThrowIfNegative(stratum);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(stratum, Size);
        VetPushCargo(
            SrcFmt,
            _width,
            _height,
            blob.Length,
            pushPixelFmt,
            pushPixelKind);

        lock (_latch)
        {
            int extant = _queued.FindLastIndex(p => p.Layer == stratum);
            QueuedStratum refresh = new QueuedStratum(stratum, blob);
            if (extant >= 0)
                _queued[extant] = refresh;
            else
                _queued.Add(refresh);
        }
    }

    public long HandleStaleUpdates()
    {
        ObjectDisposedException.ThrowIf(IsPhysicalSunsetDone, this);
        QueuedStratum[] drain;
        lock (_latch)
        {
            if (_queued.Count is 0)
                return 0;
            drain = [.. _queued];
        }

        long generated = 0;
        bool compressed = ChunkCompressionCodec.IsChunkCompressed(_fmt);
        foreach (QueuedStratum stratum in drain)
        {
            _texture.Upload(0, stratum.Layer, stratum.Data);
            if (_mipTierTally <= 1)
                continue;
            if (!compressed)
                continue;

            foreach (ChunkCompressionMipSequence.Tier tier in
                ChunkCompressionMipSequence.AssembleCompressed(
                    _fmt,
                    stratum.Data,
                    _width,
                    _height,
                    _mipTierTally))
            {
                _texture.Upload(tier.MipLevel, stratum.Layer, tier.Data);
                generated = checked(generated + tier.Data.Length);
            }
        }

        if (!compressed && _mipTierTally > 1)
        {
            _texture.ProduceMipChain();
            generated = checked(generated + MipChainOctets());
        }

        lock (_latch)
        {
            foreach (QueuedStratum stratum in drain)
            {
                int ordinal = _queued.FindIndex(p => p.Layer == stratum.Layer && ReferenceEquals(p.Data, stratum.Data));
                if (ordinal >= 0)
                    _queued.RemoveAt(ordinal);
            }
        }

        return generated;
    }

    public GpuTextureSlot LocateSocket(bool wrapping) => wrapping ? _encloseSocket : _clampSocket;

    public void FreeTextureSockets() => FreeSocketsQuietly();

    public void Dispose()
    {
        if (IsPhysicalSunsetDone)
            return;
        IsPhysicalSunsetDone = true;
        FreeSocketsQuietly();
        _texture.Dispose();
        lock (_latch)
            _queued.Clear();
    }

    internal TexelLayout SrcFmt { get; }

    internal static int MipTiersFor(int width, int height) =>
        (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height)))) + 1;

    internal static int DeriveAnticipatedBlobDims(TexelLayout fmt, int width, int height)
    {
        return IsCompressedFmt(fmt)
            ? TextureTools.FetchCompressedStratumDims(width, height, fmt)
            : fmt switch
            {
                TexelLayout.RGBA8 => checked(width * height * 4),
                TexelLayout.RGB8 => checked(width * height * 3),
                TexelLayout.A8 => checked(width * height),
                TexelLayout.Rgba32f => checked(width * height * 16),
                _ => throw new NotSupportedException($"Not supported format {fmt}"),
            };
    }

    internal static void VetPushCargo(
        TexelLayout fmt,
        int width,
        int height,
        int dataLength,
        PushPixelFmt? pushPixelFmt,
        PushPixelKind? pushPixelKind)
    {
        int anticipatedOctets = DeriveAnticipatedBlobDims(fmt, width, height);
        if (dataLength != anticipatedOctets)
        {
            throw new ArgumentException(
                $"Texture-array layer payload has {dataLength} bytes; wanted precisely {anticipatedOctets} "
                + $"for {fmt} {width}x{height}.",
                nameof(dataLength));
        }

        if (IsCompressedFmt(fmt))
        {
            if (pushPixelFmt.HasValue || pushPixelKind.HasValue)
                throw new ArgumentException("Compressed texture uploads can't specify pixel format/type overrides");
            return;
        }

        VetPushCargoRest(fmt, pushPixelFmt, pushPixelKind);
    }

    private static void VetPushCargoRest(TexelLayout fmt, PushPixelFmt? pushPixelFmt, PushPixelKind? pushPixelKind)
    {
        var anticipatedFmt = fmt.ToPixelFmt();
        var anticipatedKind = fmt.ToPixelKind();
        if ((pushPixelFmt ?? anticipatedFmt) != anticipatedFmt
                    || (pushPixelKind ?? anticipatedKind) != anticipatedKind)
        {
            throw new ArgumentException(
                $"Upload descriptor {pushPixelFmt}/{pushPixelKind} doesn't match "
                + $"the {anticipatedFmt}/{anticipatedKind} transfer needed by {fmt}.");
        }
    }

    private void FreeSocketsQuietly()
    {
        if (_encloseSocket.IsAssigned)
        {
            _device.FreeTextureSocket(_encloseSocket);
            _encloseSocket = GpuTextureSlot.Unassigned;
        }
        if (_clampSocket.IsAssigned)
        {
            _device.FreeTextureSocket(_clampSocket);
            _clampSocket = GpuTextureSlot.Unassigned;
        }
    }

    private long MipChainOctets()
    {
        return checked(SumDimsInOctets
            - (TextureAtlasKeeper.DeriveTierOctets(_width, _height, SrcFmt) * Size));
    }

    private static GpuBitmapFmt ChartFmt(TexelLayout fmt)
    {
        return fmt switch
        {
            TexelLayout.RGBA8 => GpuBitmapFmt.Rgba8Unorm,
            TexelLayout.DXT1 => GpuBitmapFmt.Bc1Unorm,
            TexelLayout.DXT3 => GpuBitmapFmt.Bc2Unorm,
            TexelLayout.DXT5 => GpuBitmapFmt.Bc3Unorm,
            _ => throw new NotSupportedException(
                $"World texture format {fmt} has no GpuBitmapFmt member. "
                + "RGB8 and Rgba32f are not in the pinned RHI format list, and A8 needs the "
                + "component swizzle the GL array applies, which lives in a Vulkan image view "
                + "and isn't part of GpuTextureDescription. Supporting these formats needs "
                + "extending the contract or proving no such atlas exists"),
        };
    }

    private static bool IsCompressedFmt(TexelLayout fmt) =>
        fmt is TexelLayout.DXT1 or TexelLayout.DXT3 or TexelLayout.DXT5;
}
