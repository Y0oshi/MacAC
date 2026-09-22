using System.Numerics;
using MacAC.Dat;
using MacAC.Assets;
using MacAC.Mechanics.Geometry;
using MacAC.Mechanics.Realm;

namespace MacAC.Client.Graphics.Stride;

public static class StrideBuildingMint
{
    public sealed record Entry(
        StrollStructure Building, Matrix4x4 WorldTransform, Matrix4x4 InverseWorldTransform)
    {
        public Matrix4x4 PieceZeroRealmXform { get; } =
            Building.PieceZeroXform * WorldTransform;

        public Matrix4x4 InvPieceZeroRealmXform { get; } =
            Invert(Building.PieceZeroXform * WorldTransform);

        private static Matrix4x4 Invert(Matrix4x4 val)
        {
            return !Matrix4x4.Invert(val, out Matrix4x4 inv)
                ? throw new InvalidOperationException("A building part-zero transform isn't invertible")
                : inv;
        }
    }

    public static List<Entry> Build(
        IDatAccess datFiles, uint lbIdent,
        IReadOnlyList<BuildingSpec>? structureInfos, Vector3 lbShift)
    {
        List<Entry> outcome = new List<Entry>();
        if (structureInfos is null)
            return outcome;
        uint lbBitmask = lbIdent & 0xFFFF0000u;

        foreach (BuildingSpec structureDetails in structureInfos)
        {
            Vector3 origin = new(
                structureDetails.Pose.Origin.X,
                structureDetails.Pose.Origin.Y,
                structureDetails.Pose.Origin.Z);
            int chamberX = (int)MathF.Floor(origin.X / 24f);
            int chamberY = (int)MathF.Floor(origin.Y / 24f);
            uint locusChamberIdent = lbBitmask | (uint)(chamberX * 8 + chamberY + 1);

            StrideBldPortal[] gateways = new StrideBldPortal[structureDetails.Doorways.Count];
            for (int idx = 0; idx < gateways.Length; ++idx)
            {
                BuildingDoorway gateway = structureDetails.Doorways[idx];
                gateways[idx] = new StrideBldPortal
                {
                    PortalSide = UnpackStructureFlank((ushort)gateway.Bits),
                    PreciseFit = ((ushort)gateway.Bits & 0x1) != 0,
                    OtherCellId = gateway.OtherCellId == 0xFFFF
                        ? 0xFFFFFFFFu
                        : lbBitmask | gateway.OtherCellId,
                    AnotherGatewayIdent = unchecked((short)gateway.OtherPortalId),
                    StabRoster = [.. gateway.StabIds.Select(s => lbBitmask | s)],
                };
            }

            StrideBspNode? bsp = null;
            Vector3 orderMiddle = Vector3.Zero;
            uint pieceZeroGfxObjRefIdent = 0;
            Matrix4x4 pieceZeroXform = Matrix4x4.Identity;
            float pieceZeroScalingZ = 1f;
            var downgradeTiers = new List<StrideBuildingDegradeLevel>();
            if (datFiles.Get<PartMesh>(structureDetails.ModelId) is PartMesh straightGfxObjRef)
            {
                pieceZeroGfxObjRefIdent = structureDetails.ModelId;
                FillGfx(straightGfxObjRef);
            }
            else if (datFiles.Get<RigSpec>(structureDetails.ModelId) is RigSpec rig)
            {
                var pieces = RigTriMesh.Flatten(rig);
                if (pieces.Count > 0)
                {
                    TriMeshRef pieceZero = pieces[0];
                    pieceZeroGfxObjRefIdent = pieceZero.GfxObjId;
                    pieceZeroXform = pieceZero.PartTransform;
                    pieceZeroScalingZ = rig.DefaultScale.Count > 0
                        ? rig.DefaultScale[0].Z
                        : 1f;
                    if (datFiles.Get<PartMesh>(pieceZero.GfxObjId) is PartMesh rigGfxObjRef)
                        FillGfx(rigGfxObjRef);
                }
            }

            void FillGfx(PartMesh gfxObjRef)
            {
                bsp = TranslateDrawingBsp(gfxObjRef, gfxObjRef.DrawTree?.Root);
                orderMiddle = new Vector3(gfxObjRef.SortCenter.X, gfxObjRef.SortCenter.Y, gfxObjRef.SortCenter.Z);
                if (gfxObjRef.LodTableId is not 0
                    && datFiles.Get<LodTable>(gfxObjRef.LodTableId)
                        is LodTable downgradeDetails)
                {
                    foreach (LodLevel tier in downgradeDetails.Levels)
                    {
                        StrideBspNode? tierBsp = null;
                        if (tier.PartMeshId != 0
                            && datFiles.Get<PartMesh>((uint)tier.PartMeshId) is PartMesh tierGfx)
                        {
                            tierBsp = TranslateDrawingBsp(tierGfx, tierGfx.DrawTree?.Root);
                        }
                        downgradeTiers.Add(
                            new StrideBuildingDegradeLevel(
                                (uint)tier.PartMeshId,
                                tier.DegradeMode,
                                tier.MinDist,
                                tier.IdealDist,
                                tier.MaxDist,
                                tierBsp));
                    }
                }
            }

            Matrix4x4 realmXform =
                Matrix4x4.CreateFromQuaternion(structureDetails.Pose.Orientation)
                * Matrix4x4.CreateTranslation(origin + lbShift);
            Matrix4x4.Invert(realmXform, out Matrix4x4 inv);
            outcome.Add(new Entry(
                new StrollStructure
                {
                    LocusChamberIdent = locusChamberIdent,
                    Portals = gateways,
                    GfxObjId = pieceZeroGfxObjRefIdent,
                    DrawingBsp = bsp,
                    DowngradeTiers = [.. downgradeTiers],
                    SortCenter = orderMiddle,
                    PieceZeroXform = pieceZeroXform,
                    PieceZeroScalingZ = pieceZeroScalingZ,
                },
                realmXform,
                inv));
        }
        return outcome;
    }

