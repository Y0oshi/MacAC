using System.Numerics;

#nullable disable

namespace MacAC.Dat;

// A typed property value from the client's property system. Kinds are looked up in the catalog by property id.
public abstract class PropertyValue
{
    public abstract PropertyKind Kind { get; }
    public uint PropertyId { get; set; }
    public bool KeyedInFile { get; set; }

    // Reads the property id then the value whose kind the catalog gives for that id.
    public static PropertyValue ReadKeyed(ref DatCursor c)
    {
        uint id = c.U32();
        var catalog = c.Catalog ?? throw new DatFormatException("a property catalog is needed to decode property values");
        if (!catalog.Properties.TryGetValue(id, out var spec)) throw new DatFormatException($"property 0x{id:X} is not in the catalog");
        return ReadOfKind(ref c, spec.Kind, id, keyed: true);
    }

    public static PropertyValue ReadOfKind(ref DatCursor c, PropertyKind kind, uint id = 0, bool keyed = false)
    {
        switch (kind)
        {
            case PropertyKind.Enum: return new EnumProperty { PropertyId = id, KeyedInFile = keyed, Value = c.U32() };
            case PropertyKind.Bool: return new BoolProperty { PropertyId = id, KeyedInFile = keyed, Value = c.U8() != 0 };
            case PropertyKind.DataId: return new DataIdProperty { PropertyId = id, KeyedInFile = keyed, Value = c.U32() };
            case PropertyKind.Float: return new FloatProperty { PropertyId = id, KeyedInFile = keyed, Value = c.F32() };
            case PropertyKind.Integer: return new IntegerProperty { PropertyId = id, KeyedInFile = keyed, Value = c.I32() };
            case PropertyKind.StringInfo: return new TextInfoProperty { PropertyId = id, KeyedInFile = keyed, Value = TextInfo.Read(ref c) };
            case PropertyKind.Color: return new ColorProperty { PropertyId = id, KeyedInFile = keyed, Value = Argb.Read(ref c) };
            case PropertyKind.Array:
            {
                int n = (int)c.U32();
                var items = new List<PropertyValue>(n);
                for (int i = 0; i < n; i++) items.Add(ReadKeyed(ref c));
                return new ArrayProperty { PropertyId = id, KeyedInFile = keyed, Items = items };
            }
            case PropertyKind.Struct:
            {
                c.U8();
                int n = c.U8();
                var d = new Dictionary<uint, PropertyValue>(n);
                for (int i = 0; i < n; i++) { uint k = c.U32(); d[k] = ReadKeyed(ref c); }
                return new StructProperty { PropertyId = id, KeyedInFile = keyed, Fields = d };
            }
            case PropertyKind.Vector: return new VectorProperty { PropertyId = id, KeyedInFile = keyed, Value = c.Vec3() };
            case PropertyKind.Bitfield32: return new Bitfield32Property { PropertyId = id, KeyedInFile = keyed, Value = c.U32() };
            case PropertyKind.Bitfield64: return new Bitfield64Property { PropertyId = id, KeyedInFile = keyed, Value = c.U64() };
            case PropertyKind.InstanceId: return new InstanceIdProperty { PropertyId = id, KeyedInFile = keyed, Value = c.U32() };
            default: throw new DatFormatException($"property kind {kind} is not decodable");
        }
    }

    public static Dictionary<uint, PropertyValue> ReadHashedBag(ref DatCursor c)
    {
        c.U8();
        int n = (int)c.PackedU32();
        var d = new Dictionary<uint, PropertyValue>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); d[k] = ReadKeyed(ref c); }
        return d;
    }
}

public class TextInfo
{
    public byte Token { get; set; }
    public uint StringId { get; set; }
    public uint TableId { get; set; }
    public TextOverrideBits Override { get; set; }
    public byte English { get; set; }
    public byte Comment { get; set; }
    public static TextInfo Read(ref DatCursor c) => new() { Token = c.U8(), StringId = c.U32(), TableId = c.U32(), Override = (TextOverrideBits)c.U8(), English = c.U8(), Comment = c.U8() };
}

