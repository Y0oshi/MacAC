using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Shell.Panels;

public sealed class EffigyClickMap
{
    public const uint PressLookupEnum = 0x1000000Cu;
    public const uint InterfaceEnumBucket = 7u;

    private readonly byte[] _rgba;

    public int Width { get; }
    public int Height { get; }

    public EffigyClickMap(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length < checked(width * height * 4))
            throw new ArgumentException("The RGBA buffer is smaller than the declared map", nameof(rgba));

        _rgba = rgba;
        Width = width;
        Height = height;
    }

    public static EffigyClickMap? Load(IDatAccess datFiles)
    {
        ArgumentNullException.ThrowIfNull(datFiles);

        uint masterDid = (uint)datFiles.Portal.Db.Header.MasterMapId;
        if (masterDid is 0
            || !datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master)
            || master is null
            || !master.ClientEnumToId.TryGetValue(InterfaceEnumBucket, out uint subLookupDid)
            || !datFiles.Portal.TryGet<IdNameMap>(subLookupDid, out var subLookup)
            || subLookup is null
            || !subLookup.ClientEnumToId.TryGetValue(PressLookupEnum, out uint canvasDid))
            return null;

        if (!datFiles.Portal.TryGet<Bitmap>(canvasDid, out var canvas)
            && !datFiles.HighRes.TryGet<Bitmap>(canvasDid, out canvas))
            return null;

        ColorTable? swatch = canvas.DefaultColorTableId is not 0
            ? datFiles.Get<ColorTable>(canvas.DefaultColorTableId)
            : null;
        var decoded = CanvasUnpacker.DecodeRenderSurface(canvas, swatch);
        return decoded.Width > 1 && decoded.Height > 1
            ? new EffigyClickMap(decoded.Rgba8, decoded.Width, decoded.Height)
            : null;
    }

    public WieldBitmask FetchCorpusLocale(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return WieldBitmask.None;

        int pixel = (y * Width + x) * 4;
        byte r = _rgba[pixel];
        byte g = _rgba[pixel + 1];
        byte b = _rgba[pixel + 2];

        return (r, g, b) switch
        {
            (0x00, 0x00, 0xFF) => WieldBitmask.HeadWear,
            (0x00, 0xFF, 0x00) => WieldBitmask.ChestWear | WieldBitmask.ChestArmor,
            (0xFF, 0x00, 0x00) => WieldBitmask.AbdomenWear | WieldBitmask.AbdomenArmor,
            (0x00, 0xFF, 0xFF) => WieldBitmask.UpperArmWear | WieldBitmask.UpperArmArmor,
            (0xFF, 0x00, 0xFF) => WieldBitmask.LowerArmWear | WieldBitmask.LowerArmArmor,
            (0xFF, 0xFF, 0x00) => WieldBitmask.UpperLegWear | WieldBitmask.UpperLegArmor,
            (0x00, 0x00, 0x80) => WieldBitmask.LowerLegWear | WieldBitmask.LowerLegArmor,
            (0x00, 0x80, 0x00) => WieldBitmask.HandWear,
            (0x80, 0x00, 0x00) => WieldBitmask.FootWear,
            _ => WieldBitmask.None,
        };
    }
}
