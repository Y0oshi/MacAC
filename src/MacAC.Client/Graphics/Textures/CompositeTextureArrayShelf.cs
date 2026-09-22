using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics;

internal readonly struct BindlessBitmapLocale(GpuTextureSlot socket, uint stratum, GpuTextureSlot repeatSocket) : IEquatable<BindlessBitmapLocale>
{
    private readonly uint _socketPlusOne = socket.IsAssigned ? socket.Index + 1 : 0;
    private readonly uint _repeatSocketPlusOne = repeatSocket.IsAssigned ? repeatSocket.Index + 1 : 0;

    public BindlessBitmapLocale(GpuTextureSlot socket, uint stratum)
        : this(socket, stratum, GpuTextureSlot.Unassigned)
    {
    }

    public static BindlessBitmapLocale Unresolved => default;

    public uint Layer { get; } = stratum;

    public GpuTextureSlot RepeatSocket
    {
        get
        {
            return _repeatSocketPlusOne is 0 ? GpuTextureSlot.Unassigned : new GpuTextureSlot(_repeatSocketPlusOne - 1);
        }
    }

    public GpuTextureSlot SettleSocket(bool wrapping) =>
        IsSettled && wrapping && RepeatSocket.IsAssigned ? RepeatSocket : Slot;

    public bool IsSettled => _socketPlusOne is not 0;

    public GpuTextureSlot Slot
    {
        get
        {
            return _socketPlusOne is 0 ? GpuTextureSlot.Unassigned : new GpuTextureSlot(_socketPlusOne - 1);
        }
    }

    public bool Equals(BindlessBitmapLocale another)
    {
        return _socketPlusOne == another._socketPlusOne && Layer == another.Layer && RepeatSocket == another.RepeatSocket;
    }

    public override bool Equals(object? objRef) =>
        objRef is BindlessBitmapLocale another && Equals(another);

    public override int GetHashCode() => HashCode.Combine(_socketPlusOne, Layer, RepeatSocket);

    public static bool operator ==(BindlessBitmapLocale left, BindlessBitmapLocale right) =>
        left.Equals(right);

    public static bool operator !=(BindlessBitmapLocale left, BindlessBitmapLocale right) =>
        !left.Equals(right);

    public override string ToString() =>
        IsSettled ? $"{Slot}/layer{Layer}" : "unresolved";
}

internal enum CompoundBitmapFlavor : byte
{
    OriginalTextureOverride,
    PaletteComposite,
}

internal readonly struct SwatchCompoundPersona : IEquatable<SwatchCompoundPersona>
{
    private readonly IReadOnlyList<SwatchOverride.SubPaletteSpan>? _spans;

    public SwatchCompoundPersona(SwatchOverride swatch, ulong digest)
    {
        ArgumentNullException.ThrowIfNull(swatch);
        BaseSwatchIdent = swatch.BasePaletteId;
        Digest = digest;
        _spans = swatch.SubPalettes;
    }

    public uint BaseSwatchIdent { get; }
    public ulong Digest { get; }
    public int SpanTally => _spans?.Count ?? 0;

    public bool Equals(SwatchCompoundPersona another)
    {
        if (Digest != another.Digest
            || BaseSwatchIdent != another.BaseSwatchIdent
            || SpanTally != another.SpanTally)

            return false;

        for (int idx = 0; idx < SpanTally; ++idx)
            if (_spans![idx] != another._spans![idx])
                return false;
        return true;
    }

    public override bool Equals(object? objRef) =>
        objRef is SwatchCompoundPersona another && Equals(another);

    public override int GetHashCode() => HashCode.Combine(BaseSwatchIdent, Digest, SpanTally);

    public static bool operator ==(SwatchCompoundPersona left, SwatchCompoundPersona right) =>
        left.Equals(right);

    public static bool operator !=(SwatchCompoundPersona left, SwatchCompoundPersona right) =>
        !left.Equals(right);
}

internal readonly record struct CompoundBitmapTag(
    CompoundBitmapFlavor Kind,
    uint SurfaceId,
    uint OrigTextureOverride,
    SwatchCompoundPersona Palette);