public class EnumProperty : PropertyValue { public uint Value { get; set; } public override PropertyKind Kind => PropertyKind.Enum; }
public class BoolProperty : PropertyValue { public bool Value { get; set; } public override PropertyKind Kind => PropertyKind.Bool; }
public class DataIdProperty : PropertyValue { public uint Value { get; set; } public override PropertyKind Kind => PropertyKind.DataId; }
public class FloatProperty : PropertyValue { public float Value { get; set; } public override PropertyKind Kind => PropertyKind.Float; }
public class IntegerProperty : PropertyValue { public int Value { get; set; } public override PropertyKind Kind => PropertyKind.Integer; }
public class TextInfoProperty : PropertyValue { public TextInfo Value { get; set; } = null!; public override PropertyKind Kind => PropertyKind.StringInfo; }
public class ColorProperty : PropertyValue { public Argb Value { get; set; } = null!; public override PropertyKind Kind => PropertyKind.Color; }
public class ArrayProperty : PropertyValue { public List<PropertyValue> Items { get; set; } = []; public override PropertyKind Kind => PropertyKind.Array; }
public class StructProperty : PropertyValue { public Dictionary<uint, PropertyValue> Fields { get; set; } = []; public override PropertyKind Kind => PropertyKind.Struct; }
public class VectorProperty : PropertyValue { public Vector3 Value { get; set; } public override PropertyKind Kind => PropertyKind.Vector; }
public class Bitfield32Property : PropertyValue { public uint Value { get; set; } public override PropertyKind Kind => PropertyKind.Bitfield32; }
public class Bitfield64Property : PropertyValue { public ulong Value { get; set; } public override PropertyKind Kind => PropertyKind.Bitfield64; }
public class InstanceIdProperty : PropertyValue { public uint Value { get; set; } public override PropertyKind Kind => PropertyKind.InstanceId; }

public class PropertySpec
{
    public uint Name { get; set; }
    public PropertyKind Kind { get; set; }
    public PropertyGroup Group { get; set; }
    public uint Provider { get; set; }
    public uint Data { get; set; }
    public PropertyPatchBits Patch { get; set; }
    public PropertyValue Default { get; set; }
    public PropertyValue Max { get; set; }
    public PropertyValue Min { get; set; }
    public float PredictionTimeout { get; set; }
    public PropertyInheritance Inheritance { get; set; }
    public PropertyShelf Shelf { get; set; }
    public PropertyPropagation Propagation { get; set; }
    public PropertyCaching Caching { get; set; }
    public bool Required { get; set; }
    public bool ReadOnly { get; set; }
    public bool NoCheckpoint { get; set; }
    public bool Recorded { get; set; }
    public bool DoNotReplay { get; set; }
    public bool AbsoluteTimeStamp { get; set; }
    public bool Groupable { get; set; }
    public bool PropagateToChildren { get; set; }
    public Dictionary<uint, uint> AvailableProperties { get; set; } = [];

