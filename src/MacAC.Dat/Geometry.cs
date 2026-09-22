using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// A placement: origin plus orientation. The file stores the quaternion as w, x, y, z.
public class Pose
{
    public Vector3 Origin { get; set; }
    public Quaternion Orientation { get; set; } = Quaternion.Identity;
    public static Pose Read(ref DatCursor c) => new() { Origin = c.Vec3(), Orientation = c.Quat() };
}

public class Orb
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public static Orb Read(ref DatCursor c) => new() { Center = c.Vec3(), Radius = c.F32() };
}

public class Capsule
{
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public float Height { get; set; }
    public static Capsule Read(ref DatCursor c) => new() { Center = c.Vec3(), Radius = c.F32(), Height = c.F32() };
}

public class TexCoord
{
    public float U { get; set; }
    public float V { get; set; }
    public static TexCoord Read(ref DatCursor c) => new() { U = c.F32(), V = c.F32() };
}

// Stored as one ARGB dword: blue is the low byte.
public class Argb
{
    public byte Blue { get; set; }
    public byte Green { get; set; }
    public byte Red { get; set; }
    public byte Alpha { get; set; }
    public static Argb Read(ref DatCursor c) { uint v = c.U32(); return From(v); }
    public static Argb From(uint v) => new() { Blue = (byte)v, Green = (byte)(v >> 8), Red = (byte)(v >> 16), Alpha = (byte)(v >> 24) };
    public uint Packed => (uint)(Alpha << 24 | Red << 16 | Green << 8 | Blue);
}

public class MeshVertex
{
    public Vector3 Position { get; set; }
    public Vector3 Normal { get; set; }
    public List<TexCoord> TexCoords { get; set; } = [];
    public static MeshVertex Read(ref DatCursor c)
    {
        int n = c.U16();
        var v = new MeshVertex { Position = c.Vec3(), Normal = c.Vec3(), TexCoords = new List<TexCoord>(n) };
        for (int i = 0; i < n; i++) v.TexCoords.Add(TexCoord.Read(ref c));
        return v;
    }
}

public enum MeshVertexKind : uint { Unknown = 0, Standard = 1 }

public class MeshVertices
{
    public MeshVertexKind Kind { get; set; }
    public Dictionary<ushort, MeshVertex> ByIndex { get; set; } = [];
    public static MeshVertices Read(ref DatCursor c)
    {
        var kind = (MeshVertexKind)c.U32();
        int n = (int)c.U32();
        var d = new Dictionary<ushort, MeshVertex>(n);
        for (int i = 0; i < n; i++) { ushort idx = c.U16(); d[idx] = MeshVertex.Read(ref c); }
        return new MeshVertices { Kind = kind, ByIndex = d };
    }
}

[Flags]
public enum StippleBits : byte { None = 0, Positive = 1, Negative = 2, Both = 3, NoPos = 4, NoNeg = 8, NoUvs = 20 }

public enum FaceCulling : int { Landblock = 0, None = 1, Clockwise = 2, CounterClockwise = 3 }

public class Facet
{
    public StippleBits Stippling { get; set; }
    public FaceCulling Culling { get; set; }
    public short FrontSurface { get; set; }
    public short BackSurface { get; set; }
    public List<short> VertexIds { get; set; } = [];
    public List<byte> FrontUvIndices { get; set; } = [];
    public List<byte> BackUvIndices { get; set; } = [];

    public static Facet Read(ref DatCursor c)
    {
        int points = c.U8();
        var stipple = (StippleBits)c.U8();
        var culling = (FaceCulling)c.I32();
        short front = c.I16(), back = c.I16();
        var f = new Facet { Stippling = stipple, Culling = culling, FrontSurface = front, BackSurface = back, VertexIds = new List<short>(points) };
        for (int i = 0; i < points; i++) f.VertexIds.Add(c.I16());
        if ((stipple & StippleBits.NoPos) == 0) for (int i = 0; i < points; i++) f.FrontUvIndices.Add(c.U8());
        if (culling == FaceCulling.Clockwise && (stipple & StippleBits.NoNeg) == 0) for (int i = 0; i < points; i++) f.BackUvIndices.Add(c.U8());
        return f;
    }

    // keyed by polygon id in the file
    public static Dictionary<ushort, Facet> ReadTable(ref DatCursor c, int n)
    {
        var d = new Dictionary<ushort, Facet>(n);
        for (int i = 0; i < n; i++) { ushort id = c.U16(); d[id] = Read(ref c); }
        return d;
    }
}

// Node tags are four ASCII bytes, stored reversed, read as one little-endian dword.
public enum BspTag : uint
{
    BPFL = 0x4250464C, BPIN = 0x4250494E, BPIn = 0x4250496E, BPOL = 0x42504F4C, BPnN = 0x42506E4E, BPnn = 0x42506E6E,
    BpIN = 0x4270494E, BpnN = 0x42706E4E, Leaf = 0x4C454146, Portal = 0x504F5254,
}

public enum BspFlavor { Cell, Physics, Drawing }

