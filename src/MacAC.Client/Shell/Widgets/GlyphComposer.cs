using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Surfaces;

namespace MacAC.Client.Shell;

public sealed class GlyphComposer(IDatAccess datFiles, BitmapStash stash)
{
    private readonly IDatAccess _datFiles = datFiles;
    private readonly BitmapStash _stash = stash;
    private readonly Dictionary<(uint, uint, uint, uint, uint), uint> _byTuple = [];
    private readonly Dictionary<(uint, uint, uint), ComposedGlyph> _pullByTuple = [];
    private readonly Dictionary<uint, uint> _arcanumGlyphs = [];
    private readonly Dictionary<uint, uint> _moduleGlyphs = [];

    private sealed record ComposedGlyph(byte[] Rgba, int Width, int Height, uint Texture);

    private IdNameMap? _underlaySubLookup;
    private bool _underlayLocateTried;
    private readonly Dictionary<uint, uint> _underlayDidByOrdinal = [];

    private IdNameMap? _fxSubLookup;
    private bool _fxLocateTried;
    private readonly Dictionary<uint, uint> _fxDidByOrdinal = [];
    private readonly Dictionary<uint, UnpackedTexture> _fxTileByDid = [];

    public static (byte[] rgba, int w, int h) Compose(IReadOnlyList<(byte[] rgba, int w, int h)> strata)
    {
        if (strata.Count is 0) return (Array.Empty<byte>(), 0, 0);
        var (baseRgba, w, h) = strata[0];
        byte[] outp = (byte[])baseRgba.Clone();
        for (int li = 1; li < strata.Count; ++li)
        {
            var (src, sw, sh) = strata[li];
            int cw = Math.Min(w, sw), ch = Math.Min(h, sh);
            for (int y = 0; y < ch; ++y)
                for (int x = 0; x < cw; ++x)
                {
                    int di = (y * w + x) * 4, si = (y * sw + x) * 4;
                    float sa = src[si + 3] / 255f;
                    if (sa <= 0f) continue;
                    float da = 1f - sa;
                    outp[di] = (byte)(src[si] * sa + outp[di] * da);
                    outp[di + 1] = (byte)(src[si + 1] * sa + outp[di + 1] * da);
                    outp[di + 2] = (byte)(src[si + 2] * sa + outp[di + 2] * da);
                    outp[di + 3] = (byte)Math.Min(255f, src[si + 3] + outp[di + 3] * da);
                }
        }
        return (outp, w, h);
    }

    public uint FetchGlyph(GearKind gearKind, uint glyphIdent, uint underlayIdent, uint topLayerIdent, uint fxList)
    {
        if (glyphIdent is 0) return 0;
        uint kindUnderlayDid = LocateUnderlayDid(gearKind);
        var tag = (typeUnderlayDid: kindUnderlayDid, iconId: glyphIdent, underlayId: underlayIdent, overlayId: topLayerIdent, effects: fxList);
        if (_byTuple.TryGetValue(tag, out var bmp)) return bmp;

        var pull = FetchOrBuildPullGlyph(glyphIdent, topLayerIdent, fxList);

        var strata = new List<(byte[] rgba, int w, int h)>();
        AppendStratum(strata, kindUnderlayDid);
        AppendStratum(strata, underlayIdent);
        if (pull is not null) strata.Add((pull.Rgba, pull.Width, pull.Height));
        if (strata.Count is 0) return 0;

        var (rgba, w, h) = Compose(strata);
        uint hnd = _stash.PushRgba8(rgba, w, h, closest: true);
        _byTuple[tag] = hnd;
        return hnd;
    }

    public uint FetchPullGlyph(GearKind gearKind, uint glyphIdent, uint underlayIdent, uint topLayerIdent, uint fxList)
    {
        _ = gearKind;
        _ = underlayIdent;
        return glyphIdent is 0 ? 0u : FetchOrBuildPullGlyph(glyphIdent, topLayerIdent, fxList)?.Texture ?? 0u;
    }

    public (uint tex, int w, int h) FetchKeyedGlyph(uint glyphIdent)
    {
        if (glyphIdent is 0u) return (0u, 0, 0);
        var glyph = FetchOrBuildPullGlyph(glyphIdent, topLayerIdent: 0u, fxList: 0u);
        return glyph is null ? (0u, 0, 0) : (glyph.Texture, glyph.Width, glyph.Height);
    }

