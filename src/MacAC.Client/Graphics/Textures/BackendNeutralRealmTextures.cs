using MacAC.Dat;
using MacAC.Client.Graphics.Batching;
using MacAC.Client.Graphics.Gpu;

namespace MacAC.Client.Graphics;

internal sealed class BackendNeutralRealmTextures : IDisposable
{
    private readonly IRealmTextureArray _rgba;
    private readonly IRealmTextureArray _compressed;
    private readonly ICompoundBitmapArrayBackend _compoundBackend;
    private readonly CompoundBitmapArrayAsset _compound;
    private bool _destroyed;

    private const int ArrReach = 64;

    private const int CompoundReach = 32;

    private const int CompoundStrata = 8;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;
        _compoundBackend.CraftNonHoused(_compound);
        _compoundBackend.Delete(_compound);
        _compressed.Dispose();
        _rgba.Dispose();
    }

    internal static BackendNeutralRealmTextures Create(IClientGpuDevice dev, Action<string> trace)
    {
        ArgumentNullException.ThrowIfNull(dev);
        ArgumentNullException.ThrowIfNull(trace);
        return new BackendNeutralRealmTextures(dev, trace);
    }

    private BackendNeutralRealmTextures(IClientGpuDevice dev, Action<string> trace)
    {
        RhiRealmTextureArrayMint arrs = new RhiRealmTextureArrayMint(dev);

        int rgbaStrata = TextureAtlasKeeper.DeriveStartingCap(
            ArrReach,
            ArrReach,
            TexelLayout.RGBA8);
        int compressedStrata = TextureAtlasKeeper.DeriveStartingCap(
            ArrReach,
            ArrReach,
            TexelLayout.DXT1);

        IRealmTextureArray? rgba = null;
        IRealmTextureArray? compressed = null;
        ICompoundBitmapArrayBackend? compoundBackend = null;
        CompoundBitmapArrayAsset? compound = null;
        try
        {
            rgba = arrs.BuildClampedArr(TexelLayout.RGBA8, ArrReach, ArrReach, rgbaStrata);
            rgba.RefreshStratum(0, SolidRgba(ArrReach, ArrReach), null, null);
            long rgbaMipOctets = rgba.HandleStaleUpdates();

            compressed = arrs.BuildClampedArr(TexelLayout.DXT1, ArrReach, ArrReach, compressedStrata);
            compressed.RefreshStratum(0, SolidBc1(ArrReach, ArrReach), null, null);
            long compressedMipOctets = compressed.HandleStaleUpdates();

            compoundBackend = new RhiCompoundBitmapArrayBackend(dev);
            compound = compoundBackend.Create(CompoundReach, CompoundReach, CompoundStrata);
            compoundBackend.Upload(compound, 0, SolidRgba(CompoundReach, CompoundReach));

            _rgba = rgba;
            _compressed = compressed;
            _compoundBackend = compoundBackend;
            _compound = compound;

            trace(
                "[V6i-2] world texture creation on the RHI: "
                + $"RGBA8 {ArrReach}x{ArrReach}x{rgbaStrata} "
                + $"(wrap {rgba.LocateSocket(true)}, clamp {rgba.LocateSocket(false)}, "
                + $"{rgbaMipOctets} mip bytes blitted); "
                + $"BC1 {ArrReach}x{ArrReach}x{compressedStrata} "
                + $"(wrap {compressed.LocateSocket(true)}, clamp {compressed.LocateSocket(false)}, "
                + $"{compressedMipOctets} mip bytes encoded); "
                + $"composite {CompoundReach}x{CompoundReach}x{CompoundStrata} ({compound.Slot}).");
        }
        catch
        {
            if (compound is not null)
            {
                compoundBackend!.CraftNonHoused(compound);
                compoundBackend.Delete(compound);
            }
            compressed?.Dispose();
            rgba?.Dispose();
            throw;
        }
    }

    internal static void Exercise(IClientGpuDevice dev, Action<string> trace)
    {
        using var textures = Create(dev, trace);
    }

    private static byte[] SolidRgba(int width, int height)
    {
        byte[] px = new byte[width * height * 4];
        Array.Fill(px, (byte)0xFF);
        return px;
    }

    private static byte[] SolidBc1(int width, int height)
    {
        int chunks = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4);
        byte[] blob = new byte[chunks * 8];
        for (int chunk = 0; chunk < chunks; ++chunk)
        {
            int at = chunk * 8;
            blob[at + 0] = 0xFF;
            blob[at + 1] = 0xFF;
            blob[at + 2] = 0xFF;
            blob[at + 3] = 0xFF;
        }

        return blob;
    }
}
