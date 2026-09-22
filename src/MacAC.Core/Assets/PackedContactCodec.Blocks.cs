using System.Collections.Immutable;
using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Assets;

/// <summary>The composite blocks (BSP trees, polygon tables) shared by several payload kinds.</summary>
public static partial class PackedContactCodec
{
    private sealed partial class Sink
    {
        public void KineticBsp(PackedKineticBsp bsp)
        {
            ArgumentNullException.ThrowIfNull(bsp);
            Idx32(bsp.TrunkOrdinal);
            Count(bsp.Joints.Length, "physics BSP node");
            Count(bsp.PolygOrdinalFlow.Length, "physics BSP polygon-index");

            for (int idx = 0; idx < bsp.Joints.Length; ++idx)
            {
                Beat(idx);
                var joint = bsp.Joints[idx];
                U32((uint)joint.Type);
                Plane(joint.SplittingPlane);
                Idx32(joint.PositiveDescendantOrdinal);
                Idx32(joint.NegativeDescendantOrdinal);
                Idx32(joint.LeafIndex);
                Idx32(joint.Solid);
                Sphere(joint.BoundingSphere);
                Range(joint.PolygonIndexRange);
            }
            for (int idx = 0; idx < bsp.PolygOrdinalFlow.Length; ++idx)
            {
                Beat(idx);
                Idx32(bsp.PolygOrdinalFlow[idx]);
            }
            Polygons(bsp.PolygChart);
        }

        public void ContainmentBsp(PackedCellContainmentBsp bsp)
        {
            ArgumentNullException.ThrowIfNull(bsp);
            Idx32(bsp.TrunkIdx);
            Count(bsp.Nodes.Length, "cell-containment BSP node");
            for (int idx = 0; idx < bsp.Nodes.Length; ++idx)
            {
                Beat(idx);
                var joint = bsp.Nodes[idx];
                U32((uint)joint.Type);
                Plane(joint.SplittingPlane);
                Idx32(joint.PositiveDescendantOrdinal);
                Idx32(joint.NegativeDescendantOrdinal);
                Idx32(joint.LeafIndex);
            }
        }

        public void Polygons(PackedPolygonTable chart)
        {
            ArgumentNullException.ThrowIfNull(chart);
            Count(chart.Polygons.Length, "polygon");
            Count(chart.Vertices.Length, "polygon vertex");
            for (int idx = 0; idx < chart.Polygons.Length; ++idx)
            {
                Beat(idx);
                var polyg = chart.Polygons[idx];
                U16(polyg.Id);
                U32((uint)polyg.SidesType);
                Idx32(polyg.NumPoints);
                Range(polyg.VertexRange);
                Plane(polyg.Plane);
            }
            for (int idx = 0; idx < chart.Vertices.Length; ++idx)
            {
                Beat(idx);
                Vec(chart.Vertices[idx]);
            }
        }
    }

    private ref partial struct Cursor
    {
        public PackedKineticBsp KineticBsp()
        {
            int trunk = I32();
            int jointTally = Count("physics BSP node", Width.KineticJoint);
            int ordinalTally = Count("physics BSP polygon-index", Width.PolygOrdinal);
            Earmark(checked(Width.KineticJoint * (long)jointTally + Width.PolygOrdinal * (long)ordinalTally + 8L), "physics BSP rows");

            var joints = Ranks(jointTally, static (ref Cursor cursor) => new PackedKineticBspNode(
                (BspTag)cursor.U32(), cursor.Plane(), cursor.I32(), cursor.I32(), cursor.I32(), cursor.I32(), cursor.Sphere(), cursor.Range()));
            var ordinals = Ranks(ordinalTally, static (ref Cursor cursor) => cursor.I32());
            return new PackedKineticBsp(trunk, joints, ordinals, Polygons());
        }

        public PackedCellContainmentBsp ContainmentBsp()
        {
            int trunk = I32();
            int jointTally = Count("cell-containment BSP node", Width.ContainmentJoint);
            var joints = Ranks(jointTally, static (ref Cursor cursor) => new PackedCellBspNode(
                (BspTag)cursor.U32(), cursor.Plane(), cursor.I32(), cursor.I32(), cursor.I32()));
            return new PackedCellContainmentBsp(trunk, joints);
        }

        public PackedPolygonTable Polygons()
        {
            int polygTally = Count("polygon", Width.Polygon);
            int vertTally = Count("polygon vertex", Width.Vertex);
            Earmark(checked(Width.Polygon * (long)polygTally + Width.Vertex * (long)vertTally), "polygon-table rows");

            // The wire order (sides, count, range, plane) differs from the
            // record's constructor order, so the row is read into locals first.
            var polygs = Ranks(polygTally, static (ref Cursor cursor) =>
            {
                ushort ident = cursor.U16();
                FaceCulling flanks = (FaceCulling)cursor.U32();
                int pts = cursor.I32();
                var span = cursor.Range();
                return new PackedContactPolygon(ident, cursor.Plane(), flanks, pts, span);
            });
            var verts = Ranks(vertTally, static (ref Cursor cursor) => cursor.Vec());
            return new PackedPolygonTable(polygs, verts);
        }
    }
}
