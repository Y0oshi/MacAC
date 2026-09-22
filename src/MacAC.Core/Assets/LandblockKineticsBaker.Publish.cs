using System.Numerics;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Realm;
using MacAC.Dat;

namespace MacAC.Assets;

/// <summary>Pushing the resolved landblock into the kinetic cache and engine.</summary>
public static partial class LandblockKineticsBaker
{
    public readonly record struct StaticContactTally(int BspOwnerCount, int SetupOwnerCount, int NoCollisionCount);

    public static void BroadcastChambers(
        KineticAssetCache stash,
        MountedLandblock lb,
        LandblockContactBuild impacts,
        Vector3 origin,
        ICollection<CellFacet> chamberCanvases,
        ICollection<PortalFace> gatewayPlanes)
    {
        ArgumentNullException.ThrowIfNull(stash);
        ArgumentNullException.ThrowIfNull(lb);
        ArgumentNullException.ThrowIfNull(impacts);
        ArgumentNullException.ThrowIfNull(chamberCanvases);
        BroadcastChambersRest(stash, lb, impacts, chamberCanvases, gatewayPlanes, origin);
    }

    private static void BroadcastChambersRest(KineticAssetCache stash, MountedLandblock lb, LandblockContactBuild impacts, ICollection<CellFacet> chamberCanvases, ICollection<PortalFace> gatewayPlanes, Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(gatewayPlanes);
        KineticDatBundle datFiles = lb.PhysicsDats ?? KineticDatBundle.Empty;
        uint chamberTally = datFiles.Info?.CellCount ?? 0u;
        for (uint shift = 0; shift < chamberTally; ++shift)
        {
            uint chamberIdent = (lb.LandblockId & 0xFFFF0000u) | (0x0100u + shift);
            if (!datFiles.EnvCells.TryGetValue(chamberIdent, out RoomCell? chamber)
                || !impacts.CellStructures.TryGetValue(chamberIdent, out PackedCellStructContactAsset? structure)
                || !impacts.EnvCells.TryGetValue(chamberIdent, out PackedEnvCellTopology? wiring))

                continue;

            Quaternion spin = chamber.Position.Orientation;
            Vector3 chamberOrigin = chamber.Position.Origin + origin;
            Matrix4x4 xform = Matrix4x4.CreateFromQuaternion(spin) * Matrix4x4.CreateTranslation(chamberOrigin);
            stash.CacheCellStruct(chamberIdent, chamber, xform, structure, wiring);

            var polygs = structure.PortalPolygons;
            foreach (PackedEnvCellPortal gateway in wiring.Portals)
            {
                if ((uint)gateway.PolygonIndex >= (uint)polygs.Polygons.Length)
                    continue;
                var polyg = polygs.Polygons[gateway.PolygonIndex];
                if (polyg.VertexRange.Count < 3)
                    continue;

                Vector3[] realm = new Vector3[polyg.VertexRange.Count];
                for (int idx = 0; idx < realm.Length; ++idx)
                    realm[idx] = Vector3.Transform(polygs.Vertices[polyg.VertexRange.Start + idx], spin) + chamberOrigin;
                gatewayPlanes.Add(PortalFace.FromVertices(realm.AsSpan(), gateway.OtherCellId, chamberIdent & 0xFFFFu, gateway.Flags));
            }

            chamberCanvases.Add(new CellFacet(chamberIdent, structure.PhysicsBsp.PolygChart, spin, chamberOrigin));
        }
    }

    public static void ShelveStructures(KineticAssetCache stash, MountedLandblock lb, LandCanvas land, Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(stash);
        ArgumentNullException.ThrowIfNull(lb);
        ArgumentNullException.ThrowIfNull(land);
        KineticDatBundle datFiles = lb.PhysicsDats ?? KineticDatBundle.Empty;
        if (datFiles.Info is not { } details)
            return;

        uint stem = lb.LandblockId & 0xFFFF0000u;
        foreach (BuildingSpec structure in details.Structures)
        {
            ShelveStructuresLoop(structure, stem, origin, land, datFiles, stash);
        }
    }

    private static void ShelveStructuresLoop(BuildingSpec structure, uint stem, Vector3 origin, LandCanvas land, KineticDatBundle datFiles, KineticAssetCache stash)
    {
        var gateways = new List<BuildingPortalFacts>(structure.Doorways.Count);
        foreach (BuildingDoorway gateway in structure.Doorways)
            gateways.Add(new BuildingPortalFacts(stem | (uint)gateway.OtherCellId, unchecked((short)gateway.OtherPortalId), (ushort)gateway.Bits));
        Matrix4x4 xform = Matrix4x4.CreateFromQuaternion(structure.Pose.Orientation)
                        * Matrix4x4.CreateTranslation(structure.Pose.Origin + origin);
        uint landcellIdent = stem | land.ComputeOutdoorCellId(structure.Pose.Origin.X, structure.Pose.Origin.Y);
        ShelveStructuresTail(gateways, structure, xform, landcellIdent, datFiles, stash);
    }