    public static PropertySpec Read(ref DatCursor c)
    {
        uint name = c.U32();
        var kind = (PropertyKind)c.U32();
        var group = (PropertyGroup)c.U32();
        uint provider = c.U32(), data = c.U32();
        var patch = (PropertyPatchBits)c.I32();
        PropertyValue def = c.U8() != 0 ? PropertyValue.ReadOfKind(ref c, kind) : null;
        PropertyValue max = c.U8() != 0 ? PropertyValue.ReadOfKind(ref c, kind) : null;
        PropertyValue min = c.U8() != 0 ? PropertyValue.ReadOfKind(ref c, kind) : null;
        float timeout = c.F32();
        var inh = (PropertyInheritance)c.U8();
        var shelf = (PropertyShelf)c.U8();
        var prop = (PropertyPropagation)c.U8();
        var cache = (PropertyCaching)c.U8();
        bool required = c.U8() != 0, ro = c.U8() != 0, nocp = c.U8() != 0, rec = c.U8() != 0, noreplay = c.U8() != 0, abs = c.U8() != 0, groupable = c.U8() != 0, toKids = c.U8() != 0;
        c.U8();
        int n = c.U8();
        var avail = new Dictionary<uint, uint>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); avail[k] = c.U32(); }
        return new PropertySpec
        {
            Name = name, Kind = kind, Group = group, Provider = provider, Data = data, Patch = patch, Default = def, Max = max, Min = min, PredictionTimeout = timeout,
            Inheritance = inh, Shelf = shelf, Propagation = prop, Caching = cache, Required = required, ReadOnly = ro, NoCheckpoint = nocp, Recorded = rec,
            DoNotReplay = noreplay, AbsoluteTimeStamp = abs, Groupable = groupable, PropagateToChildren = toKids, AvailableProperties = avail,
        };
    }
}

public class NameMapData
{
    public uint BaseMapId { get; set; }
    public uint Unknown { get; set; }
    public Dictionary<uint, string> Names { get; set; } = [];
    public static NameMapData Read(ref DatCursor c) => new() { BaseMapId = c.U32(), Unknown = c.U32(), Names = Tables.ReadHashed(ref c, Tables.U32, Tables.PStr) };
}

// The master list of property definitions (portal 0x39000001); other records need it to decode their property bags.
public class PropertyCatalog : DatRecord, IDatRecord<PropertyCatalog>
{
    public static RecordKind Kind => RecordKind.MasterProperty;
    public static (uint First, uint Last) IdRange => (0x39000000, 0x39FFFFFF);
    public const uint DefaultId = 0x39000001;
    public NameMapData Names { get; set; } = null!;
    public Dictionary<uint, PropertySpec> Properties { get; set; } = [];
    public static PropertyCatalog Read(ref DatCursor c)
    {
        uint id = c.U32();
        var names = NameMapData.Read(ref c);
        c.U8();
        int n = (int)c.PackedU32();
        var props = new Dictionary<uint, PropertySpec>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); props[k] = PropertySpec.Read(ref c); }
        return new PropertyCatalog { Id = id, Names = names, Properties = props };
    }
}

public class PropertyBag : DatRecord, IDatRecord<PropertyBag>
{
    public static RecordKind Kind => RecordKind.DBProperties;
    public static (uint First, uint Last) IdRange => (0x78000000, 0x7FFFFFFF);
    public Dictionary<uint, PropertyValue> Properties { get; set; } = [];
    public static PropertyBag Read(ref DatCursor c)
    {
        uint id = c.U32();
        c.U8();
        int n = c.U8();
        var d = new Dictionary<uint, PropertyValue>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); d[k] = PropertyValue.ReadKeyed(ref c); }
        return new PropertyBag { Id = id, Properties = d };
    }
}

// ---- UI layouts ----

public abstract class UiMedia
{
    public abstract UiMediaKind Kind { get; }
    public UiMediaKind KindInFile { get; set; }

    public static UiMedia Read(ref DatCursor c)
    {
        var kind = (UiMediaKind)c.I32();
        var again = (UiMediaKind)c.I32();
        return kind switch
        {
            UiMediaKind.Movie => new UiMovie { KindInFile = again, FileName = c.PStr(), StretchToFullScreen = c.U8() != 0 },
            UiMediaKind.Alpha => new UiAlpha { KindInFile = again, File = c.U32() },
            UiMediaKind.Animation => ReadAnimation(ref c, again),
            UiMediaKind.Cursor => new UiCursor { KindInFile = again, File = c.U32(), XHotspot = c.U32(), YHotspot = c.U32() },
            UiMediaKind.Image => new UiImage { KindInFile = again, File = c.U32(), DrawMode = (UiDrawMode)c.U32() },
            UiMediaKind.Jump => new UiJump { KindInFile = again, JumpItemIndex = c.U32(), Probability = c.F32() },
            UiMediaKind.Message => new UiMessage { KindInFile = again, Id = c.U32(), Probability = c.F32() },
            UiMediaKind.Pause => new UiPause { KindInFile = again, MinDuration = c.F32(), MaxDuration = c.F32() },
            UiMediaKind.Sound => new UiSound { KindInFile = again, File = c.U32(), Sound = (SoundTag)c.U32() },
            UiMediaKind.State => new UiStateJump { KindInFile = again, StateId = (UiStateId)c.U32(), Probability = c.F32() },
            UiMediaKind.Fade => new UiFade { KindInFile = again, StartAlpha = c.F32(), EndAlpha = c.F32(), Duration = c.F32() },
            _ => null,
        };
    }

