using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// Every file in the dat is a record with the id it was stored under.
public abstract class DatRecord : IDatRecord
{
    public uint Id { get; set; }
    // The client's data-category word; only textures carry one in the file.
    public uint Category { get; set; }
}

// One rigid piece of geometry: vertices, render facets, collision facets and the two BSP trees over them.
public class PartMesh : DatRecord, IDatRecord<PartMesh>
{
    public static RecordKind Kind => RecordKind.GfxObj;
    public static (uint First, uint Last) IdRange => (0x01000000, 0x0100FFFF);
    public PartMeshBits Bits { get; set; }
    public List<uint> SkinIds { get; set; } = [];
    public MeshVertices Vertices { get; set; } = null!;
    public Dictionary<ushort, Facet> CollisionFacets { get; set; } = [];
    public PhysicsBspTree CollisionTree { get; set; }
    public Vector3 SortCenter { get; set; }
    public Dictionary<ushort, Facet> Facets { get; set; } = [];
    public DrawingBspTree DrawTree { get; set; }
    public uint LodTableId { get; set; }

    public static PartMesh Read(ref DatCursor c)
    {
        uint id = c.U32();
        var bits = (PartMeshBits)c.U32();
        int n = (int)c.PackedU32();
        var skins = new List<uint>(n);
        for (int i = 0; i < n; i++) skins.Add(c.U32());
        var verts = MeshVertices.Read(ref c);
        Dictionary<ushort, Facet> coll = [];
        PhysicsBspTree collTree = null;
        if ((bits & PartMeshBits.HasPhysics) != 0)
        {
            coll = Facet.ReadTable(ref c, (int)c.PackedU32());
            collTree = PhysicsBspTree.Read(ref c);
        }
        var center = c.Vec3();
        Dictionary<ushort, Facet> facets = [];
        DrawingBspTree drawTree = null;
        if ((bits & PartMeshBits.HasDrawing) != 0)
        {
            facets = Facet.ReadTable(ref c, (int)c.PackedU32());
            drawTree = DrawingBspTree.Read(ref c);
        }
        uint lod = (bits & PartMeshBits.HasDIDDegrade) != 0 ? c.U32() : 0;
        return new PartMesh
        {
            Id = id, Bits = bits, SkinIds = skins, Vertices = verts, CollisionFacets = coll, CollisionTree = collTree,
            SortCenter = center, Facets = facets, DrawTree = drawTree, LodTableId = lod,
        };
    }
}

// Where a part attaches to a parent part.
public class AttachPoint
{
    public int PartIndex { get; set; }
    public Pose Pose { get; set; } = null!;
    public static AttachPoint Read(ref DatCursor c) => new() { PartIndex = c.I32(), Pose = Pose.Read(ref c) };
}

public class LampSpec
{
    public Pose ViewSpaceLocation { get; set; } = null!;
    public Argb Color { get; set; } = null!;
    public float Intensity { get; set; }
    public float Falloff { get; set; }
    public float ConeAngle { get; set; }
    public static LampSpec Read(ref DatCursor c) => new()
    {
        ViewSpaceLocation = Pose.Read(ref c), Color = Argb.Read(ref c), Intensity = c.F32(), Falloff = c.F32(), ConeAngle = c.F32(),
    };
}

// One animation frame: a pose per part plus the cues that fire on it.
public class MotionFrame
{
    public List<Pose> Poses { get; set; } = [];
    public List<Cue> Cues { get; set; } = [];

    public MotionFrame() { }
    public MotionFrame(uint partCount) { Poses = new List<Pose>((int)partCount); }

    public static MotionFrame Read(ref DatCursor c, int partCount)
    {
        var f = new MotionFrame { Poses = new List<Pose>(partCount) };
        for (int i = 0; i < partCount; i++) f.Poses.Add(Pose.Read(ref c));
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) { var cue = Cue.Read(ref c); if (cue is not null) f.Cues.Add(cue); }
        return f;
    }
}

