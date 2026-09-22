using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// An object placed in the world: which model, where.
public class PlacedObject
{
    public uint Id { get; set; }
    public Pose Pose { get; set; } = null!;
    public static PlacedObject Read(ref DatCursor c) => new() { Id = c.U32(), Pose = Pose.Read(ref c) };
}

// One cell shape of an interior: geometry plus the BSP trees that partition it.
public class ShellCell
{
    public MeshVertices Vertices { get; set; } = null!;
    public Dictionary<ushort, Facet> Facets { get; set; } = [];
    public List<ushort> Portals { get; set; } = [];
    public CellBspTree CellTree { get; set; } = null!;
    public Dictionary<ushort, Facet> CollisionFacets { get; set; } = [];
    public PhysicsBspTree CollisionTree { get; set; } = null!;
    public DrawingBspTree DrawTree { get; set; }

    public static ShellCell Read(ref DatCursor c)
    {
        int nFacets = (int)c.U32(), nColl = (int)c.U32(), nPortals = (int)c.U32();
        var verts = MeshVertices.Read(ref c);
        var facets = Facet.ReadTable(ref c, nFacets);
        var portals = new List<ushort>(nPortals);
        for (int i = 0; i < nPortals; i++) portals.Add(c.U16());
        c.Align(4);
        var cellTree = CellBspTree.Read(ref c);
        var coll = Facet.ReadTable(ref c, nColl);
        var collTree = PhysicsBspTree.Read(ref c);
        DrawingBspTree draw = c.Flag32() ? DrawingBspTree.Read(ref c) : null;
        c.Align(4);
        return new ShellCell { Vertices = verts, Facets = facets, Portals = portals, CellTree = cellTree, CollisionFacets = coll, CollisionTree = collTree, DrawTree = draw };
    }
}

// The cell shapes an interior is built from (an "environment").
public class InteriorShell : DatRecord, IDatRecord<InteriorShell>
{
    public static RecordKind Kind => RecordKind.Environment;
    public static (uint First, uint Last) IdRange => (0x0D000000, 0x0D00FFFF);
    public Dictionary<uint, ShellCell> Cells { get; set; } = [];
    public static InteriorShell Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var s = new InteriorShell { Id = id, Cells = new Dictionary<uint, ShellCell>(n) };
        for (int i = 0; i < n; i++) { uint k = c.U32(); s.Cells[k] = ShellCell.Read(ref c); }
        return s;
    }
}

public class CellDoorway
{
    public DoorwayBits Bits { get; set; }
    public ushort PolygonId { get; set; }
    public ushort OtherCellId { get; set; }
    public ushort OtherPortalId { get; set; }
    public static CellDoorway Read(ref DatCursor c) => new() { Bits = (DoorwayBits)c.U16(), PolygonId = c.U16(), OtherCellId = c.U16(), OtherPortalId = c.U16() };
}

// One room of an interior: which shell cell it is, where, and what it can see.
public class RoomCell : DatRecord, IDatRecord<RoomCell>
{
    public static RecordKind Kind => RecordKind.EnvCell;
    public static DatShelf Shelf => DatShelf.Cell;
    public RoomCellBits Bits { get; set; }
    public List<ushort> SkinIds { get; set; } = [];
    public ushort ShellId { get; set; }
    public ushort ShellCellIndex { get; set; }
    public Pose Position { get; set; } = null!;
    public List<CellDoorway> Doorways { get; set; } = [];
    public List<ushort> VisibleCells { get; set; } = [];
    public List<PlacedObject> StaticObjects { get; set; } = [];
    public uint RestrictionObjectId { get; set; }

