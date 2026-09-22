#nullable disable

namespace MacAC.Dat;

// A material: either a texture + palette or a flat colour, with lighting terms.
public class Skin : DatRecord, IDatRecord<Skin>
{
    public static RecordKind Kind => RecordKind.Surface;
    public static (uint First, uint Last) IdRange => (0x08000000, 0x0800FFFF);
    public SkinBits Bits { get; set; }
    public uint TextureId { get; set; }
    public uint ColorTableId { get; set; }
    public Argb Color { get; set; }
    public float Translucency { get; set; }
    public float Luminosity { get; set; }
    public float Diffuse { get; set; }
    public bool IsTextured => (Bits & (SkinBits.Base1Image | SkinBits.Base1ClipMap)) != 0;

    public static Skin Read(ref DatCursor c)
    {
        var bits = (SkinBits)c.U32();
        bool textured = (bits & (SkinBits.Base1Image | SkinBits.Base1ClipMap)) != 0;
        uint tex = 0, pal = 0; Argb color = null;
        if (textured) { tex = c.U32(); pal = c.U32(); } else color = Argb.Read(ref c);
        return new Skin { Id = c.FileId, Bits = bits, TextureId = tex, ColorTableId = pal, Color = color, Translucency = c.F32(), Luminosity = c.F32(), Diffuse = c.F32() };
    }
}

// The mip chain (or single level) behind a skin.
public class SkinTexture : DatRecord, IDatRecord<SkinTexture>
{
    public static RecordKind Kind => RecordKind.SurfaceTexture;
    public static (uint First, uint Last) IdRange => (0x05000000, 0x05FFFFFF);
    public SkinTextureKind TextureKind { get; set; }
    public List<uint> BitmapIds { get; set; } = [];
    public static SkinTexture Read(ref DatCursor c)
    {
        var t = new SkinTexture { Id = c.U32(), Category = c.U32(), TextureKind = (SkinTextureKind)c.U8() };
        int n = c.I32();
        for (int i = 0; i < n; i++) t.BitmapIds.Add(c.U32());
        return t;
    }
}

// Raw pixels in one of the client's layouts.
public class Bitmap : DatRecord, IDatRecord<Bitmap>
{
    public static RecordKind Kind => RecordKind.RenderSurface;
    public static (uint First, uint Last) IdRange => (0x06000000, 0x07FFFFFF);
    public int Width { get; set; }
    public int Height { get; set; }
    public PixelLayout Layout { get; set; }
    public byte[] Pixels { get; set; } = [];
    public uint DefaultColorTableId { get; set; }
    public bool IsIndexed => Layout is PixelLayout.PFID_P8 or PixelLayout.PFID_INDEX16;

    public static Bitmap Read(ref DatCursor c)
    {
        uint id = c.U32(), cat = c.U32();
        int w = c.I32(), h = c.I32();
        var layout = (PixelLayout)c.U32();
        int n = c.I32();
        var px = c.Array(n);
        uint pal = layout is PixelLayout.PFID_P8 or PixelLayout.PFID_INDEX16 ? c.U32() : 0;
        return new Bitmap { Id = id, Category = cat, Width = w, Height = h, Layout = layout, Pixels = px, DefaultColorTableId = pal };
    }
}

public class RenderTexture : DatRecord, IDatRecord<RenderTexture>
{
    public static RecordKind Kind => RecordKind.RenderTexture;
    public static (uint First, uint Last) IdRange => (0x15000000, 0x15FFFFFF);
    public SkinTextureKind TextureKind { get; set; }
    public List<uint> SourceLevels { get; set; } = [];
    public static RenderTexture Read(ref DatCursor c)
    {
        var t = new RenderTexture { Id = c.U32(), Category = c.U32(), TextureKind = (SkinTextureKind)c.U8() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) t.SourceLevels.Add(c.U32());
        return t;
    }
}

public class ColorTable : DatRecord, IDatRecord<ColorTable>
{
    public static RecordKind Kind => RecordKind.Palette;
    public static (uint First, uint Last) IdRange => (0x04000000, 0x0400FFFF);
    public List<Argb> Colors { get; set; } = [];
    public static ColorTable Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = c.I32();
        var t = new ColorTable { Id = id, Colors = new List<Argb>(n) };
        for (int i = 0; i < n; i++) t.Colors.Add(Argb.Read(ref c));
        return t;
    }
}

public class ColorTableSet : DatRecord, IDatRecord<ColorTableSet>
{
    public static RecordKind Kind => RecordKind.PalSet;
    public static (uint First, uint Last) IdRange => (0x0F000000, 0x0F00FFFF);
    public List<uint> ColorTableIds { get; set; } = [];
    public static ColorTableSet Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var s = new ColorTableSet { Id = id, ColorTableIds = new List<uint>(n) };
        for (int i = 0; i < n; i++) s.ColorTableIds.Add(c.U32());
        return s;
    }
}