    public uint FetchArcanumGlyph(uint arcanumIdent)
    {
        if (_arcanumGlyphs.TryGetValue(arcanumIdent, out uint stashed)) return stashed;
        SpellBook chart =
            _datFiles.Get<SpellBook>(0x0E00000Eu)!;
        if (chart is null || !chart.Spells.TryGetValue(arcanumIdent, out var arcanum)) return 0u;

        uint strength = arcanum.Components.Count is 0
            ? 0u
            : CanonSpellFormula.DetermineStrengthTierOfModule(arcanum.Components[0]);
        uint strengthBacking = CanonDataIdResolver.Resolve(_datFiles, strength, 0x10000006u);
        var strata = new List<(byte[] rgba, int w, int h)>();
        AppendStratum(strata, strengthBacking);
        AppendStratum(strata, arcanum.Icon);
        if (strata.Count is 0) return 0u;

        var composed = Compose(strata);
        uint tintOrdinal = (arcanum.Bits & SpellFlags.Reversed) != 0
            ? 1u : 2u;
        uint tintDid = CanonDataIdResolver.Resolve(_datFiles, tintOrdinal, 0x10000007u);
        if (TryUnpack(tintDid, out UnpackedTexture tint))
            ReplaceWhiteFromCanvas(composed.rgba, composed.w, composed.h,
                tint.Rgba8, tint.Width, tint.Height);

        uint topLayerOrdinal = (arcanum.Bits & SpellFlags.FellowshipSpell) != 0
            ? 4u
            : (arcanum.Bits & SpellFlags.SelfTargeted) != 0
                ? 3u : 0u;
        if (topLayerOrdinal is not 0u)
        {
            uint topLayerDid = CanonDataIdResolver.Resolve(_datFiles, topLayerOrdinal, 0x10000007u);
            if (TryUnpack(topLayerDid, out UnpackedTexture topLayer))
                composed = Compose([
                    (composed.rgba, composed.w, composed.h),
                    (topLayer.Rgba8, topLayer.Width, topLayer.Height)]);
        }

        uint texture = _stash.PushRgba8(composed.rgba, composed.w, composed.h, closest: true);
        _arcanumGlyphs[arcanumIdent] = texture;
        return texture;
    }

    public uint FetchArcanumModuleGlyph(uint glyphIdent)
    {
        if (glyphIdent is 0u) return 0u;
        if (_moduleGlyphs.TryGetValue(glyphIdent, out uint stashed)) return stashed;
        if (!TryUnpack(glyphIdent, out UnpackedTexture glyph)) return 0u;
        byte[] rgba = (byte[])glyph.Rgba8.Clone();
        for (int idx = 0; idx + 3 < rgba.Length; idx += 4)
        {
            if (rgba[idx] is not 255 || rgba[idx + 1] is not 255 || rgba[idx + 2] is not 255 || rgba[idx + 3] is not 255)
                continue;
            rgba[idx] = rgba[idx + 1] = rgba[idx + 2] = 0;
        }
        uint texture = _stash.PushRgba8(rgba, glyph.Width, glyph.Height, closest: true);
        _moduleGlyphs[glyphIdent] = texture;
        return texture;
    }

    internal uint LocateUnderlayDid(GearKind gearKind)
    {
        uint raw = (uint)gearKind;
        int lsb = raw is 0 ? -1 : BitOperations.TrailingZeroCount(raw);
        uint ordinal = lsb < 0 ? 0x21u : (uint)(lsb + 1);
        if (_underlayDidByOrdinal.TryGetValue(ordinal, out var stashed)) return stashed;
        SecureUnderlaySubLookup();
        uint did = 0;
        if (_underlaySubLookup is { } sub && sub.ClientEnumToId.TryGetValue(ordinal, out var d)) did = d;
        _underlayDidByOrdinal[ordinal] = did;
        return did;
    }

    internal uint LocateFxDid(uint fxList)
    {
        int lsb = fxList is 0 ? -1 : BitOperations.TrailingZeroCount(fxList);
        uint ordinal = (uint)(lsb + 1);
        if (_fxDidByOrdinal.TryGetValue(ordinal, out var stashed)) return stashed;
        SecureFxSubLookup();
        uint did = 0;
        if (_fxSubLookup is { } sub && sub.ClientEnumToId.TryGetValue(ordinal, out var d)) did = d;
        if (did is 0 && _fxSubLookup is { } sub2 && sub2.ClientEnumToId.TryGetValue(0x21u, out var fb))
            did = fb;
        _fxDidByOrdinal[ordinal] = did;
        return did;
    }

