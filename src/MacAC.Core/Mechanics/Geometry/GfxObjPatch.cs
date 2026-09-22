using MacAC.Mechanics.Landscape;

namespace MacAC.Mechanics.Geometry;

public sealed record GfxObjPatch(
    uint SurfaceId,
    MechVertex[] Vertices,
    uint[] Indices)
{
    public SeeThroughKind Translucency { get; init; } = SeeThroughKind.Opaque;

    public float Luminosity { get; init; }

    public float Diffuse { get; init; } = 1f;

    public bool NeedsUvRepeat { get; init; }

    public float SurfOpacity { get; init; } = 1f;

    public bool DisableFog { get; init; }
}
