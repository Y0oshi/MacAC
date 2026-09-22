using System.Numerics;
using MacAC.Client.Graphics.Batching;

namespace MacAC.Client.Graphics.Stride;

internal enum StrollPaintJuncture : byte
{
    Terrain,

    OutdoorStatic,

    CellStatic,

    BuildingShell,

    PortalPunch,

    LookInStatic,

    // Every non-static draw the walk visits last: entities, monsters, items, and the meshes particles
    // ride
    Dynamic,
}

internal readonly record struct OrderedDrawDirective(
    ClusterTag Key,
    Matrix4x4 Transform,
    StrollPaintJuncture Stage,
    uint CellId,
    uint ClipSlot,
    RealmPaintRouter.InstLampGroup Lights,
    uint IndoorFlag,
    float Alpha,
    Vector2 SelectionLighting,
    uint DetailCategory,
    bool AllowInstanceMerge = false);

internal sealed class SequencedPaintFlow
{
    public readonly List<ClusterTag> Keys = [];
    public readonly List<Matrix4x4> Transforms = [];
    public readonly List<StrollPaintJuncture> Junctures = [];
    public readonly List<uint> ChamberIdents = [];
    public readonly List<uint> ClipSockets = [];
    public readonly List<RealmPaintRouter.InstLampGroup> Lights = [];
    public readonly List<uint> InsideFlags = [];
    public readonly List<float> Alphas = [];
    public readonly List<Vector2> PickLighting = [];
    public readonly List<uint> SpecificsCategories = [];
    public readonly List<bool> AllowInstMerges = [];

    public int Count => Keys.Count;

    public void Tack(in OrderedDrawDirective directive)
    {
        Keys.Add(directive.Key);
        Transforms.Add(directive.Transform);
        Junctures.Add(directive.Stage);
        ChamberIdents.Add(directive.CellId);
        ClipSockets.Add(directive.ClipSlot);
        Lights.Add(directive.Lights);
        InsideFlags.Add(directive.IndoorFlag);
        Alphas.Add(directive.Alpha);
        PickLighting.Add(directive.SelectionLighting);
        SpecificsCategories.Add(directive.DetailCategory);
        AllowInstMerges.Add(directive.AllowInstanceMerge);
    }

    public void Reset()
    {
        Keys.Clear();
        Transforms.Clear();
        Junctures.Clear();
        ChamberIdents.Clear();
        ClipSockets.Clear();
        Lights.Clear();
        InsideFlags.Clear();
        Alphas.Clear();
        PickLighting.Clear();
        SpecificsCategories.Clear();
        AllowInstMerges.Clear();
    }
}