public class GlyphDesc
{
    public ushort Unicode { get; set; }
    public ushort OffsetX { get; set; }
    public ushort OffsetY { get; set; }
    public byte Width { get; set; }
    public byte Height { get; set; }
    public sbyte HorizontalOffsetBefore { get; set; }
    public sbyte HorizontalOffsetAfter { get; set; }
    public sbyte VerticalOffsetBefore { get; set; }
    public static GlyphDesc Read(ref DatCursor c) => new()
    {
        Unicode = c.U16(), OffsetX = c.U16(), OffsetY = c.U16(), Width = c.U8(), Height = c.U8(),
        HorizontalOffsetBefore = c.I8(), HorizontalOffsetAfter = c.I8(), VerticalOffsetBefore = c.I8(),
    };
}

public class GlyphSet : DatRecord, IDatRecord<GlyphSet>
{
    public static RecordKind Kind => RecordKind.Font;
    public static (uint First, uint Last) IdRange => (0x40000000, 0x40000FFF);
    public uint MaxCharHeight { get; set; }
    public uint MaxCharWidth { get; set; }
    public List<GlyphDesc> Glyphs { get; set; } = [];
    public uint HorizontalBorderPixels { get; set; }
    public uint VerticalBorderPixels { get; set; }
    public uint BaselineOffset { get; set; }
    public uint ForegroundBitmapId { get; set; }
    public uint BackgroundBitmapId { get; set; }
    public static GlyphSet Read(ref DatCursor c)
    {
        uint id = c.U32(), mh = c.U32(), mw = c.U32();
        int n = (int)c.U32();
        var glyphs = new List<GlyphDesc>(n);
        for (int i = 0; i < n; i++) glyphs.Add(GlyphDesc.Read(ref c));
        return new GlyphSet
        {
            Id = id, MaxCharHeight = mh, MaxCharWidth = mw, Glyphs = glyphs, HorizontalBorderPixels = c.U32(), VerticalBorderPixels = c.U32(),
            BaselineOffset = c.U32(), ForegroundBitmapId = c.U32(), BackgroundBitmapId = c.U32(),
        };
    }
}

public class MaterialInstance : DatRecord, IDatRecord<MaterialInstance>
{
    public static RecordKind Kind => RecordKind.MaterialInstance;
    public static (uint First, uint Last) IdRange => (0x18000000, 0x18FFFFFF);
    public uint MaterialId { get; set; }
    public uint MaterialKind { get; set; }
    public List<uint> ModifierIds { get; set; } = [];
    public bool AllowStencilShadows { get; set; }
    public bool WantDiscardGeometry { get; set; }
    public static MaterialInstance Read(ref DatCursor c)
    {
        uint id = c.U32(), mat = c.U32(), kind = c.U32();
        int n = (int)c.U32();
        var mods = new List<uint>(n);
        for (int i = 0; i < n; i++) mods.Add(c.U32());
        return new MaterialInstance { Id = id, MaterialId = mat, MaterialKind = kind, ModifierIds = mods, AllowStencilShadows = c.U8() != 0, WantDiscardGeometry = c.U8() != 0 };
    }
}

public class MaterialProperty
{
    public uint NameId { get; set; }
    public MaterialDataKind DataKind { get; set; }
    public uint DataLength { get; set; }
    public uint DataLength2 { get; set; }
    public ushort DataLength3 { get; set; }
    public byte DataLength4 { get; set; }
    public static MaterialProperty Read(ref DatCursor c)
    {
        uint name = c.U32();
        var kind = (MaterialDataKind)c.U16();
        c.Align(4);
        return new MaterialProperty { NameId = name, DataKind = kind, DataLength = c.U32(), DataLength2 = c.U32(), DataLength3 = c.U16(), DataLength4 = c.U8() };
    }
}

public class MaterialModifier : DatRecord, IDatRecord<MaterialModifier>
{
    public static RecordKind Kind => RecordKind.MaterialModifier;
    public static (uint First, uint Last) IdRange => (0x17000000, 0x17FFFFFF);
    public List<MaterialProperty> Properties { get; set; } = [];
    public static MaterialModifier Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var m = new MaterialModifier { Id = id, Properties = new List<MaterialProperty>(n) };
        for (int i = 0; i < n; i++) m.Properties.Add(MaterialProperty.Read(ref c));
        return m;
    }
}

public class RenderMaterial : DatRecord, IDatRecord<RenderMaterial>
{
    public static RecordKind Kind => RecordKind.RenderMaterial;
    public static (uint First, uint Last) IdRange => (0x16000000, 0x16FFFFFF);
    public static RenderMaterial Read(ref DatCursor c) => new() { Id = c.U32() };
}