// A model: the parts, how they hang together, their collision volumes and default behaviours.
public class RigSpec : DatRecord, IDatRecord<RigSpec>
{
    public static RecordKind Kind => RecordKind.Setup;
    public static (uint First, uint Last) IdRange => (0x02000000, 0x0200FFFF);
    public RigBits Bits { get; set; }
    public List<uint> PartIds { get; set; } = [];
    public List<uint> ParentIndex { get; set; } = [];
    public List<Vector3> DefaultScale { get; set; } = [];
    public Dictionary<AttachSlot, AttachPoint> HoldingSlots { get; set; } = [];
    public Dictionary<AttachSlot, AttachPoint> ConnectionSlots { get; set; } = [];
    public Dictionary<PlacementId, MotionFrame> Placements { get; set; } = [];
    public List<Capsule> Capsules { get; set; } = [];
    public List<Orb> Orbs { get; set; } = [];
    public float Height { get; set; }
    public float Radius { get; set; }
    public float StepUpHeight { get; set; }
    public float StepDownHeight { get; set; }
    public Orb SortingOrb { get; set; } = null!;
    public Orb SelectionOrb { get; set; } = null!;
    public Dictionary<int, LampSpec> Lamps { get; set; } = [];
    public uint DefaultClipId { get; set; }
    public uint DefaultEffectId { get; set; }
    public uint DefaultMotionBookId { get; set; }
    public uint DefaultSoundBookId { get; set; }
    public uint DefaultEffectBookId { get; set; }
    public int PartCount => PartIds.Count;

    public static RigSpec Read(ref DatCursor c)
    {
        uint id = c.U32();
        var bits = (RigBits)c.U32();
        int n = (int)c.U32();
        var parts = new List<uint>(n);
        for (int i = 0; i < n; i++) parts.Add(c.U32());
        var parents = new List<uint>();
        if ((bits & RigBits.HasParent) != 0) for (int i = 0; i < n; i++) parents.Add(c.U32());
        var scales = new List<Vector3>();
        if ((bits & RigBits.HasDefaultScale) != 0) for (int i = 0; i < n; i++) scales.Add(c.Vec3());
        var holding = ReadSlots(ref c);
        var connect = ReadSlots(ref c);
        int np = c.I32();
        var placements = new Dictionary<PlacementId, MotionFrame>(np);
        for (int i = 0; i < np; i++) { var key = (PlacementId)c.U32(); placements[key] = MotionFrame.Read(ref c, n); }
        int nc = (int)c.U32();
        var capsules = new List<Capsule>(nc);
        for (int i = 0; i < nc; i++) capsules.Add(Capsule.Read(ref c));
        int nb = (int)c.U32();
        var balls = new List<Orb>(nb);
        for (int i = 0; i < nb; i++) balls.Add(Orb.Read(ref c));
        float height = c.F32(), radius = c.F32(), up = c.F32(), down = c.F32();
        var sorting = Orb.Read(ref c);
        var selection = Orb.Read(ref c);
        int nl = c.I32();
        var lamps = new Dictionary<int, LampSpec>(nl);
        for (int i = 0; i < nl; i++) { int key = c.I32(); lamps[key] = LampSpec.Read(ref c); }
        return new RigSpec
        {
            Id = id, Bits = bits, PartIds = parts, ParentIndex = parents, DefaultScale = scales, HoldingSlots = holding, ConnectionSlots = connect,
            Placements = placements, Capsules = capsules, Orbs = balls, Height = height, Radius = radius, StepUpHeight = up, StepDownHeight = down,
            SortingOrb = sorting, SelectionOrb = selection, Lamps = lamps,
            DefaultClipId = c.U32(), DefaultEffectId = c.U32(), DefaultMotionBookId = c.U32(), DefaultSoundBookId = c.U32(), DefaultEffectBookId = c.U32(),
        };
    }

    static Dictionary<AttachSlot, AttachPoint> ReadSlots(ref DatCursor c)
    {
        int n = c.I32();
        var d = new Dictionary<AttachSlot, AttachPoint>(n);
        for (int i = 0; i < n; i++) { var key = (AttachSlot)c.I32(); d[key] = AttachPoint.Read(ref c); }
        return d;
    }
}