    static UiAnimation ReadAnimation(ref DatCursor c, UiMediaKind again)
    {
        float duration = c.F32();
        var mode = (UiDrawMode)c.U32();
        int n = (int)c.U32();
        var frames = new List<uint>(n);
        for (int i = 0; i < n; i++) frames.Add(c.U32());
        return new UiAnimation { KindInFile = again, Duration = duration, DrawMode = mode, Frames = frames };
    }
}

public class UiMovie : UiMedia { public string FileName { get; set; } = ""; public bool StretchToFullScreen { get; set; } public override UiMediaKind Kind => UiMediaKind.Movie; }
public class UiAlpha : UiMedia { public uint File { get; set; } public override UiMediaKind Kind => UiMediaKind.Alpha; }
public class UiAnimation : UiMedia { public float Duration { get; set; } public UiDrawMode DrawMode { get; set; } public List<uint> Frames { get; set; } = []; public override UiMediaKind Kind => UiMediaKind.Animation; }
public class UiCursor : UiMedia { public uint File { get; set; } public uint XHotspot { get; set; } public uint YHotspot { get; set; } public override UiMediaKind Kind => UiMediaKind.Cursor; }
public class UiImage : UiMedia { public uint File { get; set; } public UiDrawMode DrawMode { get; set; } public override UiMediaKind Kind => UiMediaKind.Image; }
public class UiJump : UiMedia { public uint JumpItemIndex { get; set; } public float Probability { get; set; } public override UiMediaKind Kind => UiMediaKind.Jump; }
public class UiMessage : UiMedia { public uint Id { get; set; } public float Probability { get; set; } public override UiMediaKind Kind => UiMediaKind.Message; }
public class UiPause : UiMedia { public float MinDuration { get; set; } public float MaxDuration { get; set; } public override UiMediaKind Kind => UiMediaKind.Pause; }
public class UiSound : UiMedia { public uint File { get; set; } public SoundTag Sound { get; set; } public override UiMediaKind Kind => UiMediaKind.Sound; }
public class UiStateJump : UiMedia { public UiStateId StateId { get; set; } public float Probability { get; set; } public override UiMediaKind Kind => UiMediaKind.State; }
public class UiFade : UiMedia { public float StartAlpha { get; set; } public float EndAlpha { get; set; } public float Duration { get; set; } public override UiMediaKind Kind => UiMediaKind.Fade; }

public class UiState
{
    public uint StateId { get; set; }
    public bool PassToChildren { get; set; }
    public UiInheritBits Inherit { get; set; }
    public Dictionary<uint, PropertyValue> Properties { get; set; } = [];
    public List<UiMedia> Media { get; set; } = [];

    public static UiState Read(ref DatCursor c)
    {
        uint id = c.U32();
        bool pass = c.U8() != 0;
        var inherit = (UiInheritBits)c.U32();
        c.U8();
        int n = (int)c.PackedU32();
        var props = new Dictionary<uint, PropertyValue>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); props[k] = PropertyValue.ReadKeyed(ref c); }
        int nm = (int)c.PackedU32();
        var media = new List<UiMedia>(nm);
        for (int i = 0; i < nm; i++) { var m = UiMedia.Read(ref c); if (m is not null) media.Add(m); }
        return new UiState { StateId = id, PassToChildren = pass, Inherit = inherit, Properties = props, Media = media };
    }
}