    private static void ShelveStructuresTail(List<BuildingPortalFacts> gateways, BuildingSpec structure, Matrix4x4 xform, uint landcellIdent, KineticDatBundle datFiles, KineticAssetCache stash)
    {
        uint shellPieceZero = structure.ModelId;
        if (Family(shellPieceZero) == RigStem)
        {
            datFiles.Setups.TryGetValue(structure.ModelId, out RigSpec? rig);
            shellPieceZero = rig is { PartIds.Count: > 0 } ? rig.PartIds[0] : 0u;
        }
        stash.StashStructure(landcellIdent, gateways, xform, shellPieceZero);
    }

    public static void ShelveLinkHoldings(KineticAssetCache stash, LandblockContactBuild impacts)
    {
        ArgumentNullException.ThrowIfNull(stash);
        ArgumentNullException.ThrowIfNull(impacts);
        foreach ((uint ident, PackedGfxObjContactAsset asset) in impacts.GfxObjs)
            stash.CacheGfxObj(ident, asset);
        foreach ((uint ident, PackedSetupContact rig) in impacts.Setups)
            stash.CacheSetup(ident, rig);
    }

    public static StaticContactTally BroadcastStaticLink(
        KineticEngine engine,
        KineticAssetCache stash,
        MountedLandblock lb,
        LandblockContactBuild impacts,
        Vector3 origin)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(stash);
        ArgumentNullException.ThrowIfNull(lb);
        ArgumentNullException.ThrowIfNull(impacts);

        int bspHolders = 0, rigHolders = 0, rasterizeSole = 0;
        foreach (RealmActor actor in lb.Entities)
        {
            if (actor.IsStructureShell)
                continue;

            IReadOnlyList<ProxyShape> bspForms = ProxyShapeBuilder.FromLbBspPieces(actor.MeshRefs, actor.IsStructureShell, stash.FetchGfxObjRef);
            IReadOnlyList<ProxyShape> pieces = ProxyShapeBuilder.FromStaticRasterizePieces(actor.MeshRefs, stash.FetchGfxObjRef, stash.FetchVisualLimits, out _);

            if (bspForms.Count > 0)
            {
                Enrol(engine, actor, bspForms, pieces, lb, origin);
                ++bspHolders;
                continue;
            }

            var rig = stash.FetchPlanarRig(actor.SrcGfxObjRefOrRigIdent);
            List<ProxyShape>? rigForms = rig is null ? null : PrepareForms(actor, rig);
            if (rigForms is not { Count: > 0 })
            {
                if (pieces.Count > 0)
                    Enrol(engine, actor, [], pieces, lb, origin);
                ++rasterizeSole;
                continue;
            }

            Enrol(engine, actor, rigForms, pieces, lb, origin);
            ++rigHolders;
        }

        foreach (uint holderIdent in engine.ShadeObjects.GrabRefloodHoldersForLb(lb.LandblockId))
            engine.ShadeObjects.RefloodHolderForLb(holderIdent, lb.LandblockId);
        return new StaticContactTally(bspHolders, rigHolders, rasterizeSole);
    }

    // Cylinders win; spheres are used only when a setup has no cylinders at all
    private static List<ProxyShape> PrepareForms(RealmActor actor, PackedSetupContact rig)
    {
        float scaling = actor.Scale > 0f ? actor.Scale : 1f;
        List<ProxyShape> forms = new List<ProxyShape>();
        foreach (PackedContactCylinder cylinder in rig.Cylinders)
        {
            float radius = cylinder.Radius * scaling;
            float height = (cylinder.Height > 0f ? cylinder.Height : cylinder.Radius * 4f) * scaling;
            if (radius <= 0f)
                continue;
            forms.Add(ProxyShape.Cylinder(actor.SrcGfxObjRefOrRigIdent, cylinder.Origin * scaling, Quaternion.Identity, scaling, radius, height));
        }

        if (rig.Cylinders.Length is 0)
        {
            foreach (PackedContactSphere orb in rig.Spheres)
            {
                if (orb.Radius <= 0f)
                    continue;
                float radius = orb.Radius * scaling;
                Vector3 shift = orb.Origin * scaling;
                forms.Add(ProxyShape.Sphere(actor.SrcGfxObjRefOrRigIdent, shift, Quaternion.Identity, scaling, radius));
            }
        }
        return forms;
    }

    private static void Enrol(
        KineticEngine engine,
        RealmActor actor,
        IReadOnlyList<ProxyShape> linkForms,
        IReadOnlyList<ProxyShape> pieces,
        MountedLandblock lb,
        Vector3 origin)
    {
        engine.ShadeObjects.EnrollMultiPiece(
            actor.Id,
            actor.Position,
            actor.Rotation,
            linkForms,
            0u,
            ActorImpactFlagSet.None,
            realmShiftX: origin.X,
            realmShiftY: origin.Y,
            lbIdent: lb.LandblockId,
            seedChamberIdent: actor.ParentCellId ?? 0u,
            isStatic: true,
            pieceArr: pieces);
    }
}