// Keyframed motion for a rig.
public class MotionClip : DatRecord, IDatRecord<MotionClip>
{
    public static RecordKind Kind => RecordKind.Animation;
    public static (uint First, uint Last) IdRange => (0x03000000, 0x0300FFFF);
    public ClipBits Bits { get; set; }
    public uint PartCount { get; set; }
    public List<Pose> RootPoses { get; set; } = [];
    public List<MotionFrame> Frames { get; set; } = [];
    public int FrameCount => Frames.Count;

    public static MotionClip Read(ref DatCursor c)
    {
        uint id = c.U32();
        var bits = (ClipBits)c.U32();
        uint parts = c.U32();
        int n = (int)c.U32();
        var root = new List<Pose>();
        if ((bits & ClipBits.PosFrames) != 0) for (int i = 0; i < n; i++) root.Add(Pose.Read(ref c));
        var frames = new List<MotionFrame>(n);
        for (int i = 0; i < n; i++) frames.Add(MotionFrame.Read(ref c, (int)parts));
        return new MotionClip { Id = id, Bits = bits, PartCount = parts, RootPoses = root, Frames = frames };
    }
}

public class ClipRef
{
    public uint ClipId { get; set; }
    public int LowFrame { get; set; }
    public int HighFrame { get; set; }
    public float Framerate { get; set; }
    public static ClipRef Read(ref DatCursor c) => new() { ClipId = c.U32(), LowFrame = c.I32(), HighFrame = c.I32(), Framerate = c.F32() };
}

// A motion: the clips it plays and the velocity it implies.
public class MotionEntry
{
    public byte Bitfield { get; set; }
    public MotionEntryBits Bits { get; set; }
    public List<ClipRef> Clips { get; set; } = [];
    public Vector3 Velocity { get; set; }
    public Vector3 Omega { get; set; }

    public static MotionEntry Read(ref DatCursor c)
    {
        int n = c.U8();
        byte bitfield = c.U8();
        var bits = (MotionEntryBits)c.U8();
        c.Align(4);
        var clips = new List<ClipRef>(n);
        for (int i = 0; i < n; i++) clips.Add(ClipRef.Read(ref c));
        var vel = (bits & MotionEntryBits.HasVelocity) != 0 ? c.Vec3() : default;
        var omega = (bits & MotionEntryBits.HasOmega) != 0 ? c.Vec3() : default;
        return new MotionEntry { Bitfield = bitfield, Bits = bits, Clips = clips, Velocity = vel, Omega = omega };
    }

    public static Dictionary<int, MotionEntry> ReadTable(ref DatCursor c)
    {
        int n = (int)c.U32();
        var d = new Dictionary<int, MotionEntry>(n);
        for (int i = 0; i < n; i++) { int key = c.I32(); d[key] = Read(ref c); }
        return d;
    }
}

public class MotionLinkSet
{
    public Dictionary<int, MotionEntry> Entries { get; set; } = [];
    public static MotionLinkSet Read(ref DatCursor c) => new() { Entries = MotionEntry.ReadTable(ref c) };
}

// All motions a rig can perform, by stance and command.
public class MotionBook : DatRecord, IDatRecord<MotionBook>
{
    public static RecordKind Kind => RecordKind.MotionTable;
    public static (uint First, uint Last) IdRange => (0x09000000, 0x0900FFFF);
    public MotionId DefaultStyle { get; set; }
    public Dictionary<MotionId, MotionId> StyleDefaults { get; set; } = [];
    public Dictionary<int, MotionEntry> Cycles { get; set; } = [];
    public Dictionary<int, MotionEntry> Modifiers { get; set; } = [];
    public Dictionary<int, MotionLinkSet> Links { get; set; } = [];

    public static MotionBook Read(ref DatCursor c)
    {
        uint id = c.U32();
        var style = (MotionId)c.U32();
        int n = (int)c.U32();
        var defaults = new Dictionary<MotionId, MotionId>(n);
        for (int i = 0; i < n; i++) { var k = (MotionId)c.U32(); defaults[k] = (MotionId)c.U32(); }
        var cycles = MotionEntry.ReadTable(ref c);
        var mods = MotionEntry.ReadTable(ref c);
        int nl = (int)c.U32();
        var links = new Dictionary<int, MotionLinkSet>(nl);
        for (int i = 0; i < nl; i++) { int k = c.I32(); links[k] = MotionLinkSet.Read(ref c); }
        return new MotionBook { Id = id, DefaultStyle = style, StyleDefaults = defaults, Cycles = cycles, Modifiers = mods, Links = links };
    }
}

