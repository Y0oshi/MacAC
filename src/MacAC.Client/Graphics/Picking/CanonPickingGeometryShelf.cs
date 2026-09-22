using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Targeting;

namespace MacAC.Client.Graphics.Picking;

internal sealed class CanonPickingGeometryShelf(IDatAccess dats, object datLock) : ICanonPickingGeometrySource
{
    private readonly IDatAccess _datFiles = dats ?? throw new ArgumentNullException(nameof(dats));
    private readonly object _datMutex = datLock ?? throw new ArgumentNullException(nameof(datLock));
    private readonly Dictionary<uint, CanonPickMesh?> _stash = [];

    public CanonPickMesh? Resolve(uint gfxObjRefIdent)
    {
        if (_stash.TryGetValue(gfxObjRefIdent, out var stashed))
            return stashed;
        var fetched = Load(gfxObjRefIdent);
        _stash[gfxObjRefIdent] = fetched;
        return fetched;
    }

    private CanonPickMesh? Load(uint gfxObjRefIdent)
    {
        PartMesh? gfx;
        lock (_datMutex)
            gfx = _datFiles.Get<PartMesh>(gfxObjRefIdent);

        var trunk = gfx?.DrawTree?.Root;
        if (gfx is null || trunk is null || trunk.Bounds.Radius <= 0f)
            return null;

        var polygs = new List<CanonPickPolygon>(gfx.Facets.Count);
        foreach (var listing in gfx.Facets)
        {
            Facet src = listing.Value;
            if (src.VertexIds.Count < 3)
                continue;

            Vector3[] verts = new Vector3[src.VertexIds.Count];
            bool valid = true;
            for (int idx = 0; idx < src.VertexIds.Count; ++idx)
            {
                if (!gfx.Vertices.ByIndex.TryGetValue((ushort)src.VertexIds[idx], out var vert))
                {
                    valid = false;
                    break;
                }
                verts[idx] = vert.Position;
            }
            if (!valid)
                continue;

            polygs.Add(new CanonPickPolygon(
                verts,
                SingleSided: (int)src.Culling is 0));
        }

        return new CanonPickMesh(
            trunk.Bounds.Center,
            trunk.Bounds.Radius,
            polygs);
    }
}