internal sealed class CompoundBitmapArrayAsset
{
    // The GL texture name, or 0 on the backend-neutral arm
    public required uint Name { get; init; }

    // The resident bindless handle, or 0 on the backend-neutral arm
    public required ulong Handle { get; init; }

    public Gpu.IGpuBitmap? Image { get; init; }

    public required GpuTextureSlot Slot { get; init; }
    public GpuTextureSlot RepeatSlot { get; init; } = GpuTextureSlot.Unassigned;
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Capacity { get; init; }
    public required long Octets { get; init; }
}

internal interface ICompoundBitmapArrayBackend
{
    int CeilingArrStrata { get; }
    CompoundBitmapArrayAsset Create(int width, int height, int cap);
    void Upload(CompoundBitmapArrayAsset asset, int stratum, byte[] rgba);
    void CraftNonHoused(CompoundBitmapArrayAsset asset);
    void Delete(CompoundBitmapArrayAsset asset);
}

internal sealed class RhiCompoundBitmapArrayBackend : ICompoundBitmapArrayBackend
{
    private readonly Gpu.IClientGpuDevice _device;
    private readonly Gpu.IClientGpuSampler _sampler;
    private readonly Gpu.IClientGpuSampler _repeatSampler;

    internal RhiCompoundBitmapArrayBackend(Gpu.IClientGpuDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _repeatSampler = device.BuildSampler(Gpu.GpuSamplerSpec.RealmRepeat with
        {
            MipFilter = Gpu.GpuMipSift.None,
        });
        _sampler = device.BuildSampler(Gpu.GpuSamplerSpec.RealmClamp with
        {
            MipFilter = Gpu.GpuMipSift.None,
        });
    }

    public int CeilingArrStrata
    {
        get
        {
            return checked((int)Math.Min(
        _device.Capabilities.UpperImageArrStrata,
        (uint)int.MaxValue));
        }
    }

    public CompoundBitmapArrayAsset Create(int width, int height, int cap)
    {
        Gpu.IGpuBitmap? image = null;
        var socket = GpuTextureSlot.Unassigned;
        var repeatSocket = GpuTextureSlot.Unassigned;
        try
        {
            image = _device.BuildTexture(new Gpu.GpuBitmapSpec(
                $"composite-array-{width}x{height}x{cap}",
                Gpu.GpuBitmapFlavor.Texture2DArray,
                Gpu.GpuBitmapFmt.Rgba8Unorm,
                width,
                height,
                cap,
                MipLevelCount: 1));
            socket = _device.EnrollTexture(image, _sampler);
            repeatSocket = _device.EnrollTexture(image, _repeatSampler);
            return new CompoundBitmapArrayAsset
            {
                Name = 0,
                Handle = 0,
                Image = image,
                Slot = socket,
                RepeatSlot = repeatSocket,
                Width = width,
                Height = height,
                Capacity = cap,
                Octets = checked((long)width * height * 4L * cap),
            };
        }
        catch
        {
            if (repeatSocket.IsAssigned)
                _device.FreeTextureSocket(repeatSocket);
            if (socket.IsAssigned)
                _device.FreeTextureSocket(socket);
            image?.Dispose();
            throw;
        }
    }

    public void Upload(CompoundBitmapArrayAsset asset, int stratum, byte[] rgba) =>
        DemandImage(asset).Upload(0, stratum, rgba);

    // On GL this makes a bindless handle non-resident after retiring its table entry
    public void CraftNonHoused(CompoundBitmapArrayAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Slot.IsAssigned)
            _device.FreeTextureSocket(asset.Slot);
        if (asset.RepeatSlot.IsAssigned)
            _device.FreeTextureSocket(asset.RepeatSlot);
    }

    public void Delete(CompoundBitmapArrayAsset asset) => DemandImage(asset).Dispose();

    private static Gpu.IGpuBitmap DemandImage(CompoundBitmapArrayAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return asset.Image
            ?? throw new InvalidOperationException(
                "This composite resource was created by the GL backend and has no RHI image");
    }
}

internal sealed partial class CompositeTextureArrayShelf : IDisposable
{
    internal const long DefaultUnownedAllowanceOctets = 64L * 1024 * 1024;

