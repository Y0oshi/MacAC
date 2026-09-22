using System.Collections.Concurrent;
using System.Numerics;
using MacAC.Dat;

namespace MacAC.Mechanics.Geometry;

/// <summary>Axis-aligned local bounds of a GfxObj, memoised by id.</summary>
public static class GfxObjExtent
{
    private static readonly ConcurrentDictionary<uint, (Vector3 Min, Vector3 Max)> Recognized = new();

    public static (Vector3 Min, Vector3 Max)? Get(PartMesh? gfx)
    {
        if (gfx is null)
            return null;
        if (Recognized.TryGetValue(gfx.Id, out var limits))
            return limits;

        ExtentAccumulator bbox = new ExtentAccumulator();
        foreach (MeshVertex vert in gfx.Vertices.ByIndex.Values)
            bbox.Add(new Vector3(vert.Position.X, vert.Position.Y, vert.Position.Z));
        if (!bbox.TryGet(out Vector3 lower, out Vector3 upper))
            return null;

        Recognized[gfx.Id] = (lower, upper);
        return (lower, upper);
    }
}

/// <summary>Grows an axis-aligned box around points or transformed boxes.</summary>
public struct ExtentAccumulator
{
    private Vector3 _lower;
    private Vector3 _upper;
    private bool _any;

    public void Add(Vector3 pt)
    {
        if (_any)
        {
            _lower = Vector3.Min(_lower, pt);
            _upper = Vector3.Max(_upper, pt);
        }
        else
        {
            _lower = _upper = pt;
            _any = true;
        }
    }

    public void Add(Matrix4x4 pieceXform, (Vector3 Min, Vector3 Max) pieceLimits)
    {
        (Vector3 lo, Vector3 hi) = pieceLimits;
        for (int corner = 0; corner < 8; ++corner)
        {
            Vector3 own = new Vector3(
                (corner & 1) is 0 ? lo.X : hi.X,
                (corner & 2) is 0 ? lo.Y : hi.Y,
                (corner & 4) is 0 ? lo.Z : hi.Z);
            Add(Vector3.Transform(own, pieceXform));
        }
    }

    public readonly bool TryGet(out Vector3 lower, out Vector3 upper)
    {
        lower = _lower;
        upper = _upper;
        return _any;
    }
}
