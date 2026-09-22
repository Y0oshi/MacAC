using MacAC.Dat;
using MacAC.Client.Graphics.Gpu;
using MacAC.Mechanics.Geometry;

namespace MacAC.Client.Graphics.Batching;

/// <summary>
/// What makes two draws shareable. Every field is here because it cannot vary within one draw call:
/// the first three say which triangles (instancing repeats one piece of geometry), the texture slot
/// and layer say what is bound, and the rest is pipeline state — blend and depth for translucency,
/// the surface flags the fragment shader branches on, the wind variant the vertex shader applies,
/// the opacity authored on the surface, and which faces are culled.
///
/// Getting the set wrong is quiet either way: too few fields merges draws that differ, too many
/// splits every batch into groups of one. Anything not here varies per instance and rides in
/// <see cref="InstRoster"/>.
/// </summary>
internal readonly record struct ClusterTag(
    uint FirstIndex,
    int BaseVertex,
    int IndexCount,
    GpuTextureSlot TextureSlot,
    uint TextureLayer,
    SeeThroughKind Translucency,
    CanonSurfaceMaterialState MaterialState,
    uint FoliageFlags,
    float SurfaceOpacity = 1f,
    FaceCulling CullMode = FaceCulling.CounterClockwise);