public class LayoutNode
{
    public UiState State { get; set; } = null!;
    public uint ReadOrder { get; set; }
    public uint ElementId { get; set; }
    public uint Type { get; set; }
    public uint BaseElement { get; set; }
    public uint BaseLayoutId { get; set; }
    public UiStateId DefaultState { get; set; }
    public uint X { get; set; }
    public uint Y { get; set; }
    public uint Width { get; set; }
    public uint Height { get; set; }
    public uint ZLevel { get; set; }
    public uint LeftEdge { get; set; }
    public uint TopEdge { get; set; }
    public uint RightEdge { get; set; }
    public uint BottomEdge { get; set; }
    public Dictionary<UiStateId, UiState> States { get; set; } = [];
    public Dictionary<uint, LayoutNode> Children { get; set; } = [];

    public static LayoutNode Read(ref DatCursor c)
    {
        var state = UiState.Read(ref c);
        uint order = c.U32(), elementId = c.U32(), type = c.U32(), baseElement = c.U32(), baseLayout = c.U32();
        var def = (UiStateId)c.U32();
        var inh = state.Inherit;
        uint x = (inh & UiInheritBits.X) != 0 ? c.U32() : 0;
        uint y = (inh & UiInheritBits.Y) != 0 ? c.U32() : 0;
        uint w = (inh & UiInheritBits.Width) != 0 ? c.U32() : 0;
        uint h = (inh & UiInheritBits.Height) != 0 ? c.U32() : 0;
        uint z = (inh & UiInheritBits.ZLevel) != 0 ? c.U32() : 0;
        uint l = c.U32(), t = c.U32(), r = c.U32(), b = c.U32();
        var states = Tables.ReadHashed(ref c, static (ref DatCursor c) => (UiStateId)c.U32(), UiState.Read);
        var kids = Tables.ReadHashed(ref c, Tables.U32, Read);
        return new LayoutNode
        {
            State = state, ReadOrder = order, ElementId = elementId, Type = type, BaseElement = baseElement, BaseLayoutId = baseLayout, DefaultState = def,
            X = x, Y = y, Width = w, Height = h, ZLevel = z, LeftEdge = l, TopEdge = t, RightEdge = r, BottomEdge = b, States = states, Children = kids,
        };
    }
}

public class UiLayout : DatRecord, IDatRecord<UiLayout>
{
    public static RecordKind Kind => RecordKind.LayoutDesc;
    public static DatShelf Shelf => DatShelf.Local;
    public static (uint First, uint Last) IdRange => (0x21000000, 0x21FFFFFF);
    public uint Width { get; set; }
    public uint Height { get; set; }
    public Dictionary<uint, LayoutNode> Elements { get; set; } = [];
    public static UiLayout Read(ref DatCursor c) => new() { Id = c.U32(), Width = c.U32(), Height = c.U32(), Elements = Tables.ReadHashed(ref c, Tables.U32, LayoutNode.Read) };
}

// ---- input maps ----

public class UserBinding
{
    public uint ActionClass { get; set; }
    public uint ActionName { get; set; }
    public uint ActionDescription { get; set; }
    public static UserBinding Read(ref DatCursor c) => new() { ActionClass = c.U32(), ActionName = c.U32(), ActionDescription = c.U32() };
}

public class ActionBinding
{
    public uint Magic { get; set; }
    public byte Unknown { get; set; }
    public ToggleKind Toggle { get; set; }
    public uint DummyListLength { get; set; }
    public UserBinding Binding { get; set; } = null!;
    public static ActionBinding Read(ref DatCursor c) => new() { Magic = c.U32(), Unknown = c.U8(), Toggle = (ToggleKind)c.U32(), DummyListLength = c.U32(), Binding = UserBinding.Read(ref c) };
}

