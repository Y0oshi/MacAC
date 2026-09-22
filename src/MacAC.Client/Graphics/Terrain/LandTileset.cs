using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Graphics;

public sealed partial class LandTileset : IDisposable
{
    internal static readonly GpuSamplerSpec SpecificsSamplerBlurb =
        GpuSamplerSpec.RealmRepeat;

    internal readonly record struct CanonDetailTextureWiring(
        GpuTextureSlot TextureSlot,
        float Tiling,
        uint SurfaceTextureId,
        uint RenderSurfaceId,
        int Width,
        int Height)
    {
        public bool IsAvailable
        {
            get
            {
                return TextureSlot.IsAssigned
            && SurfaceTextureId is not 0
            && RenderSurfaceId is not 0;
            }
        }
    }

    private sealed record SpecificsBitmapAsset(
        IGpuBitmap Texture,
        CanonDetailTextureWiring Binding);

    private sealed class RhiArrs(
        IClientGpuDevice dev,
        IGpuBitmap land,
        IGpuBitmap alpha,
        IClientGpuSampler alphaSampler)
    {
        public IClientGpuDevice Device { get; } = dev;
        public IGpuBitmap Terrain { get; } = land;
        public IGpuBitmap Alpha { get; } = alpha;
        public IClientGpuSampler AlphaSampler { get; } = alphaSampler;
        public IClientGpuSampler? LandSampler { get; set; }
        public IGpuBitmap? StructureDetailTexture { get; set; }
        public IGpuBitmap? SurroundingsDetailTexture { get; set; }
        public GpuTextureSlot LandSocket { get; set; } = GpuTextureSlot.Unassigned;
        public GpuTextureSlot AlphaSocket { get; set; } = GpuTextureSlot.Unassigned;
    }

    private readonly RhiArrs _rhi;

    private const float CanonUpperAnisotropy = 16f;

    private LandTileset(
        IClientGpuDevice dev,
        IGpuBitmap land,
        IGpuBitmap alpha,
        IClientGpuSampler alphaSampler,
        IReadOnlyDictionary<uint, uint> lookup,
        int stratumTally,
        IReadOnlyList<float> tilingByStratum,
        int alphaStratumTally,
        IReadOnlyList<byte> cornerStrata,
        IReadOnlyList<byte> flankStrata,
        IReadOnlyList<byte> roadStrata,
        IReadOnlyList<uint> cornerTCodes,
        IReadOnlyList<uint> flankTCodes,
        IReadOnlyList<uint> roadRCodes,
        SpecificsBitmapAsset? structureSpecifics,
        SpecificsBitmapAsset? surroundingsSpecifics)
    {
        _rhi = new RhiArrs(dev, land, alpha, alphaSampler);
        LandKindToStratum = lookup;
        StratumCount = stratumTally;
        TilingByStratum = tilingByStratum;
        AlphaStratumTally = alphaStratumTally;
        CornerAlphaStrata = cornerStrata;
        FlankAlphaStrata = flankStrata;
        RoadAlphaStrata = roadStrata;
        CornerAlphaTCodes = cornerTCodes;
        FlankAlphaTCodes = flankTCodes;
        RoadAlphaRCodes = roadRCodes;
        _rhi.StructureDetailTexture = structureSpecifics?.Texture;
        _rhi.SurroundingsDetailTexture = surroundingsSpecifics?.Texture;
        StructureSpecificsTexture = structureSpecifics?.Binding ?? default;
        SurroundingsSpecificsTexture = surroundingsSpecifics?.Binding ?? default;
        _rhi.AlphaSocket = dev.EnrollTexture(alpha, alphaSampler);
        ImposeAnisotropic(CanonUpperAnisotropy);
    }

    private readonly record struct LandscapeLayerDecode(
        Dictionary<uint, UnpackedTexture> DecodedByType,
        Dictionary<uint, uint> TilingByType,
        int MaxWidth,
        int MaxHeight);

    private sealed record AlphaStratumUnpack(
        List<UnpackedTexture> Decoded,
        List<byte> CornerLayers,
        List<byte> SideLayers,
        List<byte> RoadLayers,
        List<uint> CornerTCodes,
        List<uint> SideTCodes,
        List<uint> RoadRCodes,
        int MaxWidth,
        int MaxHeight);

    private bool _destroyed;
}
