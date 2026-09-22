namespace MacAC.Mechanics.Landscape;

public static class TerrainBlend
{
    private const int Corners = 4;
    private const int Rotations = 4;
    private const uint LandCodeBitmask = 0x1Fu;

    public static uint FetchPalCode(
        int r1, int r2, int r3, int r4,
        int t1, int t2, int t3, int t4)
    {
        uint land = (uint)((t1 << 15) | (t2 << 10) | (t3 << 5) | t4);
        uint road = (uint)((r1 << 26) | (r2 << 24) | (r3 << 22) | (r4 << 20));
        const uint dims = 1u << 28;
        return dims | road | land;
    }

    public static uint[] DistillLandCodes(uint palCode)
    {
        return [
        (palCode >> 15) & LandCodeBitmask,
        (palCode >> 10) & LandCodeBitmask,
        (palCode >> 5) & LandCodeBitmask,
        palCode & LandCodeBitmask,
    ];
    }

    /// <summary>The retail hash that decides a cell's diagonal.</summary>
    public static CellSplitAxis WorkDivideDir(
        uint lbX, uint chamberX, uint lbY, uint chamberY)
    {
        uint x = lbX * 8 + chamberX;
        uint y = lbY * 8 + chamberY;
        uint digest = unchecked(x * y * 0x0CCAC033u - x * 0x421BE3BDu + y * 0x6C1AC587u - 0x519B8F25u);
        return (digest & 0x80000000u) is not 0 ? CellSplitAxis.SWtoNE : CellSplitAxis.SEtoNW;
    }

    /// <summary>The retail LCG-style pick of one alpha map out of <paramref name="tally"/>.</summary>
    public static int PseudoRandomOrdinal(uint palCode, int tally)
    {
        if (tally <= 0)
            return 0;
        int choose = (int)Math.Floor((1379576222 * palCode - 1372186442) * 2.3283064e-10 * tally);
        return choose >= tally ? 0 : choose;
    }

    /// <summary>Rotates a four-corner code one step clockwise.</summary>
    public static uint SpinLandCode(uint code)
    {
        code *= 2;
        return code >= 16 ? code - 15 : code;
    }

    public static (uint road0, uint road1, bool allRoad) FetchRoadCodes(uint palCode)
    {
        uint corners = 0;
        if ((palCode & 0x0C000000u) is not 0) corners |= 1;   // r1, bits 26-27
        if ((palCode & 0x03000000u) is not 0) corners |= 2;   // r2, bits 24-25
        if ((palCode & 0x00C00000u) is not 0) corners |= 4;   // r3, bits 22-23
        if ((palCode & 0x00300000u) is not 0) corners |= 8;   // r4, bits 20-21

        return corners switch
        {
            0xF => (0, 0, true),
            0x0 => (0, 0, false),
            0xE => (6, 12, false),
            0xD => (9, 12, false),
            0xB => (9, 3, false),
            0x7 => (3, 6, false),
            _ => (corners, 0, false),
        };
    }

    public static (byte alphaLayer, byte rotation) SeekLandAlpha(
        uint palCode, uint landCode, TerrainBlendContext cx)
    {
        bool corner = landCode is 1 or 2 or 4 or 8;
        IReadOnlyList<byte> strata = corner ? cx.CornerAlphaLayers : cx.SideAlphaLayers;
        IReadOnlyList<uint> codes = corner ? cx.CornerAlphaTCodes : cx.SideAlphaTCodes;
        if (strata.Count is 0 || codes.Count is 0)
            return (SurfaceFacts.None, 0);

        int choose = PseudoRandomOrdinal(palCode, strata.Count);
        uint contender = codes[choose];
        for (int turns = 0; turns < Rotations; ++turns)
        {
            if (contender == landCode)
                return (strata[choose], (byte)turns);
            contender = SpinLandCode(contender);
        }
        return (SurfaceFacts.None, 0);
    }

    public static (byte alphaLayer, byte rotation) SeekRoadAlpha(
        uint palCode, uint roadCode, TerrainBlendContext cx)
    {
        var strata = cx.RoadAlphaLayers;
        var codes = cx.RoadAlphaRCodes;
        if (strata.Count is 0 || codes.Count is 0)
            return (SurfaceFacts.None, 0);

        int begin = PseudoRandomOrdinal(palCode, strata.Count);
        for (int hop = 0; hop < strata.Count; ++hop)
        {
            int at = (hop + begin) % strata.Count;
            uint contender = codes[at];
            for (int turns = 0; turns < Rotations; ++turns)
            {
                if (contender == roadCode)
                    return (strata[at], (byte)turns);
                contender = SpinLandCode(contender);
            }
        }
        return (SurfaceFacts.None, 0);
    }