// A polygon that doubles as a doorway out of a drawing-BSP node.
public class DoorwayPoly
{
    public ushort PolygonId { get; set; }
    public ushort PortalIndex { get; set; }
    public static DoorwayPoly Read(ref DatCursor c) => new() { PolygonId = c.U16(), PortalIndex = c.U16() };
}

public abstract class BspNode
{
    public BspTag Tag { get; set; }
    public Plane Splitter { get; set; }
}

public class PhysicsBspNode : BspNode
{
    public PhysicsBspNode Front { get; set; }
    public PhysicsBspNode Back { get; set; }
    public int LeafIndex { get; set; }
    public int Solid { get; set; }
    public Orb Bounds { get; set; } = new();
    public List<ushort> Polygons { get; set; } = [];
}

public class DrawingBspNode : BspNode
{
    public DrawingBspNode Front { get; set; }
    public DrawingBspNode Back { get; set; }
    public int LeafIndex { get; set; }
    public Orb Bounds { get; set; } = new();
    public List<ushort> Polygons { get; set; } = [];
    public List<DoorwayPoly> Portals { get; set; } = [];
}

public class CellBspNode : BspNode
{
    public CellBspNode Front { get; set; }
    public CellBspNode Back { get; set; }
    public int LeafIndex { get; set; }
}

public static class Bsp
{
    static List<ushort> Ids(ref DatCursor c) { int n = (int)c.U32(); var l = new List<ushort>(n); for (int i = 0; i < n; i++) l.Add(c.U16()); return l; }

    public static PhysicsBspNode ReadPhysics(ref DatCursor c)
    {
        var tag = (BspTag)c.U32();
        if (tag == BspTag.Leaf)
        {
            var leaf = new PhysicsBspNode { Tag = tag, LeafIndex = c.I32() };
            leaf.Solid = c.I32(); leaf.Bounds = Orb.Read(ref c); leaf.Polygons = Ids(ref c);
            return leaf;
        }
        var n = new PhysicsBspNode { Tag = tag, Splitter = c.Plane() };
        if (tag == BspTag.Portal) { n.Front = ReadPhysics(ref c); n.Back = ReadPhysics(ref c); }
        else
        {
            if (tag is BspTag.BPnn or BspTag.BPIn or BspTag.BPIN or BspTag.BPnN) n.Front = ReadPhysics(ref c);
            if (tag is BspTag.BpIN or BspTag.BpnN or BspTag.BPIN or BspTag.BPnN) n.Back = ReadPhysics(ref c);
            n.Bounds = Orb.Read(ref c);
        }
        return n;
    }

    public static DrawingBspNode ReadDrawing(ref DatCursor c)
    {
        var tag = (BspTag)c.U32();
        if (tag == BspTag.Leaf) return new DrawingBspNode { Tag = tag, LeafIndex = c.I32() };
        var n = new DrawingBspNode { Tag = tag, Splitter = c.Plane() };
        if (tag == BspTag.Portal)
        {
            n.Front = ReadDrawing(ref c); n.Back = ReadDrawing(ref c);
            n.Bounds = Orb.Read(ref c);
            int npoly = (int)c.U32(), nportal = (int)c.U32();
            for (int i = 0; i < npoly; i++) n.Polygons.Add(c.U16());
            for (int i = 0; i < nportal; i++) n.Portals.Add(DoorwayPoly.Read(ref c));
            return n;
        }
        if (tag is BspTag.BPnn or BspTag.BPIn or BspTag.BPIN or BspTag.BPnN) n.Front = ReadDrawing(ref c);
        if (tag is BspTag.BpIN or BspTag.BpnN or BspTag.BPIN or BspTag.BPnN) n.Back = ReadDrawing(ref c);
        n.Bounds = Orb.Read(ref c); n.Polygons = Ids(ref c);
        return n;
    }

    public static CellBspNode ReadCell(ref DatCursor c)
    {
        var tag = (BspTag)c.U32();
        if (tag == BspTag.Leaf) return new CellBspNode { Tag = tag, LeafIndex = c.I32() };
        var n = new CellBspNode { Tag = tag, Splitter = c.Plane() };
        if (tag == BspTag.Portal) { n.Front = ReadCell(ref c); n.Back = ReadCell(ref c); return n; }
        if (tag is BspTag.BPnn or BspTag.BPIn or BspTag.BPIN or BspTag.BPnN) n.Front = ReadCell(ref c);
        if (tag is BspTag.BpIN or BspTag.BpnN or BspTag.BPIN or BspTag.BPnN) n.Back = ReadCell(ref c);
        return n;
    }
}

public class PhysicsBspTree { public PhysicsBspNode Root { get; set; } public static PhysicsBspTree Read(ref DatCursor c) => new() { Root = Bsp.ReadPhysics(ref c) }; }
public class DrawingBspTree { public DrawingBspNode Root { get; set; } public static DrawingBspTree Read(ref DatCursor c) => new() { Root = Bsp.ReadDrawing(ref c) }; }
public class CellBspTree { public CellBspNode Root { get; set; } public static CellBspTree Read(ref DatCursor c) => new() { Root = Bsp.ReadCell(ref c) }; }