    public static RoomCell Read(ref DatCursor c)
    {
        uint id = c.U32();
        var bits = (RoomCellBits)c.I32();
        c.U32();
        int nSkins = c.U8(), nDoors = c.U8(), nVis = c.U16();
        var skins = new List<ushort>(nSkins);
        for (int i = 0; i < nSkins; i++) skins.Add(c.U16());
        ushort shell = c.U16(), cell = c.U16();
        var pos = Pose.Read(ref c);
        var doors = new List<CellDoorway>(nDoors);
        for (int i = 0; i < nDoors; i++) doors.Add(CellDoorway.Read(ref c));
        var vis = new List<ushort>(nVis);
        for (int i = 0; i < nVis; i++) vis.Add(c.U16());
        var statics = new List<PlacedObject>();
        if ((bits & RoomCellBits.HasStaticObjs) != 0)
        {
            int n = (int)c.U32();
            for (int i = 0; i < n; i++) statics.Add(PlacedObject.Read(ref c));
        }
        uint restriction = (bits & RoomCellBits.HasRestrictionObj) != 0 ? c.U32() : 0;
        return new RoomCell
        {
            Id = id, Bits = bits, SkinIds = skins, ShellId = shell, ShellCellIndex = cell, Position = pos, Doorways = doors,
            VisibleCells = vis, StaticObjects = statics, RestrictionObjectId = restriction,
        };
    }
}

// Packed terrain sample: road bits, terrain kind and scenery index in one word.
public readonly struct TerrainSample(ushort raw)
{
    public ushort Raw { get; } = raw;
    public byte Road => (byte)(Raw & 3);
    public TerrainKind Kind => (TerrainKind)((Raw & 0x7C) >> 2);
    public byte Scenery => (byte)((Raw & 0xF800) >> 11);
    public static implicit operator ushort(TerrainSample s) => s.Raw;
    public static implicit operator TerrainSample(ushort raw) => new(raw);
}

// A 9x9 grid of heights and terrain kinds: one outdoor landblock.
public class TerrainTile : DatRecord, IDatRecord<TerrainTile>
{
    public static RecordKind Kind => RecordKind.LandBlock;
    public static DatShelf Shelf => DatShelf.Cell;
    public const int Side = 9;
    public bool HasObjects { get; set; }
    public TerrainSample[] Samples { get; set; } = new TerrainSample[Side * Side];
    public byte[] Heights { get; set; } = new byte[Side * Side];

    public static TerrainTile Read(ref DatCursor c)
    {
        var t = new TerrainTile { Id = c.U32(), HasObjects = c.Flag32() };
        for (int i = 0; i < Side * Side; i++) t.Samples[i] = new TerrainSample(c.U16());
        for (int i = 0; i < Side * Side; i++) t.Heights[i] = c.U8();
        c.Align(4);
        return t;
    }
}

public class BuildingDoorway
{
    public DoorwayBits Bits { get; set; }
    public ushort OtherCellId { get; set; }
    public ushort OtherPortalId { get; set; }
    public List<ushort> StabIds { get; set; } = [];
    public static BuildingDoorway Read(ref DatCursor c)
    {
        var d = new BuildingDoorway { Bits = (DoorwayBits)c.U16(), OtherCellId = c.U16(), OtherPortalId = c.U16() };
        int n = c.U16();
        for (int i = 0; i < n; i++) d.StabIds.Add(c.U16());
        c.Align(4);
        return d;
    }
}

public class BuildingSpec
{
    public uint ModelId { get; set; }
    public Pose Pose { get; set; } = null!;
    public uint LeafCount { get; set; }
    public List<BuildingDoorway> Doorways { get; set; } = [];
    public static BuildingSpec Read(ref DatCursor c)
    {
        var s = new BuildingSpec { ModelId = c.U32(), Pose = Pose.Read(ref c), LeafCount = c.U32() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.Doorways.Add(BuildingDoorway.Read(ref c));
        return s;
    }
}

// What stands on a landblock: static objects, buildings and their restrictions.
public class TerrainTileExtras : DatRecord, IDatRecord<TerrainTileExtras>
{
    public static RecordKind Kind => RecordKind.LandBlockInfo;
    public static DatShelf Shelf => DatShelf.Cell;
    public uint CellCount { get; set; }
    public List<PlacedObject> Objects { get; set; } = [];
    public List<BuildingSpec> Structures { get; set; } = [];
    public Dictionary<uint, uint> Restrictions { get; set; } = [];

