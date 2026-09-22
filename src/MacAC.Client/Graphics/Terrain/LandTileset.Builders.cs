using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Graphics;

public sealed partial class LandTileset
{
    internal static LandTileset AssembleBackendNeutral(IClientGpuDevice dev, IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(dev);
        ArgumentNullException.ThrowIfNull(datFiles);

        WorldRegion zone = datFiles.Get<WorldRegion>(0x13000000u)
            ?? throw new InvalidOperationException("Region dat id 0x13000000 absent");
        var bmpCombine = zone.Terrain?.Surfaces?.TexMerge;
        var landDsc = bmpCombine?.TerrainTexes;

        Dictionary<uint, UnpackedTexture> decodedByKind;
        Dictionary<uint, uint> tilingByKind;
        int upperW, upperH;
        if (landDsc is null || landDsc.Count is 0)
        {
            Console.WriteLine("WARN: TerrainDesc absent, using single white fallback layer");
            decodedByKind = new Dictionary<uint, UnpackedTexture> { [0u] = WhitePixel() };
            tilingByKind = [];
            upperW = 1;
            upperH = 1;
        }
        else
        {
            var unpack = UnpackLandStrata(datFiles, landDsc);
            decodedByKind = unpack.DecodedByType;
            tilingByKind = unpack.TilingByType;
            upperW = unpack.MaxWidth;
            upperH = unpack.MaxHeight;
        }

        AlphaStratumUnpack alpha = bmpCombine is null
            ? new AlphaStratumUnpack([], [], [], [], [], [], [], 1, 1)
            : UnpackAlphaStrata(datFiles, bmpCombine);
        var alphaDecoded = alpha.Decoded;
        int alphaStratumTally = Math.Max(1, alphaDecoded.Count);
        int alphaW = alphaDecoded.Count is 0 ? 1 : alpha.MaxWidth;
        int alphaH = alphaDecoded.Count is 0 ? 1 : alpha.MaxHeight;

        int stratumTally = decodedByKind.Count;
        int mipTiers = Batching.RhiRealmTextureArray.MipTiersFor(upperW, upperH);
        IGpuBitmap? landTexture = null;
        IGpuBitmap? alphaTexture = null;
        IClientGpuSampler? specificsSampler = null;
        SpecificsBitmapAsset? structureSpecifics = null;
        SpecificsBitmapAsset? surroundingsSpecifics = null;
        try
        {
            landTexture = dev.BuildTexture(new GpuBitmapSpec(
                "terrain-atlas",
                GpuBitmapFlavor.Texture2DArray,
                GpuBitmapFmt.Rgba8Unorm,
                upperW,
                upperH,
                stratumTally,
                mipTiers));

            var lookup = new Dictionary<uint, uint>(stratumTally);
            int stratumIndex = 0;
            foreach (var kvp in decodedByKind)
            {
                byte[] buf = RescaleRgba8Closest(kvp.Value, upperW, upperH);
                landTexture.Upload(0, stratumIndex, buf);
                lookup[kvp.Key] = (uint)stratumIndex;
                ++stratumIndex;
            }

            landTexture.ProduceMipChain();

            float[] tilingByStratum = LandBitmapTilingChart.Build(
                lookup.Select(listing =>
                    (listing.Value, tilingByKind.TryGetValue(listing.Key, out uint repeatTally)
                        ? repeatTally
                        : 1u)));

            alphaTexture = dev.BuildTexture(new GpuBitmapSpec(
                "terrain-alpha-atlas",
                GpuBitmapFlavor.Texture2DArray,
                GpuBitmapFmt.Rgba8Unorm,
                alphaW,
                alphaH,
                alphaStratumTally,
                MipLevelCount: 1));
            if (alphaDecoded.Count is 0)
            {
                Console.WriteLine("WARN: no alpha maps loaded; alpha atlas will be a 1x1 white fallback");
                alphaTexture.Upload(0, 0, [0xFF, 0xFF, 0xFF, 0xFF]);
            }
            else
            {
                for (int idx = 0; idx < alphaDecoded.Count; ++idx)
                    alphaTexture.Upload(0, idx, RescaleRgba8Closest(alphaDecoded[idx], alphaW, alphaH));
            }

            IClientGpuSampler alphaSampler = dev.BuildSampler(GpuSamplerSpec.RealmClamp with
            {
                MipFilter = GpuMipSift.None,
            });

            specificsSampler = dev.BuildSampler(SpecificsSamplerBlurb);
            if (landDsc is { Count: > 1 })
            {
                structureSpecifics = TryCreateDetailTexture(
                    dev,
                    datFiles,
                    specificsSampler,
                    landDsc[1],
                    "building");
            }
            if (landDsc is { Count: > 2 })
            {
                surroundingsSpecifics = TryCreateDetailTexture(
                    dev,
                    datFiles,
                    specificsSampler,
                    landDsc[2],
                    "environment");
            }

            Console.WriteLine(
                $"LandTileset: {stratumTally} terrain layers at {upperW}x{upperH} ({mipTiers} mip levels)");
            Console.WriteLine(
                $"AlphaAtlas: {alphaStratumTally} layers at {alphaW}x{alphaH}  "
                + $"(corners={alpha.CornerLayers.Count}, sides={alpha.SideLayers.Count}, "
                + $"roads={alpha.RoadLayers.Count})");

            return new LandTileset(
                dev,
                landTexture,
                alphaTexture,
                alphaSampler,
                lookup,
                stratumTally,
                tilingByStratum,
                alphaStratumTally,
                alpha.CornerLayers,
                alpha.SideLayers,
                alpha.RoadLayers,
                alpha.CornerTCodes,
                alpha.SideTCodes,
                alpha.RoadRCodes,
                structureSpecifics,
                surroundingsSpecifics);
        }
        catch
        {
            TeardownSpecificsTextureAsset(dev, surroundingsSpecifics);
            TeardownSpecificsTextureAsset(dev, structureSpecifics);
            alphaTexture?.Dispose();
            landTexture?.Dispose();
            throw;
        }
    }
}
