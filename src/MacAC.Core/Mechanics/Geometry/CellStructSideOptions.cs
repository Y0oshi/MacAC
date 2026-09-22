using MacAC.Dat;

namespace MacAC.Mechanics.Geometry;

public enum CellStructFacetSide
{
    Positive = 0,

    Negative = 1,
}

/// <summary>One face a cell polygon may emit, and how to build it.</summary>
public readonly record struct CellStructSideOption(
    CellStructFacetSide SurfaceSlot,
    CellStructFacetSide UvSlot,
    int CopyOrdinal,
    int NormalSign,
    bool ReverseWinding);

/// <summary>The retail sides_type rules for cell-structure polygons.</summary>
public static class CellStructSideOptions
{
    private const int FlanksSingle = 0;
    private const int FlanksDouble = 1;
    private const int FlanksBoth = 2;

    private static readonly CellStructSideOption Front =
        new(CellStructFacetSide.Positive, CellStructFacetSide.Positive, CopyOrdinal: 0, NormalSign: 1, ReverseWinding: false);

    private static readonly CellStructSideOption[] Single = [Front];

    // Same surface both ways: a mirrored copy of the front
    private static readonly CellStructSideOption[] Double =
    [
        Front,
        new(CellStructFacetSide.Positive, CellStructFacetSide.Positive, CopyOrdinal: 1, NormalSign: -1, ReverseWinding: true),
    ];

    // Distinct front and back surfaces
    private static readonly CellStructSideOption[] Both =
    [
        Front,
        new(CellStructFacetSide.Negative, CellStructFacetSide.Negative, CopyOrdinal: 0, NormalSign: -1, ReverseWinding: false),
    ];

    private const SkinBits BlendClan = SkinBits.Alpha | SkinBits.InvAlpha | SkinBits.Additive;

    public static ReadOnlySpan<CellStructSideOption> FetchContenders(int rawFlanksKind)
    {
        return rawFlanksKind switch
        {
            FlanksDouble => Double,
            FlanksBoth => Both,
            _ => Single,
        };
    }

    public static bool IsCanonDefinedFlanksKind(int rawFlanksKind) => rawFlanksKind is FlanksSingle or FlanksDouble or FlanksBoth;

    public static (int A, int B, int C) TriangleFanOrdinals(int triangleOrdinal, bool reverseWinding)
    {
        return reverseWinding
            ? (triangleOrdinal + 2, triangleOrdinal + 1, 0)
            : (0, triangleOrdinal + 1, triangleOrdinal + 2);
    }

    public static bool IsUvAbsent(CellStructSideOption contender, StippleBits stippling)
    {
        StippleBits absent = contender.UvSlot == CellStructFacetSide.Positive ? StippleBits.NoPos : StippleBits.NoNeg;
        return (stippling & absent) != 0;
    }

    public static int StartingCanvasBitmask(SkinBits kind)
    {
        if ((kind & BlendClan) != 0) return 2;
        if ((kind & SkinBits.Base1ClipMap) != 0) return 8;
        return (kind & SkinBits.Translucent) != 0 ? 4 : 0;
    }

    /// <summary>A positive stippling byte on the front face sets bit 0.</summary>
    public static int ImposeStipplingBitmaskBit(int latestBitmask, CellStructFacetSide contenderCanvasSocket, StippleBits stippling)
    {
        if (contenderCanvasSocket != CellStructFacetSide.Positive)
            return latestBitmask;
        return unchecked((sbyte)(byte)stippling) > 0 ? latestBitmask | 1 : latestBitmask;
    }
}