public class LodLevel
{
    public uint PartMeshId { get; set; }
    public uint DegradeMode { get; set; }
    public float MinDist { get; set; }
    public float IdealDist { get; set; }
    public float MaxDist { get; set; }
    public static LodLevel Read(ref DatCursor c) => new() { PartMeshId = c.U32(), DegradeMode = c.U32(), MinDist = c.F32(), IdealDist = c.F32(), MaxDist = c.F32() };
}

public class LodTable : DatRecord, IDatRecord<LodTable>
{
    public static RecordKind Kind => RecordKind.GfxObjDegradeInfo;
    public static (uint First, uint Last) IdRange => (0x11000000, 0x1100FFFF);
    public List<LodLevel> Levels { get; set; } = [];
    public static LodTable Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var t = new LodTable { Id = id, Levels = new List<LodLevel>(n) };
        for (int i = 0; i < n; i++) t.Levels.Add(LodLevel.Read(ref c));
        return t;
    }
}

public class ColorSwap
{
    public uint ColorTableId { get; set; }
    public byte Offset { get; set; }
    public byte Count { get; set; }
    public static ColorSwap Read(ref DatCursor c) => new() { ColorTableId = c.PackedId(DatIds.ColorTable), Offset = c.U8(), Count = c.U8() };
}

public class TextureOverride
{
    public byte PartIndex { get; set; }
    public uint OldTextureId { get; set; }
    public uint NewTextureId { get; set; }
    public static TextureOverride Read(ref DatCursor c) => new() { PartIndex = c.U8(), OldTextureId = c.PackedId(DatIds.SkinTexture), NewTextureId = c.PackedId(DatIds.SkinTexture) };
}

public class PartOverride
{
    public byte PartIndex { get; set; }
    public uint PartMeshId { get; set; }
    public static PartOverride Read(ref DatCursor c) => new() { PartIndex = c.U8(), PartMeshId = c.PackedId(DatIds.PartMesh) };
}

// How an object looks on top of its rig: palette, texture and part overrides.
public class LookDesc
{
    public byte Version { get; set; }
    public uint ColorTableId { get; set; }
    public List<ColorSwap> ColorSwaps { get; set; } = [];
    public List<TextureOverride> TextureSwaps { get; set; } = [];
    public List<PartOverride> PartSwaps { get; set; } = [];

    public static LookDesc Read(ref DatCursor c)
    {
        c.Align(4);
        byte version = c.U8();
        int np = c.U8(), nt = c.U8(), na = c.U8();
        uint pal = np > 0 ? c.PackedId(DatIds.ColorTable) : 0;
        var a = new LookDesc { Version = version, ColorTableId = pal };
        for (int i = 0; i < np; i++) a.ColorSwaps.Add(ColorSwap.Read(ref c));
        for (int i = 0; i < nt; i++) a.TextureSwaps.Add(TextureOverride.Read(ref c));
        for (int i = 0; i < na; i++) a.PartSwaps.Add(PartOverride.Read(ref c));
        c.Align(4);
        return a;
    }
}

public class WardrobeTextureSwap
{
    public uint OldTextureId { get; set; }
    public uint NewTextureId { get; set; }
    public static WardrobeTextureSwap Read(ref DatCursor c) => new() { OldTextureId = c.U32(), NewTextureId = c.U32() };
}

public class WardrobePartEffect
{
    public uint Index { get; set; }
    public uint PartMeshId { get; set; }
    public List<WardrobeTextureSwap> TextureSwaps { get; set; } = [];
    public static WardrobePartEffect Read(ref DatCursor c)
    {
        var e = new WardrobePartEffect { Index = c.U32(), PartMeshId = c.U32() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) e.TextureSwaps.Add(WardrobeTextureSwap.Read(ref c));
        return e;
    }
}

public class WardrobeBaseEffect
{
    public List<WardrobePartEffect> Parts { get; set; } = [];
    public static WardrobeBaseEffect Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var e = new WardrobeBaseEffect { Parts = new List<WardrobePartEffect>(n) };
        for (int i = 0; i < n; i++) e.Parts.Add(WardrobePartEffect.Read(ref c));
        return e;
    }
}

