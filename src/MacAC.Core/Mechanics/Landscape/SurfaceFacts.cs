namespace MacAC.Mechanics.Landscape;

public readonly record struct SurfaceFacts(
    byte BaseLayer,
    byte Ovl0Layer, byte Ovl0AlphaLayer, byte Ovl0Rotation,
    byte Ovl1Layer, byte Ovl1AlphaLayer, byte Ovl1Rotation,
    byte Ovl2Layer, byte Ovl2AlphaLayer, byte Ovl2Rotation,
    byte RoadLayer, byte Road0AlphaLayer, byte Road0Rotation,
    byte Road1AlphaLayer, byte Road1Rotation)
{
    public const byte None = 255;

    /// <summary>A recipe with only the base texture and every overlay switched off.</summary>
    public static SurfaceFacts BaseSole(byte baseStratum)
    {
        return new(
        baseStratum,
        None, None, 0,
        None, None, 0,
        None, None, 0,
        None, None, 0,
        None, 0);
    }
}

/// <summary>The region's layer tables, resolved once per landblock region.</summary>
public sealed record TerrainBlendContext(
    IReadOnlyDictionary<uint, byte> TerrainTypeToLayer,
    byte RoadLayer,
    IReadOnlyList<byte> CornerAlphaLayers,
    IReadOnlyList<byte> SideAlphaLayers,
    IReadOnlyList<byte> RoadAlphaLayers,
    IReadOnlyList<uint> CornerAlphaTCodes,
    IReadOnlyList<uint> SideAlphaTCodes,
    IReadOnlyList<uint> RoadAlphaRCodes);
