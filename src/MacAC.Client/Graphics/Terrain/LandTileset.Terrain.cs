using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Graphics;

public sealed partial class LandTileset
{
    public IReadOnlyDictionary<uint, uint> LandKindToStratum { get; }

    public int StratumCount { get; }

    public IReadOnlyList<float> TilingByStratum { get; }

    public int AlphaStratumTally { get; }

    public IReadOnlyList<byte> CornerAlphaStrata { get; }

    public IReadOnlyList<uint> CornerAlphaTCodes { get; }

    public IReadOnlyList<byte> FlankAlphaStrata { get; }

    public IReadOnlyList<uint> FlankAlphaTCodes { get; }

    public IReadOnlyList<byte> RoadAlphaStrata { get; }

    public IReadOnlyList<uint> RoadAlphaRCodes { get; }

    internal CanonDetailTextureWiring StructureSpecificsTexture { get; }

    internal CanonDetailTextureWiring SurroundingsSpecificsTexture { get; }

    internal (GpuTextureSlot Terrain, GpuTextureSlot Alpha) TextureSockets =>
        (_rhi.LandSocket, _rhi.AlphaSocket);

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _rhi.SurroundingsDetailTexture?.Dispose();
        _rhi.StructureDetailTexture?.Dispose();
        _rhi.Alpha.Dispose();
        _rhi.Terrain.Dispose();
    }

    public void AssignAnisotropic(int tier)
    {
        ImposeAnisotropic(tier);
        Console.WriteLine($"LandTileset: anisotropic updated to {tier}x");
    }

    private static LandscapeLayerDecode UnpackLandStrata(
        IDatAccess datFiles,
        IReadOnlyList<TerrainTexDesc> landDsc)
    {
        var decodedByKind = new Dictionary<uint, UnpackedTexture>();
        var tilingByKind = new Dictionary<uint, uint>();
        int upperW = 1, upperH = 1;
        foreach (TerrainTexDesc tmtd in landDsc)
        {
            uint kindTag = (uint)tmtd.Kind;
            if (decodedByKind.ContainsKey(kindTag))
                continue;

            tilingByKind[kindTag] = tmtd.Tex.TexTiling;

            uint canvasTextureIdent = (uint)tmtd.Tex.TextureId;
            SkinTexture? texture = datFiles.Get<SkinTexture>(canvasTextureIdent);
            if (texture is null || texture.BitmapIds.Count is 0)
            {
                Console.WriteLine($"WARN: TerrainType {tmtd.Kind} SurfaceTexture 0x{canvasTextureIdent:X8} absent");
                decodedByKind[kindTag] = UnpackedTexture.Magenta;
                continue;
            }

            Bitmap? surface = datFiles.Get<Bitmap>((uint)texture.BitmapIds[0]);
            if (surface is null)
            {
                decodedByKind[kindTag] = UnpackedTexture.Magenta;
                continue;
            }

            ColorTable? swatch = surface.DefaultColorTableId is not 0
                ? datFiles.Get<ColorTable>(surface.DefaultColorTableId)
                : null;

            UnpackedTexture decoded = CanvasUnpacker.DecodeRenderSurface(surface, swatch);
            decodedByKind[kindTag] = decoded;
            if (decoded.Width > upperW) upperW = decoded.Width;
            if (decoded.Height > upperH) upperH = decoded.Height;
        }

        return new LandscapeLayerDecode(decodedByKind, tilingByKind, upperW, upperH);
    }

    private static AlphaStratumUnpack UnpackAlphaStrata(
        IDatAccess datFiles,
        TexMerge bmpCombine)
    {
        var decoded = new List<UnpackedTexture>();
        List<byte> cornerStrata = new List<byte>();
        List<byte> flankStrata = new List<byte>();
        List<byte> roadStrata = new List<byte>();
        List<uint> cornerTCodes = new List<uint>();
        List<uint> flankTCodes = new List<uint>();
        List<uint> roadRCodes = new List<uint>();

        foreach (TerrainAlphaMap listing in bmpCombine.CornerMaps)
        {
            if (TryUnpackAlphaLookup(datFiles, (uint)listing.TextureId, out var dtex))
            {
                UnpackAlphaStrataBranch3(cornerStrata, decoded, cornerTCodes, listing, dtex);
            }
            else
            {
                Console.WriteLine($"WARN: CornerTerrainMap TextureId 0x{(uint)listing.TextureId:X8} could not decode");
            }
        }
        foreach (TerrainAlphaMap listing in bmpCombine.SideMaps)
        {
            if (TryUnpackAlphaLookup(datFiles, (uint)listing.TextureId, out var dtex))
            {
                UnpackAlphaStrataBranch2(flankStrata, decoded, flankTCodes, listing, dtex);
            }
            else
            {
                Console.WriteLine($"WARN: SideTerrainMap TextureId 0x{(uint)listing.TextureId:X8} could not decode");
            }
        }
        foreach (TerrainAlphaMap listing in bmpCombine.RoadMaps)
        {
            if (TryUnpackAlphaLookup(datFiles, (uint)listing.TextureId, out var dtex))
            {
                UnpackAlphaStrataBranch(roadStrata, decoded, roadRCodes, listing, dtex);
            }
            else
            {
                Console.WriteLine($"WARN: RoadMap TextureId 0x{(uint)listing.TextureId:X8} could not decode");
            }
        }

        int decodedUpperW = 1, decodedUpperH = 1;
        foreach (var texture in decoded)
        {
            if (texture.Width > decodedUpperW) decodedUpperW = texture.Width;
            if (texture.Height > decodedUpperH) decodedUpperH = texture.Height;
        }

        return new AlphaStratumUnpack(
            decoded,
            cornerStrata,
            flankStrata,
            roadStrata,
            cornerTCodes,
            flankTCodes,
            roadRCodes,
            decodedUpperW,
            decodedUpperH);
    }

    private static void UnpackAlphaStrataBranch(List<byte> roadStrata, List<UnpackedTexture> decoded, List<uint> roadRCodes, TerrainAlphaMap listing, UnpackedTexture dtex)
    {
        roadStrata.Add((byte)decoded.Count);
        roadRCodes.Add(listing.Code);
        decoded.Add(dtex);
    }

    private static void UnpackAlphaStrataBranch2(List<byte> flankStrata, List<UnpackedTexture> decoded, List<uint> flankTCodes, TerrainAlphaMap listing, UnpackedTexture dtex)
    {
        flankStrata.Add((byte)decoded.Count);
        flankTCodes.Add(listing.Code);
        decoded.Add(dtex);
    }

    private static void UnpackAlphaStrataBranch3(List<byte> cornerStrata, List<UnpackedTexture> decoded, List<uint> cornerTCodes, TerrainAlphaMap listing, UnpackedTexture dtex)
    {
        cornerStrata.Add((byte)decoded.Count);
        cornerTCodes.Add(listing.Code);
        decoded.Add(dtex);
    }

    private static SpecificsBitmapAsset? TryCreateDetailTexture(
        IClientGpuDevice dev,
        IDatAccess datFiles,
        IClientGpuSampler sampler,
        TerrainTexDesc land,
        string bucketLabel)
    {
        uint canvasTextureIdent = (uint)land.Tex.DetailTextureId;
        if (canvasTextureIdent is 0)
            return null;

        var canvasTexture = datFiles.Get<SkinTexture>(canvasTextureIdent);
        if (canvasTexture is null || canvasTexture.BitmapIds.Count is 0)
        {
            Console.WriteLine(
                $"WARN: retail {bucketLabel} detail SurfaceTexture "
                + $"0x{canvasTextureIdent:X8} absent");
            return null;
        }

        uint rasterizeCanvasIdent = (uint)canvasTexture.BitmapIds[0];
        var rasterizeCanvas = datFiles.Get<Bitmap>(rasterizeCanvasIdent);
        if (rasterizeCanvas is null)
        {
            Console.WriteLine(
                $"WARN: retail {bucketLabel} detail RenderSurface "
                + $"0x{rasterizeCanvasIdent:X8} absent");
            return null;
        }

        ColorTable? swatch = rasterizeCanvas.DefaultColorTableId is not 0
            ? datFiles.Get<ColorTable>(rasterizeCanvas.DefaultColorTableId)
            : null;
        var decoded = CanvasUnpacker.DecodeRenderSurface(
            rasterizeCanvas,
            swatch);
        if (ReferenceEquals(decoded, UnpackedTexture.Magenta))
        {
            Console.WriteLine(
                $"WARN: retail {bucketLabel} detail RenderSurface "
                + $"0x{rasterizeCanvasIdent:X8} could not decode");
            return null;
        }

        int mipTiers = Batching.RhiRealmTextureArray.MipTiersFor(
            decoded.Width,
            decoded.Height);
        IGpuBitmap? texture = null;
        var socket = GpuTextureSlot.Unassigned;
        try
        {
            texture = dev.BuildTexture(new GpuBitmapSpec(
                $"retail-detail-{bucketLabel}",
                GpuBitmapFlavor.Texture2DArray,
                GpuBitmapFmt.Rgba8Unorm,
                decoded.Width,
                decoded.Height,
                LayerCount: 1,
                MipLevelCount: mipTiers));
            texture.Upload(0, 0, decoded.Rgba8);
            texture.ProduceMipChain();
            socket = dev.EnrollTexture(texture, sampler);
            CanonDetailTextureWiring mapping = new CanonDetailTextureWiring(
                socket,
                land.Tex.DetailTexTiling,
                canvasTextureIdent,
                rasterizeCanvasIdent,
                decoded.Width,
                decoded.Height);
            Console.WriteLine(
                $"Retail detail {bucketLabel}: SurfaceTexture "
                + $"0x{canvasTextureIdent:X8} -> RenderSurface "
                + $"0x{rasterizeCanvasIdent:X8}, {decoded.Width}x{decoded.Height}, "
                + $"tiling={mapping.Tiling}");
            return new SpecificsBitmapAsset(texture, mapping);
        }
        catch
        {
            if (socket.IsAssigned)
                dev.FreeTextureSocket(socket);
            texture?.Dispose();
            throw;
        }
    }

    private static void TeardownSpecificsTextureAsset(
        IClientGpuDevice dev,
        SpecificsBitmapAsset? asset)
    {
        if (asset is null)
            return;

        if (asset.Binding.IsAvailable)
            dev.FreeTextureSocket(asset.Binding.TextureSlot);
        asset.Texture.Dispose();
    }

    private static UnpackedTexture WhitePixel() =>
        new([0xFF, 0xFF, 0xFF, 0xFF], 1, 1);

    private void ImposeAnisotropic(float tier)
    {
        RhiArrs rhi = _rhi!;
        IClientGpuSampler sampler = rhi.Device.BuildSampler(GpuSamplerSpec.RealmRepeat with
        {
            MaxAnisotropy = Math.Max(1f, tier),
        });
        if (ReferenceEquals(rhi.LandSampler, sampler) && rhi.LandSocket.IsAssigned)
            return;

        ImposeAnisotropicRest(rhi, sampler);
    }

    private void ImposeAnisotropicRest(RhiArrs rhi, IClientGpuSampler sampler)
    {
        if (rhi.LandSocket.IsAssigned)
            rhi.Device.FreeTextureSocket(rhi.LandSocket);
        rhi.LandSampler = sampler;
        rhi.LandSocket = rhi.Device.EnrollTexture(rhi.Terrain, sampler);
    }

    private static bool TryUnpackAlphaLookup(IDatAccess datFiles, uint canvasTextureIdent, out UnpackedTexture decoded)
    {
        decoded = UnpackedTexture.Magenta;

        SkinTexture? texture = datFiles.Get<SkinTexture>(canvasTextureIdent);
        if (texture is null || texture.BitmapIds.Count is 0)
            return false;

        Bitmap? surface = datFiles.Get<Bitmap>((uint)texture.BitmapIds[0]);
        if (surface is null)
            return false;

        // Alpha maps ship as PFID_CUSTOM_LSCAPE_ALPHA (AC's landscape-alpha format) or the more generic
        // PFID_A8; terrain blending alpha masks MUST use isAdditive=true so R=G=B=A=val - the terrain
        // fragment shader reads .r for the blend weight.
        UnpackedTexture d = CanvasUnpacker.DecodeRenderSurface(surface, swatch: null, isClipLookup: false, isAdditive: true);
        if (ReferenceEquals(d, UnpackedTexture.Magenta))
            return false;

        decoded = d;
        return true;
    }

    private static byte[] RescaleRgba8Closest(UnpackedTexture src, int dstW, int dstH)
    {
        if (src.Width == dstW && src.Height == dstH)
            return src.Rgba8;

        byte[] dst = new byte[dstW * dstH * 4];
        for (int y = 0; y < dstH; ++y)
        {
            int srcY = y * src.Height / dstH;
            for (int x = 0; x < dstW; ++x)
            {
                int srcX = x * src.Width / dstW;
                int si = (srcY * src.Width + srcX) * 4;
                int di = (y * dstW + x) * 4;
                dst[di + 0] = src.Rgba8[si + 0];
                dst[di + 1] = src.Rgba8[si + 1];
                dst[di + 2] = src.Rgba8[si + 2];
                dst[di + 3] = src.Rgba8[si + 3];
            }
        }
        return dst;
    }
}
