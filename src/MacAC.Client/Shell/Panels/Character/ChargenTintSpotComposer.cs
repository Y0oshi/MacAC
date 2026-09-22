using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Genesis;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Shell.Panels;

internal interface IChargenSwatchBitmapOrigin
{
    uint BlankSpotTexture { get; }

    uint GradDiskTexture { get; }

    uint GradPlugTexture { get; }

    uint FetchEngagedSpotTexture(GenesisSwatchRgb rgb);
}

internal sealed class ChargenTintSpotComposer(IDatAccess datFiles, BitmapStash stash) : IChargenSwatchBitmapOrigin
{
    private const uint SpotEnumIdent = 0x1000000Du;
    private const uint BlankEnumIdent = 0x1000000Fu;
    private const uint GradDiskEnumIdent = 0x1000000Eu;
    private const uint GradPlugEnumIdent = 0x10000010u;
    private const uint EnumBucket = 7u;

    private readonly IDatAccess _datFiles = datFiles;
    private readonly BitmapStash _stash = stash;

    private UnpackedTexture? _spotBlueprint;
    private bool _spotLocateTried;
    private readonly Dictionary<(byte R, byte G, byte B), uint> _bakedSpotByTint = [];
    private bool _blankLocateTried;
    private bool _gradDiskLocateTried;
    private bool _gradPlugLocateTried;

    public uint BlankSpotTexture
    {
        get
        {
            if (!_blankLocateTried)
            {
                _blankLocateTried = true;
                if (TryUnpack(BlankEnumIdent, out UnpackedTexture decoded))
                    field = _stash.PushRgba8(decoded.Rgba8, decoded.Width, decoded.Height, closest: true);
            }
            return field;
        }
    }

    public uint GradDiskTexture
    {
        get
        {
            if (!_gradDiskLocateTried)
            {
                _gradDiskLocateTried = true;
                if (TryUnpack(GradDiskEnumIdent, out UnpackedTexture decoded))
                    field = _stash.PushRgba8(decoded.Rgba8, decoded.Width, decoded.Height, closest: true);
            }
            return field;
        }
    }

    public uint GradPlugTexture
    {
        get
        {
            if (!_gradPlugLocateTried)
            {
                _gradPlugLocateTried = true;
                if (TryUnpack(GradPlugEnumIdent, out UnpackedTexture decoded))
                    field = _stash.PushRgba8(decoded.Rgba8, decoded.Width, decoded.Height, closest: true);
            }
            return field;
        }
    }

    public uint FetchEngagedSpotTexture(GenesisSwatchRgb rgb)
    {
        if (!_spotLocateTried)
        {
            _spotLocateTried = true;
            if (TryUnpack(SpotEnumIdent, out UnpackedTexture decoded))
                _spotBlueprint = decoded;
        }
        if (_spotBlueprint is not { } blueprint)
            return 0u;

        var tag = (rgb.R, rgb.G, rgb.B);
        if (_bakedSpotByTint.TryGetValue(tag, out uint stashed))
            return stashed;

        byte[] baked = ReplacePreciseBlackWithTint(blueprint.Rgba8, rgb);
        uint texture = _stash.PushRgba8(baked, blueprint.Width, blueprint.Height, closest: true);
        _bakedSpotByTint[tag] = texture;
        return texture;
    }

    internal static byte[] ReplacePreciseBlackWithTint(byte[] rgba, GenesisSwatchRgb rgb)
    {
        byte[] baked = (byte[])rgba.Clone();
        for (int idx = 0; idx + 3 < baked.Length; idx += 4)
        {
            if (baked[idx] is not 0 || baked[idx + 1] is not 0 || baked[idx + 2] is not 0 || baked[idx + 3] is not 255)
                continue;
            baked[idx] = rgb.R;
            baked[idx + 1] = rgb.G;
            baked[idx + 2] = rgb.B;
            baked[idx + 3] = 255;
        }
        return baked;
    }

    private bool TryUnpack(uint enumIdent, out UnpackedTexture decoded)
    {
        decoded = null!;
        uint did = CanonDataIdResolver.Resolve(_datFiles, enumIdent, EnumBucket);
        if (did is 0) return false;
        if (!_datFiles.TryGet<Bitmap>(did, out var surface) || surface is null) return false;
        decoded = CanvasUnpacker.DecodeRenderSurface(surface);
        return true;
    }
}