    internal const long DefaultPhysicalAllowanceOctets = 128L * 1024 * 1024;

    internal const long MarkArrOctets = 4L * 1024 * 1024;

    internal const int CeilingStrataPerArr = 64;

    internal const int DefaultCeilingUploadsPerCycle = 16;

    internal const int DestUnveilCeilingUploadsPerCycle = 64;

    internal const long DefaultCeilingPushOctetsPerCycle = 8L * 1024 * 1024;

    internal const int CeilingLogicalEvictionsPerCycle = 16;

    internal const int CeilingTilesetCreationsPerCycle = 1;

    private readonly ICompoundBitmapArrayBackend _backend;

    private readonly GpuSunsetRegister _sunsetRegister;

    private readonly HolderScopedAssetRegistry<CompoundBitmapTag> _holders = new();

    private readonly BoundedUnownedResourceShelf<CompoundBitmapTag> _unowned;
    private readonly int _ceilingArrStrata;
    private readonly long _ceilingPushOctetsPerCycle;

    private readonly Dictionary<CompoundBitmapTag, EntryUnit> _listings = [];

    private readonly Dictionary<(int Width, int Height), List<Tileset>> _tilesetsByDims = [];

    private readonly List<Tileset> _tilesets = [];
    private long _useSeries;
    private int _cycleTilesetCreationTally;
    private bool _destUnveilPushPrecedence;

    private int _queuedTilesetWidth;

    private int _queuedTilesetHeight;

    private long _queuedTilesetAllocOctets;

    private readonly List<CompoundBitmapTag> _evictionTemp = new(CeilingLogicalEvictionsPerCycle);

    private bool _teardownAsked;

    private bool _destroyed;

    private sealed class EntryUnit
    {
        public required Tileset Atlas { get; init; }
        public required int Layer { get; init; }
        public required long Bytes { get; init; }
    }

    private sealed class Tileset
    {
        public required CompoundBitmapArrayAsset Resource { get; init; }
        public required Batching.TextureAtlasSlotAllotter Slots { get; init; }
        public int ListingCount { get; set; }
        public int QueuedRetirements { get; set; }
        public long PreviousUseSeries { get; set; }
        public bool FreeAsked { get; set; }
        public TilesetFreeJuncture FreeJuncture { get; set; }

        public int OnHandStrata => Slots.OnHandTally;
        public bool IsGpuSafeVacant => ListingCount is 0 && QueuedRetirements is 0;
        public bool IsReusable => !FreeAsked && FreeJuncture == TilesetFreeJuncture.Resident;
        public bool Deleted => FreeJuncture >= TilesetFreeJuncture.Deleted;
    }

    private enum TilesetFreeJuncture : byte
    {
        Resident,
        NonResident,
        Deleted,
        Accounted,
    }

    internal CompositeTextureArrayShelf(
        ICompoundBitmapArrayBackend backend,
        IGpuAssetSunsetFifo sunsetFifo,
        long unownedAllowanceOctets = DefaultUnownedAllowanceOctets,
        long physicalAllowanceOctets = DefaultPhysicalAllowanceOctets,
        int ceilingUploadsPerCycle = DefaultCeilingUploadsPerCycle,
        long ceilingPushOctetsPerCycle = DefaultCeilingPushOctetsPerCycle)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        ArgumentNullException.ThrowIfNull(sunsetFifo);
        _sunsetRegister = new GpuSunsetRegister(sunsetFifo);
        ArgumentOutOfRangeException.ThrowIfNegative(physicalAllowanceOctets);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingUploadsPerCycle, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(ceilingPushOctetsPerCycle, 1);
        _unowned = new BoundedUnownedResourceShelf<CompoundBitmapTag>(unownedAllowanceOctets);
        _physicalAllowanceOctets = physicalAllowanceOctets;
        LatestCeilingUploadsPerCycle = ceilingUploadsPerCycle;
        _ceilingPushOctetsPerCycle = ceilingPushOctetsPerCycle;
        _ceilingArrStrata = Math.Max(
            1,
            Math.Min(backend.CeilingArrStrata, CeilingStrataPerArr));
    }
}