public class WardrobeColorRange
{
    public uint Offset { get; set; }
    public uint Count { get; set; }
    public static WardrobeColorRange Read(ref DatCursor c) => new() { Offset = c.U32(), Count = c.U32() };
}

public class WardrobeColorSwap
{
    public List<WardrobeColorRange> Ranges { get; set; } = [];
    public uint ColorTableSetId { get; set; }
    public static WardrobeColorSwap Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var ranges = new List<WardrobeColorRange>(n);
        for (int i = 0; i < n; i++) ranges.Add(WardrobeColorRange.Read(ref c));
        return new WardrobeColorSwap { Ranges = ranges, ColorTableSetId = c.U32() };
    }
}

public class WardrobeColorEffect
{
    public uint IconId { get; set; }
    public List<WardrobeColorSwap> Swaps { get; set; } = [];
    public static WardrobeColorEffect Read(ref DatCursor c)
    {
        var e = new WardrobeColorEffect { IconId = c.U32() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) e.Swaps.Add(WardrobeColorSwap.Read(ref c));
        return e;
    }
}

// Clothing: per-rig part and texture overrides, and the palette choices.
public class WardrobeTable : DatRecord, IDatRecord<WardrobeTable>
{
    public static RecordKind Kind => RecordKind.ClothingTable;
    public static (uint First, uint Last) IdRange => (0x10000000, 0x1000FFFF);
    public Dictionary<uint, WardrobeBaseEffect> BaseEffects { get; set; } = [];
    public Dictionary<uint, WardrobeColorEffect> ColorEffects { get; set; } = [];

    public static WardrobeTable Read(ref DatCursor c)
    {
        uint id = c.U32();
        var bases = Tables.ReadPacked(ref c, static (ref DatCursor c) => c.U32(), WardrobeBaseEffect.Read);
        var colors = Tables.ReadPacked(ref c, static (ref DatCursor c) => c.U32(), WardrobeColorEffect.Read);
        return new WardrobeTable { Id = id, BaseEffects = bases, ColorEffects = colors };
    }
}

// A particle system description.
public class EmitterDesc : DatRecord, IDatRecord<EmitterDesc>
{
    public static RecordKind Kind => RecordKind.ParticleEmitter;
    public static (uint First, uint Last) IdRange => (0x32000000, 0x3200FFFF);
    public uint Version { get; set; }
    public EmitterShape Shape { get; set; }
    public ParticleMotion Motion { get; set; }
    public uint PartMeshId { get; set; }
    public uint HwPartMeshId { get; set; }
    public double Birthrate { get; set; }
    public int MaxParticles { get; set; }
    public int InitialParticles { get; set; }
    public int TotalParticles { get; set; }
    public double TotalSeconds { get; set; }
    public double Lifespan { get; set; }
    public double LifespanRand { get; set; }
    public Vector3 OffsetDir { get; set; }
    public float MinOffset { get; set; }
    public float MaxOffset { get; set; }
    public Vector3 A { get; set; }
    public float MinA { get; set; }
    public float MaxA { get; set; }
    public Vector3 B { get; set; }
    public float MinB { get; set; }
    public float MaxB { get; set; }
    public Vector3 C { get; set; }
    public float MinC { get; set; }
    public float MaxC { get; set; }
    public float StartScale { get; set; }
    public float FinalScale { get; set; }
    public float ScaleRand { get; set; }
    public float StartTrans { get; set; }
    public float FinalTrans { get; set; }
    public float TransRand { get; set; }
    public bool IsParentLocal { get; set; }