    internal static void ReplaceWhiteFromCanvas(byte[] dst, int dw, int dh, byte[] src, int sw, int sh)
    {
        for (int y = 0; y < dh; ++y)
            for (int x = 0; x < dw; ++x)
            {
                int di = (y * dw + x) * 4;
                if (dst[di] is 255 && dst[di + 1] is 255 && dst[di + 2] is 255 && dst[di + 3] is 255
                    && x < sw && y < sh)
                {
                    int si = (y * sw + x) * 4;
                    dst[di] = src[si]; dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2]; dst[di + 3] = src[si + 3];
                }
            }
    }

    internal bool TryFetchFxTile(uint fxList, out UnpackedTexture tile)
    {
        tile = null!;
        uint did = LocateFxDid(fxList);
        if (did is 0) return false;
        if (_fxTileByDid.TryGetValue(did, out var stashed)) { tile = stashed; return true; }
        if (!TryUnpack(did, out var texture)) return false;
        _fxTileByDid[did] = texture;
        tile = texture;
        return true;
    }

    internal bool TryFetchKeyedGlyphRgba(uint glyphIdent, out byte[] rgba, out int w, out int h)
    {
        rgba = []; w = 0; h = 0;
        if (glyphIdent is 0u) return false;
        var glyph = FetchOrBuildPullGlyph(glyphIdent, topLayerIdent: 0u, fxList: 0u);
        if (glyph is null) return false;
        rgba = glyph.Rgba; w = glyph.Width; h = glyph.Height;
        return true;
    }

    internal bool TryUnpackRaw(uint rasterizeCanvasIdent, out byte[] rgba, out int w, out int h)
    {
        rgba = []; w = 0; h = 0;
        if (!TryUnpack(rasterizeCanvasIdent, out UnpackedTexture decoded)) return false;
        rgba = decoded.Rgba8; w = decoded.Width; h = decoded.Height;
        return true;
    }

    private void SecureUnderlaySubLookup()
    {
        if (_underlayLocateTried) return;
        _underlayLocateTried = true;
        uint masterDid = (uint)_datFiles.Portal.Db.Header.MasterMapId;
        if (masterDid is 0) return;
        if (!_datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master)) return;
        if (!master.ClientEnumToId.TryGetValue(0x10000004u, out var subDid)) return;
        if (_datFiles.Portal.TryGet<IdNameMap>(subDid, out var sub)) _underlaySubLookup = sub;
    }

    private void SecureFxSubLookup()
    {
        if (_fxLocateTried) return;
        _fxLocateTried = true;
        uint masterDid = (uint)_datFiles.Portal.Db.Header.MasterMapId;
        if (masterDid is 0) return;
        if (!_datFiles.Portal.TryGet<IdNameMap>(masterDid, out var master)) return;
        if (!master.ClientEnumToId.TryGetValue(0x10000005u, out var subDid)) return;
        if (_datFiles.Portal.TryGet<IdNameMap>(subDid, out var sub)) _fxSubLookup = sub;
    }

    private bool TryUnpack(uint rasterizeCanvasIdent, out UnpackedTexture decoded)
    {
        decoded = null!;
        if (rasterizeCanvasIdent is 0) return false;
        if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var surface) &&
            !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out surface))
            return false;
        decoded = CanvasUnpacker.DecodeRenderSurface(surface, swatch: null);
        return true;
    }

    private ComposedGlyph? FetchOrBuildPullGlyph(uint glyphIdent, uint topLayerIdent, uint fxList)
    {
        var tag = (iconId: glyphIdent, overlayId: topLayerIdent, effects: fxList);
        if (_pullByTuple.TryGetValue(tag, out var stashed)) return stashed;

        var pullStrata = new List<(byte[] rgba, int w, int h)>();
        AppendStratum(pullStrata, glyphIdent);
        AppendStratum(pullStrata, topLayerIdent);
        if (pullStrata.Count is 0) return null;

        var composed = Compose(pullStrata);
        if (TryFetchFxTile(fxList, out var tile))
            ReplaceWhiteFromCanvas(composed.rgba, composed.w, composed.h,
                tile.Rgba8, tile.Width, tile.Height);

        uint texture = _stash.PushRgba8(composed.rgba, composed.w, composed.h, closest: true);
        ComposedGlyph built = new ComposedGlyph(composed.rgba, composed.w, composed.h, texture);
        _pullByTuple[tag] = built;
        return built;
    }

    private void AppendStratum(List<(byte[], int, int)> strata, uint rasterizeCanvasIdent)
    {
        if (rasterizeCanvasIdent is 0) return;
        if (!_datFiles.Portal.TryGet<Bitmap>(rasterizeCanvasIdent, out var surface) &&
            !_datFiles.HighRes.TryGet<Bitmap>(rasterizeCanvasIdent, out surface))
            return;
        UnpackedTexture decoded = CanvasUnpacker.DecodeRenderSurface(surface, swatch: null);
        strata.Add((decoded.Rgba8, decoded.Width, decoded.Height));
    }
}
