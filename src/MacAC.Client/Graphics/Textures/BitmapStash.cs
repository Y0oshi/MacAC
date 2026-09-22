// src/MacAC.Client/Rendering/TextureCache.cs
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Tenancy;

namespace MacAC.Client.Graphics;

public sealed partial class BitmapStash
    : Batching.IActorTextureLifetime,
      IDisposable
{
    private readonly IClientGpuDevice _device;

    private readonly IDatAccess _datFiles;

    private readonly string _telemetryFolder;

    private readonly Dictionary<(uint SurfaceId, uint OrigTextureId), (int Width, int Height)>
        _decodedDimensionsByTexture = [];

    private readonly record struct GpuWidgetTextureEntry(
        IGpuBitmap Texture,
        GpuTextureSlot Slot,
        uint GlName,
        int Width,
        int Height);

    private readonly Dictionary<(uint SurfaceId, bool Nearest), GpuWidgetTextureEntry>
        _rasterizeCanvasGpuTextures = [];

    private readonly HashSet<uint> _loggedAbsentRasterizeCanvasIdents = [];

    private readonly List<GpuWidgetTextureEntry> _adhocGpuTextures = [];

    private readonly Dictionary<uint, IGpuBitmap> _closestWidgetTextureSrcs = [];

    private readonly Dictionary<uint, uint> _linearWidgetTwinHnds = [];

    private readonly CompositeTextureArrayShelf? _compoundTextures;

    private bool _destUnveilPushPrecedence;

    private readonly StandaloneBindlessTextureShelf? _moteTextures;

    private readonly Dictionary<(uint surfaceId, uint origTexOverride), bool> _swatchIndexedByTexture = [];

    private readonly Dictionary<uint, (int Width, int Height, string Format)> _pushMetadata = [];

    private int _printCycleCounter;

    private bool _canvasHistogramAlreadyDumped;

    internal BitmapStash(IClientGpuDevice dev, IDatAccess datFiles)
        : this(
            dev,
            datFiles,
            ImmediateGpuAssetSunsetFifo.Instance,
            Path.Combine(
                Path.GetTempPath(),
                "macac",
                "diagnostics"))
    {
    }

    internal BitmapStash(
        IClientGpuDevice device,
        IDatAccess datFiles,
        IGpuAssetSunsetFifo sunsetFifo,
        string telemetryFolder,
        TenancyAllowanceKnobs? budgets = null)
    {
        budgets ??= TenancyAllowanceKnobs.Default;
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _datFiles = datFiles;
        ArgumentException.ThrowIfNullOrWhiteSpace(telemetryFolder);
        _telemetryFolder = telemetryFolder;
        ArgumentNullException.ThrowIfNull(sunsetFifo);

        AssetTidyCluster assetList = new AssetTidyCluster();
        CompositeTextureArrayShelf? compound = null;
        StandaloneBindlessTextureShelf? motes = null;
        try
        {
            compound = new CompositeTextureArrayShelf(
                new RhiCompoundBitmapArrayBackend(device),
                sunsetFifo,
                budgets.CompositeUnownedBytes,
                budgets.CompositePhysicalBytes);
            assetList.Add("composite texture cache", compound.Dispose);
            motes = new StandaloneBindlessTextureShelf(
                new MoteRhiTextureBackend(this),
                sunsetFifo,
                budgets.StandaloneUnownedBytes,
                budgets.StandaloneUnownedEntries);
            assetList.Add("particle texture cache", motes.Dispose);
            assetList.TransferAll();
        }
        catch (Exception constructionMiss)
        {
            assetList.RevertConstructionAndThrow(
                "BitmapStash construction failed and its child-cache prefix did not cleanly roll back.",
                constructionMiss);
        }

        _compoundTextures = compound;
        _moteTextures = motes;
    }

    private readonly Dictionary<(uint SurfaceId, bool Repeat), GpuWidgetTextureEntry>
        _realmCanvasGpuTextures = [];

    private uint _upcomingSyntheticPushLabel = uint.MaxValue;

    private static readonly GpuSamplerSpec WidgetClosestRepeat = new(
        GpuSift.Nearest,
        GpuSift.Nearest,
        GpuMipSift.None,
        GpuAddressManner.Repeat,
        GpuAddressManner.Repeat,
        MaxAnisotropy: 1f);

    private sealed class MoteRhiTextureBackend(BitmapStash holder)
        : IStandaloneBindlessBitmapBackend
    {
        public void ForgeNonHoused(StandaloneBindlessBitmapAsset asset)
        {
            if (asset.Slot.IsAssigned)
                holder._device.FreeTextureSocket(asset.Slot);
        }

        public void Delete(StandaloneBindlessBitmapAsset asset)
        {
            asset.Texture?.Dispose();
            holder.UntrackUploadedTexture(asset.Name);
        }
    }
}
