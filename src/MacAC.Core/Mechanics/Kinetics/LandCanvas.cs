using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

public readonly record struct TerrainFacetPolygon(
    float Z,
    Vector3 Normal,
    TerrainTriVerts Vertices);

public readonly record struct TerrainTriVerts(
    Vector3 V0,
    Vector3 V1,
    Vector3 V2)
{
    public int Length => 3;

    public Vector3 this[int index] => index switch
    {
        0 => V0,
        1 => V1,
        2 => V2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

public sealed class LandCanvas
{
    private const int HeightmapFlank = 9;
    private const int HeightTally = HeightmapFlank * HeightmapFlank;
    private const int HeightChartDims = 256;
    public const float ChamberDims = 24f;
    public const int ChambersPerFlank = 8;  // 192 / 24

    private const byte NotWater = 0;
    private const byte PartiallyWater = 1;
    private const byte EntirelyWater = 2;

    private readonly float[,] _z;               // pre-resolved heights [x, y]
    private readonly bool[,] _cornerIsWater;    // per-VERTEX water flag [x, y] - SurfChar[(type >> 2) & 0x1F]
    private readonly byte[,] _chamberWater;
    private readonly uint _lbX;
    private readonly uint _lbY;

    // Which land cell a local point falls in and where inside it
    private readonly record struct CellHit(int Cx, int Cy, float Tx, float Ty);

    // The four corner heights of a land cell and the retail interpolation over them
    private readonly record struct Quad(float BL, float BR, float TR, float TL)
    {
        // Which of the two triangles the point is in: true = the one touching BR (SW/NE split) or BL
        // (NW/SE split)
        public static bool LowerTriangle(float tx, float ty, bool divideSWtoNE) => divideSWtoNE ? tx > ty : tx + ty <= 1f;

        public float Z(float tx, float ty, bool divideSWtoNE)
        {
            if (divideSWtoNE)
            {
                return tx > ty
                    ? BL + (BR - BL) * tx + (TR - BR) * ty
                    : BL + (TR - TL) * tx + (TL - BL) * ty;
            }

            return tx + ty <= 1f
                ? BL + (BR - BL) * tx + (TL - BL) * ty
                : TR + (TL - TR) * (1f - tx) + (BR - TR) * (1f - ty);
        }

        public (float DzDx, float DzDy) Slope(float tx, float ty, bool divideSWtoNE)
        {
            bool lower = LowerTriangle(tx, ty, divideSWtoNE);
            if (divideSWtoNE)
            {
                return lower
                    ? ((BR - BL) / ChamberDims, (TR - BR) / ChamberDims)
                    : ((TR - TL) / ChamberDims, (TL - BL) / ChamberDims);
            }
            return lower
                ? ((BR - BL) / ChamberDims, (TL - BL) / ChamberDims)
                : ((TR - TL) / ChamberDims, (TR - BR) / ChamberDims);
        }
    }

    public LandCanvas(byte[] heights, float[] heightTable,
        uint lbX = 0, uint lbY = 0,
        byte[]? landKinds = null)
        : this(
            heights,
            (heightTable ?? throw new ArgumentNullException(nameof(heightTable))).AsSpan(),
            lbX,
            lbY,
            landKinds)
    {
    }

    public LandCanvas(
        byte[] heights,
        ReadOnlySpan<float> heightChart,
        uint lbX = 0,
        uint lbY = 0,
        byte[]? landKinds = null)
    {
        Validate(heights, heightChart);

        _lbX = lbX;
        _lbY = lbY;

        // Pre-resolve all 81 heights so SampleZ is a pure lookup + lerp
        _z = new float[HeightmapFlank, HeightmapFlank];
        for (int x = 0; x < HeightmapFlank; ++x)
        {
            for (int y = 0; y < HeightmapFlank; ++y)
                _z[x, y] = heightChart[heights[x * HeightmapFlank + y]];
        }

        _cornerIsWater = new bool[HeightmapFlank, HeightmapFlank];
        if (landKinds is not null && landKinds.Length >= HeightTally)
        {
            for (int x = 0; x < HeightmapFlank; ++x)
            {
                for (int y = 0; y < HeightmapFlank; ++y)
                {
                    int canvas = (landKinds[x * HeightmapFlank + y] >> 2) & 0x1F;
                    _cornerIsWater[x, y] = canvas is >= 0x10 and <= 0x14;
                }
            }
        }

        _chamberWater = new byte[ChambersPerFlank, ChambersPerFlank];
        for (int cx = 0; cx < ChambersPerFlank; ++cx)
        {
            for (int cy = 0; cy < ChambersPerFlank; ++cy)
            {
                int wet = 0;
                if (_cornerIsWater[cx, cy]) ++wet;
                if (_cornerIsWater[cx + 1, cy]) ++wet;
                if (_cornerIsWater[cx + 1, cy + 1]) ++wet;
                if (_cornerIsWater[cx, cy + 1]) ++wet;
                _chamberWater[cx, cy] = wet switch
                {
                    0 => NotWater,
                    4 => EntirelyWater,
                    _ => PartiallyWater,
                };
            }
        }
    }

    public float ProbeZ(float ownX, float ownY)
    {
        CellHit strike = Locate(ownX, ownY);
        return CornersOf(strike).Z(strike.Tx, strike.Ty, Split(strike));
    }

    public static float SampleZFromHeightmap(
        byte[] heights, float[] heightChart,
        uint lbX, uint lbY,
        float ownX, float ownY)
    {
        ArgumentNullException.ThrowIfNull(heightChart);
        return SampleZFromHeightmap(heights, heightChart.AsSpan(), lbX, lbY, ownX, ownY);
    }

    public static float SampleZFromHeightmap(
        byte[] heights,
        ReadOnlySpan<float> heightChart,
        uint lbX,
        uint lbY,
        float ownX,
        float ownY)
    {
        Validate(heights, heightChart);
        CellHit strike = Locate(ownX, ownY);
        bool divide = IsDivideSWtoNE(lbX, (uint)strike.Cx, lbY, (uint)strike.Cy);
        return CornersOf(heights, heightChart, strike).Z(strike.Tx, strike.Ty, divide);
    }

    public static float ProbeNormZFromHeightmap(
        byte[] heights, float[] heightChart,
        uint lbX, uint lbY,
        float ownX, float ownY)
    {
        ArgumentNullException.ThrowIfNull(heightChart);
        Validate(heights, heightChart);
        CellHit strike = Locate(ownX, ownY);
        bool divide = IsDivideSWtoNE(lbX, (uint)strike.Cx, lbY, (uint)strike.Cy);
        (float dzdx, float dzdy) = CornersOf(heights, heightChart, strike).Slope(strike.Tx, strike.Ty, divide);
        return 1f / MathF.Sqrt(dzdx * dzdx + dzdy * dzdy + 1f);
    }

    public (float Z, Vector3 Normal) ProbeCanvas(float ownX, float ownY)
    {
        CellHit strike = Locate(ownX, ownY);
        Quad quad = CornersOf(strike);
        bool divide = Split(strike);
        float z = quad.Z(strike.Tx, strike.Ty, divide);
        (float dzdx, float dzdy) = quad.Slope(strike.Tx, strike.Ty, divide);
        return (z, Vector3.Normalize(new Vector3(-dzdx, -dzdy, 1f)));
    }

    public TerrainFacetPolygon ProbeCanvasPolyg(float ownX, float ownY)
    {
        CellHit strike = Locate(ownX, ownY);
        Quad quad = CornersOf(strike);
        bool divide = Split(strike);
        float z = quad.Z(strike.Tx, strike.Ty, divide);

        Vector3 bl = new(strike.Cx * ChamberDims, strike.Cy * ChamberDims, quad.BL);
        Vector3 br = new((strike.Cx + 1) * ChamberDims, strike.Cy * ChamberDims, quad.BR);
        Vector3 tr = new((strike.Cx + 1) * ChamberDims, (strike.Cy + 1) * ChamberDims, quad.TR);
        Vector3 tl = new(strike.Cx * ChamberDims, (strike.Cy + 1) * ChamberDims, quad.TL);

        bool lower = Quad.LowerTriangle(strike.Tx, strike.Ty, divide);
        TerrainTriVerts verts = divide
            ? (lower ? new TerrainTriVerts(bl, br, tr) : new TerrainTriVerts(bl, tr, tl))
            : (lower ? new TerrainTriVerts(bl, br, tl) : new TerrainTriVerts(br, tr, tl));

        Vector3 norm = Vector3.Normalize(Vector3.Cross(verts[1] - verts[0], verts[2] - verts[0]));
        if (norm.Z < 0f)
            norm = -norm;

        return new TerrainFacetPolygon(z, norm, verts);
    }

    public float TasteWaterZDepth(float ownX, float ownY)
    {
        CellHit strike = Locate(ownX, ownY);
        switch (_chamberWater[strike.Cx, strike.Cy])
        {
            case NotWater:
                return 0f;
            case EntirelyWater:
                return 0.9f;
        }

        // Partially water: the nearest corner decides.
        int vx = strike.Cx + (strike.Tx >= 0.5f ? 1 : 0);
        int vy = strike.Cy + (strike.Ty >= 0.5f ? 1 : 0);
        return _cornerIsWater[vx, vy] ? 0.45f : 0.1f;
    }

    public uint ComputeOutdoorCellId(float ownX, float ownY) => CalculateExteriorChamberLoIdent(ownX, ownY);

    public static uint CalculateExteriorChamberLoIdent(float ownX, float ownY)
    {
        int cx = Math.Clamp((int)(ownX / ChamberDims), 0, ChambersPerFlank - 1);
        int cy = Math.Clamp((int)(ownY / ChamberDims), 0, ChambersPerFlank - 1);
        return (uint)(1 + cx * ChambersPerFlank + cy);
    }

    public static uint ComputeOutdoorCellId(uint lbIdent, float ownX, float ownY) =>
        (lbIdent & 0xFFFF0000u) | CalculateExteriorChamberLoIdent(ownX, ownY);

    private static void Validate(byte[] heights, ReadOnlySpan<float> heightTable)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (heights.Length < HeightTally)
            throw new ArgumentException("heights must have 81 entries", nameof(heights));
        if (heightTable.Length < HeightChartDims)
            throw new ArgumentException("heightTable must have 256 entries", nameof(heightTable));
    }

    private static CellHit Locate(float ownX, float ownY)
    {
        float fx = Math.Clamp(ownX / ChamberDims, 0f, ChambersPerFlank - 0.001f);
        float fy = Math.Clamp(ownY / ChamberDims, 0f, ChambersPerFlank - 0.001f);
        int cx = Math.Clamp((int)fx, 0, ChambersPerFlank - 1);
        int cy = Math.Clamp((int)fy, 0, ChambersPerFlank - 1);
        return new CellHit(cx, cy, fx - cx, fy - cy);
    }

    private Quad CornersOf(in CellHit strike)
    {
        return new(_z[strike.Cx, strike.Cy], _z[strike.Cx + 1, strike.Cy], _z[strike.Cx + 1, strike.Cy + 1], _z[strike.Cx, strike.Cy + 1]);
    }

    private static Quad CornersOf(byte[] heights, ReadOnlySpan<float> heightChart, in CellHit strike)
    {
        return new(
        heightChart[heights[strike.Cx * HeightmapFlank + strike.Cy]],
        heightChart[heights[(strike.Cx + 1) * HeightmapFlank + strike.Cy]],
        heightChart[heights[(strike.Cx + 1) * HeightmapFlank + (strike.Cy + 1)]],
        heightChart[heights[strike.Cx * HeightmapFlank + (strike.Cy + 1)]]);
    }

    private bool Split(in CellHit strike) => IsDivideSWtoNE(_lbX, (uint)strike.Cx, _lbY, (uint)strike.Cy);

    // The retail per-cell hash that decides which diagonal splits the quad
    private static bool IsDivideSWtoNE(uint lbX, uint chamberX, uint lbY, uint chamberY)
    {
        uint x = lbX * 8 + chamberX;
        uint y = lbY * 8 + chamberY;
        uint dw = unchecked(x * y * 0x0CCAC033u - x * 0x421BE3BDu + y * 0x6C1AC587u - 0x519B8F25u);
        return (dw & 0x80000000u) is not 0;
    }
}