    public static SurfaceFacts AssembleCanvas(uint palCode, TerrainBlendContext cx)
    {
        (uint road0, uint road1, bool allRoad) = FetchRoadCodes(palCode);
        if (allRoad)
            return SurfaceFacts.BaseSole(cx.RoadLayer);

        Span<byte> strata = stackalloc byte[Corners];
        Span<uint> topLayerCodes = stackalloc uint[Corners - 1];
        PlanTopLayers(DistillLandCodes(palCode), cx, strata, topLayerCodes);

        Span<byte> ovlStratum = [SurfaceFacts.None, SurfaceFacts.None, SurfaceFacts.None];
        Span<byte> ovlAlpha = [SurfaceFacts.None, SurfaceFacts.None, SurfaceFacts.None];
        Span<byte> ovlPivot = [0, 0, 0];
        for (int idx = 0; idx < topLayerCodes.Length; ++idx)
        {
            if (topLayerCodes[idx] is 0)
                continue;
            (byte alpha, byte pivot) = SeekLandAlpha(palCode, topLayerCodes[idx], cx);
            if (alpha == SurfaceFacts.None)
                continue;
            ovlStratum[idx] = strata[idx + 1];
            ovlAlpha[idx] = alpha;
            ovlPivot[idx] = pivot;
        }

        // Both road overlays share the road texture; only alpha and rotation differ.
        byte roadStratum = SurfaceFacts.None;
        (byte alpha, byte turn) road0Choose = (SurfaceFacts.None, 0);
        (byte alpha, byte turn) road1Choose = (SurfaceFacts.None, 0);
        if (cx.RoadLayer != SurfaceFacts.None && road0 is not 0)
        {
            road0Choose = SeekRoadAlpha(palCode, road0, cx);
            if (road0Choose.alpha != SurfaceFacts.None)
            {
                roadStratum = cx.RoadLayer;
                if (road1 is not 0)
                {
                    (byte alpha, byte turn) second = SeekRoadAlpha(palCode, road1, cx);
                    if (second.alpha != SurfaceFacts.None)
                        road1Choose = second;
                }
            }
            else
            {
                road0Choose = (SurfaceFacts.None, 0);
            }
        }

        return new SurfaceFacts(
            strata[0],
            ovlStratum[0], ovlAlpha[0], ovlPivot[0],
            ovlStratum[1], ovlAlpha[1], ovlPivot[1],
            ovlStratum[2], ovlAlpha[2], ovlPivot[2],
            roadStratum, road0Choose.alpha, road0Choose.turn,
            road1Choose.alpha, road1Choose.turn);
    }

    /// <summary>Packs a recipe into the four per-vertex data words the shader reads.</summary>
    public static (uint d0, uint d1, uint d2, uint d3) PopulateChamberBlob(SurfaceFacts facts, CellSplitAxis divide)
    {
        static uint Duo(uint texture, uint alpha) => texture | (alpha << 8);

        uint road1Texture = facts.Road1AlphaLayer == SurfaceFacts.None ? SurfaceFacts.None : facts.RoadLayer;

        uint d0 = Duo(facts.BaseLayer, SurfaceFacts.None) | (Duo(facts.Ovl0Layer, facts.Ovl0AlphaLayer) << 16);
        uint d1 = Duo(facts.Ovl1Layer, facts.Ovl1AlphaLayer) | (Duo(facts.Ovl2Layer, facts.Ovl2AlphaLayer) << 16);
        uint d2 = Duo(facts.RoadLayer, facts.Road0AlphaLayer) | (Duo(road1Texture, facts.Road1AlphaLayer) << 16);
        uint d3 = ((uint)facts.Ovl0Rotation << 2)          // bits 0-1 would be the base rotation; always 0
            | ((uint)facts.Ovl1Rotation << 4)
            | ((uint)facts.Ovl2Rotation << 6)
            | ((uint)facts.Road0Rotation << 8)
            | ((uint)facts.Road1Rotation << 10)
            | (((uint)divide & 1u) << 12);
        return (d0, d1, d2, d3);
    }

    // Decides which corner terrains become the base and the overlays
    private static void PlanTopLayers(
        uint[] corners,
        TerrainBlendContext cx,
        Span<byte> strata,
        Span<uint> topLayerCodes)
    {
        for (int idx = 0; idx < Corners; ++idx)
        {
            for (int jdx = idx + 1; jdx < Corners; ++jdx)
            {
                if (corners[idx] == corners[jdx])
                {
                    PlanWithRepeat(corners, cx, strata, topLayerCodes, idx);
                    return;
                }
            }
        }

        for (int idx = 0; idx < Corners; ++idx)
            strata[idx] = StratumFor(cx, corners[idx]);
        topLayerCodes[0] = 2;
        topLayerCodes[1] = 4;
        topLayerCodes[2] = 8;
    }

    private static void PlanWithRepeat(
        uint[] corners,
        TerrainBlendContext cx,
        Span<byte> strata,
        Span<uint> topLayerCodes,
        int repeatedAt)
    {
        uint primary = corners[repeatedAt];
        strata[0] = StratumFor(cx, primary);
        strata[1..].Fill(SurfaceFacts.None);
        topLayerCodes.Clear();

        uint secondary = 0;
        bool haveSecondary = false;
        for (int kdx = 0; kdx < Corners; ++kdx)
        {
            if (corners[kdx] == primary)
                continue;

            if (!haveSecondary)
            {
                topLayerCodes[0] = 1u << kdx;
                secondary = corners[kdx];
                strata[1] = StratumFor(cx, secondary);
                haveSecondary = true;
                continue;
            }

            // The retail rule: an adjacent repeat of the secondary widens its
            // overlay; anything else becomes the third layer. Either way, stop.
            if (corners[kdx] == secondary && topLayerCodes[0] == 1u << (kdx - 1))
            {
                topLayerCodes[0] += 1u << kdx;
            }
            else
            {
                strata[2] = StratumFor(cx, corners[kdx]);
                topLayerCodes[1] = 1u << kdx;
            }
            break;
        }
    }

    private static byte StratumFor(TerrainBlendContext cx, uint landCode)
    {
        return cx.TerrainTypeToLayer.TryGetValue(landCode, out byte stratum) ? stratum : SurfaceFacts.None;
    }
}