    public static TerrainTileExtras Read(ref DatCursor c)
    {
        uint id = c.U32();
        uint cells = c.U32();
        int n = (int)c.U32();
        var objs = new List<PlacedObject>(n);
        for (int i = 0; i < n; i++) objs.Add(PlacedObject.Read(ref c));
        int nb = c.U16();
        bool hasRestrictions = c.U16() != 0;
        var buildings = new List<BuildingSpec>(nb);
        for (int i = 0; i < nb; i++) buildings.Add(BuildingSpec.Read(ref c));
        var restrictions = new Dictionary<uint, uint>();
        if (hasRestrictions)
        {
            int nr = c.U16();
            c.U16();
            for (int i = 0; i < nr; i++) { uint k = c.U32(); restrictions[k] = c.U32(); }
        }
        return new TerrainTileExtras { Id = id, CellCount = cells, Objects = objs, Structures = buildings, Restrictions = restrictions };
    }
}

public class SceneryItem
{
    public uint ObjectId { get; set; }
    public Pose BaseLoc { get; set; } = null!;
    public float Frequency { get; set; }
    public float DisplaceX { get; set; }
    public float DisplaceY { get; set; }
    public float MinScale { get; set; }
    public float MaxScale { get; set; }
    public float MaxRotation { get; set; }
    public float MinSlope { get; set; }
    public float MaxSlope { get; set; }
    public int Align { get; set; }
    public int Orient { get; set; }
    public uint WeenieObj { get; set; }
    public static SceneryItem Read(ref DatCursor c) => new()
    {
        ObjectId = c.U32(), BaseLoc = Pose.Read(ref c), Frequency = c.F32(), DisplaceX = c.F32(), DisplaceY = c.F32(), MinScale = c.F32(), MaxScale = c.F32(),
        MaxRotation = c.F32(), MinSlope = c.F32(), MaxSlope = c.F32(), Align = c.I32(), Orient = c.I32(), WeenieObj = c.U32(),
    };
}

public class SceneryList : DatRecord, IDatRecord<SceneryList>
{
    public static RecordKind Kind => RecordKind.Scene;
    public static (uint First, uint Last) IdRange => (0x12000000, 0x1200FFFF);
    public List<SceneryItem> Items { get; set; } = [];
    public static SceneryList Read(ref DatCursor c)
    {
        uint id = c.U32();
        int n = (int)c.U32();
        var s = new SceneryList { Id = id, Items = new List<SceneryItem>(n) };
        for (int i = 0; i < n; i++) s.Items.Add(SceneryItem.Read(ref c));
        return s;
    }
}

// ---- Region ----

public class LandLayout
{
    public int BlockLength { get; set; }
    public int BlockWidth { get; set; }
    public float SquareLength { get; set; }
    public int LandblockLength { get; set; }
    public int VertexPerCell { get; set; }
    public float MaxObjHeight { get; set; }
    public float SkyHeight { get; set; }
    public float RoadWidth { get; set; }
    public float[] HeightTable { get; set; } = new float[256];
    public static LandLayout Read(ref DatCursor c)
    {
        var d = new LandLayout
        {
            BlockLength = c.I32(), BlockWidth = c.I32(), SquareLength = c.F32(), LandblockLength = c.I32(), VertexPerCell = c.I32(),
            MaxObjHeight = c.F32(), SkyHeight = c.F32(), RoadWidth = c.F32(),
        };
        for (int i = 0; i < 256; i++) d.HeightTable[i] = c.F32();
        return d;
    }
}

public class TimeOfDay
{
    public float Start { get; set; }
    public bool IsNight { get; set; }
    public string Name { get; set; } = "";
    public static TimeOfDay Read(ref DatCursor c) { var t = new TimeOfDay { Start = c.F32(), IsNight = c.Flag32(), Name = c.Str() }; c.Align(4); return t; }
}

public class Season
{
    public uint Start { get; set; }
    public string Name { get; set; } = "";
    public static Season Read(ref DatCursor c) { var s = new Season { Start = c.U32(), Name = c.Str() }; c.Align(4); return s; }
}

public class GameTime
{
    public double ZeroTimeOfYear { get; set; }
    public uint ZeroYear { get; set; }
    public float DayLength { get; set; }
    public uint DaysPerYear { get; set; }
    public string YearSpec { get; set; } = "";
    public List<TimeOfDay> TimesOfDay { get; set; } = [];
    public List<string> DaysOfWeek { get; set; } = [];
    public List<Season> Seasons { get; set; } = [];
    public static GameTime Read(ref DatCursor c)
    {
        var g = new GameTime { ZeroTimeOfYear = c.F64(), ZeroYear = c.U32(), DayLength = c.F32(), DaysPerYear = c.U32(), YearSpec = c.Str() };
        c.Align(4);
        int n = (int)c.U32(); for (int i = 0; i < n; i++) g.TimesOfDay.Add(TimeOfDay.Read(ref c));
        n = (int)c.U32(); for (int i = 0; i < n; i++) g.DaysOfWeek.Add(c.Str());
        n = (int)c.U32(); for (int i = 0; i < n; i++) g.Seasons.Add(Season.Read(ref c));
        return g;
    }
}

public class SkyObject
{
    public float BeginTime { get; set; }
    public float EndTime { get; set; }
    public float BeginAngle { get; set; }
    public float EndAngle { get; set; }
    public float TexVelocityX { get; set; }
    public float TexVelocityY { get; set; }
    public uint PartMeshId { get; set; }
    public uint EffectScriptId { get; set; }
    public uint Properties { get; set; }
    public static SkyObject Read(ref DatCursor c) => new()
    {
        BeginTime = c.F32(), EndTime = c.F32(), BeginAngle = c.F32(), EndAngle = c.F32(), TexVelocityX = c.F32(), TexVelocityY = c.F32(),
        PartMeshId = c.U32(), EffectScriptId = c.U32(), Properties = c.U32(),
    };
}

public class SkyObjectReplace
{
    public uint ObjectIndex { get; set; }
    public uint PartMeshId { get; set; }
    public float Rotate { get; set; }
    public float Transparent { get; set; }
    public float Luminosity { get; set; }
    public float MaxBright { get; set; }
    public static SkyObjectReplace Read(ref DatCursor c) => new()
    {
        ObjectIndex = c.U32(), PartMeshId = c.U32(), Rotate = c.F32(), Transparent = c.F32(), Luminosity = c.F32(), MaxBright = c.F32(),
    };
}

public class SkyTimeOfDay
{
    public float Begin { get; set; }
    public float DirBright { get; set; }
    public float DirHeading { get; set; }
    public float DirPitch { get; set; }
    public Argb DirColor { get; set; } = null!;
    public float AmbBright { get; set; }
    public Argb AmbColor { get; set; } = null!;
    public float MinWorldFog { get; set; }
    public float MaxWorldFog { get; set; }
    public Argb WorldFogColor { get; set; } = null!;
    public uint WorldFog { get; set; }
    public List<SkyObjectReplace> Replacements { get; set; } = [];
    public static SkyTimeOfDay Read(ref DatCursor c)
    {
        var s = new SkyTimeOfDay
        {
            Begin = c.F32(), DirBright = c.F32(), DirHeading = c.F32(), DirPitch = c.F32(), DirColor = Argb.Read(ref c), AmbBright = c.F32(), AmbColor = Argb.Read(ref c),
            MinWorldFog = c.F32(), MaxWorldFog = c.F32(), WorldFogColor = Argb.Read(ref c), WorldFog = c.U32(),
        };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.Replacements.Add(SkyObjectReplace.Read(ref c));
        return s;
    }
}

public class DayGroup
{
    public float ChanceOfOccur { get; set; }
    public string DayName { get; set; } = "";
    public List<SkyObject> SkyObjects { get; set; } = [];
    public List<SkyTimeOfDay> SkyTimes { get; set; } = [];
    public static DayGroup Read(ref DatCursor c)
    {
        var d = new DayGroup { ChanceOfOccur = c.F32(), DayName = c.Str() };
        c.Align(4);
        int n = (int)c.U32(); for (int i = 0; i < n; i++) d.SkyObjects.Add(SkyObject.Read(ref c));
        n = (int)c.U32(); for (int i = 0; i < n; i++) d.SkyTimes.Add(SkyTimeOfDay.Read(ref c));
        return d;
    }
}

public class SkyDesc
{
    public double TickSize { get; set; }
    public double LightTickSize { get; set; }
    public List<DayGroup> DayGroups { get; set; } = [];
    public static SkyDesc Read(ref DatCursor c)
    {
        var s = new SkyDesc { TickSize = c.F64(), LightTickSize = c.F64() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.DayGroups.Add(DayGroup.Read(ref c));
        return s;
    }
}

public class AmbientSound
{
    public SoundTag Sound { get; set; }
    public float Volume { get; set; }
    public float BaseChance { get; set; }
    public float MinRate { get; set; }
    public float MaxRate { get; set; }
    public static AmbientSound Read(ref DatCursor c) => new() { Sound = (SoundTag)c.U32(), Volume = c.F32(), BaseChance = c.F32(), MinRate = c.F32(), MaxRate = c.F32() };
}

public class AmbientSoundSet
{
    public uint SoundBookId { get; set; }
    public List<AmbientSound> Sounds { get; set; } = [];
    public static AmbientSoundSet Read(ref DatCursor c)
    {
        var s = new AmbientSoundSet { SoundBookId = c.U32() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.Sounds.Add(AmbientSound.Read(ref c));
        return s;
    }
}

public class SoundDesc
{
    public List<AmbientSoundSet> Sets { get; set; } = [];
    public static SoundDesc Read(ref DatCursor c)
    {
        var s = new SoundDesc();
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.Sets.Add(AmbientSoundSet.Read(ref c));
        return s;
    }
}

public class SceneChoice
{
    public uint SoundIndex { get; set; }
    public List<uint> SceneryListIds { get; set; } = [];
    public static SceneChoice Read(ref DatCursor c)
    {
        var s = new SceneChoice { SoundIndex = c.U32() };
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.SceneryListIds.Add(c.U32());
        return s;
    }
}

public class SceneDesc
{
    public List<SceneChoice> Choices { get; set; } = [];
    public static SceneDesc Read(ref DatCursor c)
    {
        var s = new SceneDesc();
        int n = (int)c.U32();
        for (int i = 0; i < n; i++) s.Choices.Add(SceneChoice.Read(ref c));
        return s;
    }
}

public class TerrainKindDesc
{
    public string Name { get; set; } = "";
    public Argb Color { get; set; } = null!;
    public List<uint> SceneChoices { get; set; } = [];
    public static TerrainKindDesc Read(ref DatCursor c)
    {
        var t = new TerrainKindDesc { Name = c.Str() };
        c.Align(4);
        var color = Argb.Read(ref c);
        int n = (int)c.U32();
        var scenes = new List<uint>(n);
        for (int i = 0; i < n; i++) scenes.Add(c.U32());
        return new TerrainKindDesc { Name = t.Name, Color = color, SceneChoices = scenes };
    }
}

public class TerrainAlphaMap
{
    public uint Code { get; set; }
    public uint TextureId { get; set; }
    public static TerrainAlphaMap Read(ref DatCursor c) => new() { Code = c.U32(), TextureId = c.U32() };
}

public class TerrainTex
{
    public uint TextureId { get; set; }
    public uint TexTiling { get; set; }
    public uint MaxVertBright { get; set; }
    public uint MinVertBright { get; set; }
    public uint MaxVertSaturate { get; set; }
    public uint MinVertSaturate { get; set; }
    public uint MaxVertHue { get; set; }
    public uint MinVertHue { get; set; }
    public uint DetailTexTiling { get; set; }
    public uint DetailTextureId { get; set; }
    public static TerrainTex Read(ref DatCursor c) => new()
    {
        TextureId = c.U32(), TexTiling = c.U32(), MaxVertBright = c.U32(), MinVertBright = c.U32(), MaxVertSaturate = c.U32(), MinVertSaturate = c.U32(),
        MaxVertHue = c.U32(), MinVertHue = c.U32(), DetailTexTiling = c.U32(), DetailTextureId = c.U32(),
    };
}

public class TerrainTexDesc
{
    public TerrainKind Kind { get; set; }
    public TerrainTex Tex { get; set; } = null!;
    public static TerrainTexDesc Read(ref DatCursor c) => new() { Kind = (TerrainKind)c.I32(), Tex = TerrainTex.Read(ref c) };
}

public class TexMerge
{
    public uint BaseTexSize { get; set; }
    public List<TerrainAlphaMap> CornerMaps { get; set; } = [];
    public List<TerrainAlphaMap> SideMaps { get; set; } = [];
    public List<TerrainAlphaMap> RoadMaps { get; set; } = [];
    public List<TerrainTexDesc> TerrainTexes { get; set; } = [];
    public static TexMerge Read(ref DatCursor c)
    {
        var t = new TexMerge { BaseTexSize = c.U32() };
        int n = (int)c.U32(); for (int i = 0; i < n; i++) t.CornerMaps.Add(TerrainAlphaMap.Read(ref c));
        n = (int)c.U32(); for (int i = 0; i < n; i++) t.SideMaps.Add(TerrainAlphaMap.Read(ref c));
        n = (int)c.U32(); for (int i = 0; i < n; i++) t.RoadMaps.Add(TerrainAlphaMap.Read(ref c));
        n = (int)c.U32(); for (int i = 0; i < n; i++) t.TerrainTexes.Add(TerrainTexDesc.Read(ref c));
        return t;
    }
}

public class LandSurfaces
{
    public uint Kind { get; set; }
    public TexMerge TexMerge { get; set; } = null!;
    public static LandSurfaces Read(ref DatCursor c) => new() { Kind = c.U32(), TexMerge = TexMerge.Read(ref c) };
}

public class TerrainDesc
{
    public List<TerrainKindDesc> Kinds { get; set; } = [];
    public LandSurfaces Surfaces { get; set; } = null!;
    public static TerrainDesc Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var kinds = new List<TerrainKindDesc>(n);
        for (int i = 0; i < n; i++) kinds.Add(TerrainKindDesc.Read(ref c));
        return new TerrainDesc { Kinds = kinds, Surfaces = LandSurfaces.Read(ref c) };
    }
}

public class RegionMisc
{
    public uint Version { get; set; }
    public uint GameMapId { get; set; }
    public uint AutotestMapId { get; set; }
    public uint AutotestMapSize { get; set; }
    public uint ClearCellId { get; set; }
    public uint ClearMonsterId { get; set; }
    public static RegionMisc Read(ref DatCursor c) => new()
    {
        Version = c.U32(), GameMapId = c.U32(), AutotestMapId = c.U32(), AutotestMapSize = c.U32(), ClearCellId = c.U32(), ClearMonsterId = c.U32(),
    };
}

// The world description: terrain sizes, calendar, sky, ambient sound, scenery and texturing.
public class WorldRegion : DatRecord, IDatRecord<WorldRegion>
{
    public static RecordKind Kind => RecordKind.Region;
    public static (uint First, uint Last) IdRange => (0x13000000, 0x1300FFFF);
    public uint RegionNumber { get; set; }
    public uint Version { get; set; }
    public string Name { get; set; } = "";
    public LandLayout Land { get; set; } = null!;
    public GameTime Time { get; set; } = null!;
    public RegionParts Parts { get; set; }
    public SkyDesc Sky { get; set; }
    public SoundDesc Sound { get; set; }
    public SceneDesc Scenes { get; set; }
    public TerrainDesc Terrain { get; set; } = null!;
    public RegionMisc Misc { get; set; }

    public static WorldRegion Read(ref DatCursor c)
    {
        uint id = c.U32();
        uint number = c.U32(), version = c.U32();
        string name = c.Str();
        c.Align(4);
        var land = LandLayout.Read(ref c);
        var time = GameTime.Read(ref c);
        var parts = (RegionParts)c.U32();
        SkyDesc sky = (parts & RegionParts.HasSkyInfo) != 0 ? SkyDesc.Read(ref c) : null;
        SoundDesc sound = (parts & RegionParts.HasSoundInfo) != 0 ? SoundDesc.Read(ref c) : null;
        SceneDesc scenes = (parts & RegionParts.HasSceneInfo) != 0 ? SceneDesc.Read(ref c) : null;
        var terrain = TerrainDesc.Read(ref c);
        RegionMisc misc = (parts & RegionParts.HasRegionMisc) != 0 ? RegionMisc.Read(ref c) : null;
        return new WorldRegion
        {
            Id = id, RegionNumber = number, Version = version, Name = name, Land = land, Time = time, Parts = parts,
            Sky = sky, Sound = sound, Scenes = scenes, Terrain = terrain, Misc = misc,
        };
    }
}
