using System.Numerics;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Dat;

namespace MacAC.Assets;

public static partial class LandblockKineticsBaker
{
    private const uint GfxObjRefStem = 0x01000000u;
    private const uint RigStem = 0x02000000u;
    private const string HeightChartTooShort = "The retail terrain height table must contain at least 256 entries.";

    public static IReadOnlyList<RealmActor> FastenStaticTriMeshes(
        IDatAccess datFiles,
        MountedLandblock src,
        Vector3 realmShift,
        bool includeVisualLimits = true)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(src);

        List<RealmActor> outcome = new List<RealmActor>(src.Entities.Count);
        foreach (RealmActor seed in src.Entities)
        {
            MeshGather collect = new MeshGather(datFiles, includeVisualLimits);
            collect.Collect(seed.SrcGfxObjRefOrRigIdent, scaling: null);
            if (collect.Empty)
                continue;

            RealmActor actor = new RealmActor
            {
                Id = seed.Id,
                SrcGfxObjRefOrRigIdent = seed.SrcGfxObjRefOrRigIdent,
                Position = seed.Position + realmShift,
                Rotation = seed.Rotation,
                MeshRefs = collect.Refs,
                FxChamberIdent = seed.FxChamberIdent,
                IsStructureShell = seed.IsStructureShell,
                StructureShellMooringChamberIdent = seed.StructureShellMooringChamberIdent,
            };
            collect.StampLimits(actor);
            outcome.Add(actor);
        }
        return outcome;
    }

    private struct MeshGather(IDatAccess datFiles, bool withLimits)
    {
        public readonly List<TriMeshRef> Refs = [];
        private ExtentAccumulator _reach;

        public readonly bool Empty => Refs.Count is 0;

        public void Collect(uint srcIdent, Matrix4x4? scaling)
        {
            if (Family(srcIdent) == GfxObjRefStem)
            {
                if (datFiles.Get<PartMesh>(srcIdent) is not { } gfx)
                    return;
                Matrix4x4 xform = scaling ?? Matrix4x4.Identity;
                Expand(gfx, xform);
                Refs.Add(new TriMeshRef(srcIdent, xform));
            }
            else if (Family(srcIdent) == RigStem)
            {
                if (datFiles.Get<RigSpec>(srcIdent) is not { } rig)
                    return;
                foreach (TriMeshRef piece in RigTriMesh.Flatten(rig))
                {
                    if (datFiles.Get<PartMesh>(piece.GfxObjId) is not { } gfx)
                        continue;
                    Matrix4x4 xform = scaling is { } s ? piece.PartTransform * s : piece.PartTransform;
                    Expand(gfx, xform);
                    Refs.Add(scaling is null ? piece : new TriMeshRef(piece.GfxObjId, xform));
                }
            }
        }

        public readonly void StampLimits(RealmActor actor)
        {
            if (_reach.TryGet(out Vector3 lower, out Vector3 upper))
                actor.AssignOwnLimits(lower, upper);
        }

        private void Expand(PartMesh gfx, Matrix4x4 xform)
        {
            if (withLimits && GfxObjExtent.Get(gfx) is { } bbox)
                _reach.Add(xform, bbox);
        }
    }

    public static IReadOnlyList<RealmActor> ExpandScenery(
        IDatAccess datFiles,
        MountedLandblock src,
        Vector3 realmShift,
        ReadOnlySpan<float> heightTable,
        bool includeVisualLimits = true)
    {
        ArgumentNullException.ThrowIfNull(datFiles);
        ArgumentNullException.ThrowIfNull(src);
        if (heightTable.Length < 256)
            throw new ArgumentException(HeightChartTooShort, nameof(heightTable));

        if (datFiles.Get<WorldRegion>(0x13000000u) is not { } zone)
            return [];

        var stances = SceneryGrower.Produce(
            datFiles, zone, src.Heightmap, src.LandblockId, StructureFootprint(datFiles, src.LandblockId));
        if (stances.Count is 0)
            return [];

        uint lbx = (src.LandblockId >> 24) & 0xFFu;
        uint lby = (src.LandblockId >> 16) & 0xFFu;
        uint serialNo = 0u;
        List<RealmActor> outcome = new List<RealmActor>(stances.Count);
        foreach (SceneryGrower.SceneryPlacement stance in stances)
        {
            MeshGather collect = new MeshGather(datFiles, includeVisualLimits);
            collect.Collect(stance.ObjectId, Matrix4x4.CreateScale(stance.Scale));
            if (collect.Empty)
                continue;

            float x = stance.LocalPosition.X;
            float y = stance.LocalPosition.Y;
            float terrain = LandCanvas.SampleZFromHeightmap(src.Heightmap.Heights, heightTable, lbx, lby, x, y);
            RealmActor actor = new RealmActor
            {
                Id = SceneryIdPool.Allocate(lbx, lby, ref serialNo),
                SrcGfxObjRefOrRigIdent = stance.ObjectId,
                Position = new Vector3(x, y, terrain + stance.LocalPosition.Z) + realmShift,
                Rotation = stance.Rotation,
                MeshRefs = collect.Refs,
                Scale = stance.Scale,
                FxChamberIdent = LandCanvas.ComputeOutdoorCellId(src.LandblockId, x, y),
            };
            collect.StampLimits(actor);
            outcome.Add(actor);
        }
        return outcome;
    }

    // Which DBObj family a DID belongs to, by its top byte
    private static uint Family(uint did) => did & 0xFF000000u;

    // The 9×9 land cells that buildings sit on, or null when the landblock has no info record
    private static HashSet<int>? StructureFootprint(IDatAccess datFiles, uint lbIdent)
    {
        if (datFiles.Get<TerrainTileExtras>(DetailsIdentOf(lbIdent)) is not { } details)
            return null;

        HashSet<int> chambers = new HashSet<int>();
        foreach (BuildingSpec structure in details.Structures)
        {
            int cx = Math.Clamp((int)(structure.Pose.Origin.X / 24f), 0, 8);
            int cy = Math.Clamp((int)(structure.Pose.Origin.Y / 24f), 0, 8);
            chambers.Add(cx * 9 + cy);
        }
        return chambers;
    }

    private static uint DetailsIdentOf(uint lbIdent) => (lbIdent & 0xFFFF0000u) | 0xFFFEu;
}