public class InputConflicts
{
    public uint InputMap { get; set; }
    public List<uint> ConflictingInputMaps { get; set; } = [];
    public static InputConflicts Read(ref DatCursor c)
    {
        var i = new InputConflicts { InputMap = c.U32() };
        int n = (int)c.U32();
        for (int k = 0; k < n; k++) i.ConflictingInputMaps.Add(c.U32());
        return i;
    }
}

public class ActionMap : DatRecord, IDatRecord<ActionMap>
{
    public static RecordKind Kind => RecordKind.ActionMap;
    public static (uint First, uint Last) IdRange => (0x26000000, 0x2600FFFF);
    public Dictionary<uint, Dictionary<uint, ActionBinding>> InputMaps { get; set; } = [];
    public Dictionary<uint, InputConflicts> Conflicts { get; set; } = [];
    public uint TextTableId { get; set; }
    public static ActionMap Read(ref DatCursor c)
    {
        uint id = c.U32();
        var maps = Tables.ReadHashed(ref c, Tables.U32, static (ref DatCursor c) => Tables.ReadHashed(ref c, Tables.U32, ActionBinding.Read));
        uint table = c.U32();
        var conflicts = Tables.ReadHashed(ref c, Tables.U32, InputConflicts.Read);
        return new ActionMap { Id = id, InputMaps = maps, TextTableId = table, Conflicts = conflicts };
    }
}

public class InputDevice
{
    public InputDeviceKind Kind { get; set; }
    public Guid Guid { get; set; }
    public static InputDevice Read(ref DatCursor c) => new() { Kind = (InputDeviceKind)c.U8(), Guid = new Guid(c.Bytes(16)) };
}

public class ControlSpec
{
    public uint Key { get; set; }
    public uint Modifier { get; set; }
    public static ControlSpec Read(ref DatCursor c) => new() { Key = c.U32(), Modifier = c.U32() };
}

public class QualifiedControl
{
    public ControlSpec Key { get; set; } = null!;
    public uint Activation { get; set; }
    public uint Unknown { get; set; }
    public static QualifiedControl Read(ref DatCursor c) => new() { Key = ControlSpec.Read(ref c), Activation = c.U32(), Unknown = c.U32() };
}

public class InputMap
{
    public List<QualifiedControl> Mappings { get; set; } = [];
    public static InputMap Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var m = new InputMap { Mappings = new List<QualifiedControl>(n) };
        for (int i = 0; i < n; i++) m.Mappings.Add(QualifiedControl.Read(ref c));
        return m;
    }
}

public class MasterInputMap : DatRecord, IDatRecord<MasterInputMap>
{
    public static RecordKind Kind => RecordKind.MasterInputMap;
    public static (uint First, uint Last) IdRange => (0x14000000, 0x1400FFFF);
    public string Name { get; set; } = "";
    public Guid MapGuid { get; set; }
    public List<InputDevice> Devices { get; set; } = [];
    public List<ControlSpec> MetaKeys { get; set; } = [];
    public Dictionary<uint, InputMap> InputMaps { get; set; } = [];
    public static MasterInputMap Read(ref DatCursor c)
    {
        uint id = c.U32();
        string name = c.PStr();
        var guid = new Guid(c.Bytes(16));
        int n = (int)c.U32();
        var devices = new List<InputDevice>(n);
        for (int i = 0; i < n; i++) devices.Add(InputDevice.Read(ref c));
        n = (int)c.U32();
        var meta = new List<ControlSpec>(n);
        for (int i = 0; i < n; i++) meta.Add(ControlSpec.Read(ref c));
        n = (int)c.U32();
        var maps = new Dictionary<uint, InputMap>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); maps[k] = InputMap.Read(ref c); }
        return new MasterInputMap { Id = id, Name = name, MapGuid = guid, Devices = devices, MetaKeys = meta, InputMaps = maps };
    }
}