    public static EmitterDesc Read(ref DatCursor c) => new()
    {
        Id = c.U32(), Version = c.U32(), Shape = (EmitterShape)c.I32(), Motion = (ParticleMotion)c.I32(), PartMeshId = c.U32(), HwPartMeshId = c.U32(),
        Birthrate = c.F64(), MaxParticles = c.I32(), InitialParticles = c.I32(), TotalParticles = c.I32(), TotalSeconds = c.F64(), Lifespan = c.F64(), LifespanRand = c.F64(),
        OffsetDir = c.Vec3(), MinOffset = c.F32(), MaxOffset = c.F32(), A = c.Vec3(), MinA = c.F32(), MaxA = c.F32(), B = c.Vec3(), MinB = c.F32(), MaxB = c.F32(),
        C = c.Vec3(), MinC = c.F32(), MaxC = c.F32(), StartScale = c.F32(), FinalScale = c.F32(), ScaleRand = c.F32(), StartTrans = c.F32(), FinalTrans = c.F32(), TransRand = c.F32(),
        IsParentLocal = c.Flag32(),
    };
}

public class EffectStep
{
    public double StartTime { get; set; }
    public Cue Cue { get; set; }
    public static EffectStep Read(ref DatCursor c) => new() { StartTime = c.F64(), Cue = Cue.Read(ref c) };
}

// A timed list of cues (a "physics script").
public class EffectScript : DatRecord, IDatRecord<EffectScript>
{
    public static RecordKind Kind => RecordKind.PhysicsScript;
    public static (uint First, uint Last) IdRange => (0x33000000, 0x3300FFFF);
    public List<EffectStep> Steps { get; set; } = [];
    public static EffectScript Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var s = new EffectScript { Id = id, Steps = new List<EffectStep>(n) };
        for (int i = 0; i < n; i++) s.Steps.Add(EffectStep.Read(ref c));
        return s;
    }
}

public class EffectChoice
{
    public float Mod { get; set; }
    public uint ScriptId { get; set; }
    public static EffectChoice Read(ref DatCursor c) => new() { Mod = c.F32(), ScriptId = c.U32() };
}

public class EffectChoices
{
    public List<EffectChoice> Choices { get; set; } = [];
    public static EffectChoices Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var e = new EffectChoices { Choices = new List<EffectChoice>(n) };
        for (int i = 0; i < n; i++) e.Choices.Add(EffectChoice.Read(ref c));
        return e;
    }
}

// Maps an effect id to the scripts that can play for it.
public class EffectBook : DatRecord, IDatRecord<EffectBook>
{
    public static RecordKind Kind => RecordKind.PhysicsScriptTable;
    public static (uint First, uint Last) IdRange => (0x34000000, 0x3400FFFF);
    public Dictionary<EffectId, EffectChoices> Entries { get; set; } = [];
    public static EffectBook Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var b = new EffectBook { Id = id, Entries = new Dictionary<EffectId, EffectChoices>(n) };
        for (int i = 0; i < n; i++) { var k = (EffectId)c.U32(); b.Entries[k] = EffectChoices.Read(ref c); }
        return b;
    }
}

// Readers for the game's hash-table encodings. Keys and values are read in file order.
public static class Tables
{
    public delegate T Reader<T>(ref DatCursor c);

    // ushort count, ushort bucket size, then entries
    public static Dictionary<TKey, TValue> ReadPacked<TKey, TValue>(ref DatCursor c, Reader<TKey> key, Reader<TValue> value) where TKey : notnull
    {
        int n = c.U16();
        c.U16();
        var d = new Dictionary<TKey, TValue>(n);
        for (int i = 0; i < n; i++) { var k = key(ref c); d[k] = value(ref c); }
        return d;
    }

    // byte bucket-size index, packed count, then entries
    public static Dictionary<TKey, TValue> ReadHashed<TKey, TValue>(ref DatCursor c, Reader<TKey> key, Reader<TValue> value) where TKey : notnull
    {
        c.U8();
        int n = (int)c.PackedU32();
        var d = new Dictionary<TKey, TValue>(n);
        for (int i = 0; i < n; i++) { var k = key(ref c); d[k] = value(ref c); }
        return d;
    }

    public static uint U32(ref DatCursor c) => c.U32();
    public static int I32(ref DatCursor c) => c.I32();
    public static string PStr(ref DatCursor c) => c.PStr();
    public static string Str(ref DatCursor c) => c.Str();
    public static string WideStr(ref DatCursor c) => c.WideStr();

    public static List<T> ReadList<T>(ref DatCursor c, int n, Reader<T> item)
    {
        var l = new List<T>(n);
        for (int i = 0; i < n; i++) l.Add(item(ref c));
        return l;
    }
}