    private static int UnpackStructureFlank(ushort flagSet) => (flagSet & 0x2) is not 0 ? 0 : 1;

    private static StrideBspNode? TranslateDrawingBsp(PartMesh gfxObjRef, DrawingBspNode? joint)
    {
        if (joint is null) return null;
        StrideBspNode converted = new StrideBspNode
        {
            SplittingPlane = new StridePlane(joint.Splitter.Normal, joint.Splitter.D),
            IsFail = joint.Tag == BspTag.Leaf,
            PosNode = TranslateDrawingBsp(gfxObjRef, joint.Front),
            NegNode = TranslateDrawingBsp(gfxObjRef, joint.Back),
        };
        if (joint.Tag == BspTag.Portal && joint.Portals is not null)
        {
            var refs = new List<StridePortalRef>(joint.Portals.Count);
            foreach (DoorwayPoly gatewayRef in joint.Portals)
            {
                var polyg = AssembleGfxPolyg(gfxObjRef, gatewayRef.PolygonId);
                if (polyg is not null)
                    refs.Add(new StridePortalRef
                    {
                        GatewayOrdinal = gatewayRef.PortalIndex,
                        Polygon = polyg,
                    });
            }
            converted.InGateways = [.. refs];
        }
        return converted;
    }

    private static StridePolygon? AssembleGfxPolyg(PartMesh gfxObjRef, ushort polygIdent)
    {
        return !gfxObjRef.Facets.TryGetValue(polygIdent, out Facet? poly)
            || poly is null || poly.VertexIds.Count < 3
            ? null
            : AssemblePolygFromVerts(
            poly.VertexIds,
            ident => gfxObjRef.Vertices.ByIndex.TryGetValue((ushort)ident, out MeshVertex? vertex)
                ? new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z)
                : null);
    }

    private static StridePolygon? AssemblePolygFromVerts(
        IReadOnlyList<short> vertIdents, Func<short, Vector3?> locate)
    {
        Vector3[] verts = new Vector3[vertIdents.Count];
        for (int idx = 0; idx < vertIdents.Count; ++idx)
        {
            Vector3? v = locate(vertIdents[idx]);
            if (v is null) return null;
            verts[idx] = v.Value;
        }
        Vector3 norm = Vector3.Normalize(
            Vector3.Cross(verts[1] - verts[0], verts[2] - verts[0]));
        return new StridePolygon
        {
            Vertices = verts,
            Plane = new StridePlane(norm, -Vector3.Dot(norm, verts[0])),
        };
    }
}
