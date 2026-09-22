using System.Numerics;

namespace MacAC.Mechanics.Realm;

/// <summary>One drawable part: a GfxObj and where it sits relative to its entity.</summary>
public readonly record struct TriMeshRef(uint GfxObjId, Matrix4x4 PartTransform)
{
    public IReadOnlyDictionary<uint, uint>? CanvasOverrides { get; init; }
}
