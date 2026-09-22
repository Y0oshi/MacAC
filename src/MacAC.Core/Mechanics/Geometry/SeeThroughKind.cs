using MacAC.Dat;

namespace MacAC.Mechanics.Geometry;

/// <summary>How a surface blends with what is already in the framebuffer.</summary>
public enum SeeThroughKind
{
    /// <summary>Depth write and test, no blend.</summary>
    Opaque = 0,

    ClipMap = 1,

    /// <summary>src·a + dst·(1−a); depth test only. Glass, water decals, flame alpha.</summary>
    AlphaBlend = 2,

    /// <summary>src·a + dst; depth test only. Portal swirls, glows, particles.</summary>
    Additive = 3,

    /// <summary>src·(1−a) + dst·a. Rare, but the DATs do contain it.</summary>
    InvAlpha = 4,
}

public enum CanonSurfaceBlend : byte
{
    Opaque = 0,
    StraightAlpha = 1,
    AlphaAdditive = 2,
    Additive = 3,
    InverseAlpha = 4,
    InverseAdditive = 5,
    Clip = 6,
}

public enum CanonSurfaceAlphaTest : byte
{
    Disabled = 0,
    Paletted = 1,
    Dds = 2,
}

public readonly record struct CanonSurfaceMaterialState(
    CanonSurfaceBlend Blend,
    CanonSurfaceAlphaTest AlphaTest,
    bool FogEnabled)
{
    private const int AlphaTestShift = 3;
    private const byte FogOffBit = 0x20;
    private const byte ReservedBitset = 0xC0;

    public static CanonSurfaceMaterialState Opaque { get; } =
        new(CanonSurfaceBlend.Opaque, CanonSurfaceAlphaTest.Disabled, FogEnabled: true);

    public bool AlphaTestTurnedOn => AlphaTest != CanonSurfaceAlphaTest.Disabled;

    public float AlphaTestReference
    {
        get
        {
            return AlphaTest switch
            {
                CanonSurfaceAlphaTest.Disabled => 0f,
                CanonSurfaceAlphaTest.Paletted => 100f / 255f,
                CanonSurfaceAlphaTest.Dds => 200f / 255f,
                _ => throw new ArgumentOutOfRangeException(nameof(AlphaTest), AlphaTest, null),
            };
        }
    }

    /// <summary>Blend in bits 0-2, alpha test in 3-4, fog-off in 5; 6-7 reserved.</summary>
    public byte ToDenseByte()
    {
        if (Blend > CanonSurfaceBlend.Clip)
            throw new InvalidOperationException($"Unrecognized SetSurface blend {Blend}.");
        if (AlphaTest > CanonSurfaceAlphaTest.Dds)
            throw new InvalidOperationException($"Unrecognized SetSurface alpha test {AlphaTest}.");
        return (byte)((byte)Blend | ((byte)AlphaTest << AlphaTestShift) | (FogEnabled ? 0 : FogOffBit));
    }

    public static CanonSurfaceMaterialState FromDenseByte(byte val)
    {
        if ((val & ReservedBitset) is not 0)
            throw new InvalidDataException($"Reserved SetSurface state bits are set: 0x{val:X2}.");
        CanonSurfaceBlend blend = (CanonSurfaceBlend)(val & 0x07);
        CanonSurfaceAlphaTest alphaTest = (CanonSurfaceAlphaTest)((val >> AlphaTestShift) & 0x03);
        if (blend > CanonSurfaceBlend.Clip || alphaTest > CanonSurfaceAlphaTest.Dds)
            throw new InvalidDataException($"Not valid SetSurface state byte: 0x{val:X2}.");
        return new CanonSurfaceMaterialState(blend, alphaTest, FogEnabled: (val & FogOffBit) is 0);
    }

    public static CanonSurfaceMaterialState Resolve(SkinBits kind, bool texturePresent, bool textureHasSwatch)
    {
        bool additive = kind.HasFlag(SkinBits.Additive);
        bool clip = kind.HasFlag(SkinBits.Base1ClipMap);

        CanonSurfaceBlend blend;
        blend = kind.HasFlag(SkinBits.Alpha) ? additive ? CanonSurfaceBlend.AlphaAdditive : CanonSurfaceBlend.StraightAlpha : kind.HasFlag(SkinBits.InvAlpha) ? additive ? CanonSurfaceBlend.InverseAdditive : CanonSurfaceBlend.InverseAlpha : additive ? CanonSurfaceBlend.Additive : CanonSurfaceBlend.Opaque;

        var alphaTest = CanonSurfaceAlphaTest.Disabled;
        if (clip)
        {
            alphaTest = texturePresent && textureHasSwatch ? CanonSurfaceAlphaTest.Paletted : CanonSurfaceAlphaTest.Dds;
            if (blend == CanonSurfaceBlend.Opaque)
                blend = CanonSurfaceBlend.Clip;
        }

        // An authored translucency turns opaque/clip/straight surfaces into a plain alpha blend.
        if (kind.HasFlag(SkinBits.Translucent)
            && (clip || blend is CanonSurfaceBlend.Opaque or CanonSurfaceBlend.StraightAlpha))
        {
            blend = CanonSurfaceBlend.StraightAlpha;
            alphaTest = CanonSurfaceAlphaTest.Disabled;
        }

        return new CanonSurfaceMaterialState(blend, alphaTest, FogEnabled: !additive);
    }
}

public static class SeeThroughKindExtensions
{
    private const SkinBits BlendClan = SkinBits.Additive | SkinBits.Alpha | SkinBits.InvAlpha;

    public static SeeThroughKind FromCanvasKind(SkinBits kind)
    {
        bool translucent = kind.HasFlag(SkinBits.Translucent);
        bool clip = kind.HasFlag(SkinBits.Base1ClipMap);

        // Translucency overrides an otherwise opaque or clip-mapped surface.
        if (translucent && (clip || (kind & BlendClan) == 0))
            return SeeThroughKind.AlphaBlend;

        if (kind.HasFlag(SkinBits.Additive))
            return SeeThroughKind.Additive;
        if (kind.HasFlag(SkinBits.InvAlpha))
            return SeeThroughKind.InvAlpha;
        if ((kind & (SkinBits.Alpha | SkinBits.Translucent)) != 0)
            return SeeThroughKind.AlphaBlend;
        return clip ? SeeThroughKind.ClipMap : SeeThroughKind.Opaque;
    }

    /// <summary>Only surfaces flagged Translucent honour their translucency value.</summary>
    public static float DensityFromCanvasSeeThrough(SkinBits kind, float seeThrough)
    {
        return kind.HasFlag(SkinBits.Translucent) ? Math.Clamp(1f - seeThrough, 0f, 1f) : 1f;
    }

    public static bool DisablesFixedFunctionFog(SkinBits kind) => kind.HasFlag(SkinBits.Additive);
}
