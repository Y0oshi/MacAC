using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public enum WidgetPropertyKind : byte
{
    Enum,
    Bool,
    DataId,
    Float,
    Integer,
    StringInfo,
    Color,
    Array,
    Struct,
    Vector,
    Bitfield32,
    Bitfield64,
    InstanceId,
}

public readonly record struct WidgetColorValue(byte Blue, byte Green, byte Red, byte Alpha);

public readonly record struct WidgetStringInfoValue(
    byte Token,
    uint StringId,
    uint TableId,
    byte Override,
    byte English,
    byte Comment);

public sealed class WidgetPropertyValue
{
    public WidgetPropertyKind Kind;
    public uint MasterPropertyId;
    public ulong UnsignedValue;
    public int IntegerValue;
    public float FloatValue;
    public bool BoolValue;
    public WidgetStringInfoValue StringInfoValue;
    public WidgetColorValue ColorValue;
    public Vector3 VectorValue;
    public List<WidgetPropertyValue> ArrayValue = [];
    public Dictionary<uint, WidgetPropertyValue> StructValue = [];

    public WidgetPropertyValue Clone()
    {
        WidgetPropertyValue replicate = new WidgetPropertyValue
        {
            Kind = Kind,
            MasterPropertyId = MasterPropertyId,
            UnsignedValue = UnsignedValue,
            IntegerValue = IntegerValue,
            FloatValue = FloatValue,
            BoolValue = BoolValue,
            StringInfoValue = StringInfoValue,
            ColorValue = ColorValue,
            VectorValue = VectorValue,
        };

        foreach (var gear in ArrayValue)
            replicate.ArrayValue.Add(gear.Clone());
        foreach (var (tag, val) in StructValue)
            replicate.StructValue[tag] = val.Clone();
        return replicate;
    }
}

public sealed class WidgetPropertyBag
{
    public Dictionary<uint, WidgetPropertyValue> Values = [];

    public bool TryGetValue(uint ident, out WidgetPropertyValue val)
        => Values.TryGetValue(ident, out val!);

    public WidgetPropertyBag Clone()
    {
        WidgetPropertyBag replicate = new WidgetPropertyBag();
        foreach (var (tag, val) in Values)
            replicate.Values[tag] = val.Clone();
        return replicate;
    }

    public static WidgetPropertyBag Merge(WidgetPropertyBag baseProps, WidgetPropertyBag derivedProps)
    {
        WidgetPropertyBag merged = baseProps.Clone();
        foreach (var (tag, val) in derivedProps.Values)
            merged.Values[tag] = val.Clone();
        return merged;
    }
}

public readonly record struct WidgetImageMedia(uint File, int DrawMode);

public sealed record WidgetLoopingImageMotion(uint[] Frames, float Duration, int DrawMode)
{
    public uint Sample(double passedSecs)
    {
        if (Frames.Length is 0 || !float.IsFinite(Duration) || Duration <= 0f)
            return 0u;
        double moment = double.IsFinite(passedSecs) ? Math.Max(0d, passedSecs) : 0d;
        float stage = (float)(moment % Duration / Duration);
        return Frames[(int)(stage * (double)(Frames.Length - 1) + 0.5d)];
    }
}

public enum WidgetMediaStepKind
{
    Other,

    Image,

    Pause,

    /// <summary>Branch to another entry - what makes a sequence loop.</summary>
    Jump,

    State,
}

public readonly record struct WidgetMediaStep(
    WidgetMediaStepKind Kind,
    uint File,
    int DrawMode,
    float MinDuration,
    float MaxDuration,
    uint JumpIndex,
    float Probability,
    int RawType = 0);

public sealed class WidgetStateInfo
{
    public const uint StraightPhaseIdent = uint.MaxValue;

    public uint Id;
    public string Name = "";
    public bool PassToChildren;
    public uint IncorporationFlags;
    public WidgetImageMedia? Image;
    public WidgetLoopingImageMotion? LoopingAnimation;

    public IReadOnlyList<WidgetMediaStep> MediaSteps = Array.Empty<WidgetMediaStep>();
    public WidgetCursorMedia? Cursor;
    public WidgetPropertyBag Properties = new();

    public int MediaCount;

    public int ImageMediaCount;

    public WidgetStateInfo Clone()
    {
        return new()
        {
            Id = Id,
            Name = Name,
            PassToChildren = PassToChildren,
            IncorporationFlags = IncorporationFlags,
            Image = Image,
            LoopingAnimation = LoopingAnimation,
            MediaSteps = MediaSteps,
            Cursor = Cursor,
            Properties = Properties.Clone(),
            MediaCount = MediaCount,
            ImageMediaCount = ImageMediaCount,
        };
    }

    public static WidgetStateInfo Merge(WidgetStateInfo basePhase, WidgetStateInfo derivedPhase)
    {
        return new()
        {
            Id = derivedPhase.Id,
            Name = derivedPhase.Name,
            PassToChildren = derivedPhase.PassToChildren,
            IncorporationFlags = derivedPhase.IncorporationFlags,
            Image = derivedPhase.Image ?? basePhase.Image,
            LoopingAnimation = derivedPhase.LoopingAnimation ?? basePhase.LoopingAnimation,
            Cursor = derivedPhase.Cursor ?? basePhase.Cursor,
            Properties = WidgetPropertyBag.Merge(basePhase.Properties, derivedPhase.Properties),
            MediaCount = basePhase.MediaCount + derivedPhase.MediaCount,
            ImageMediaCount = basePhase.ImageMediaCount + derivedPhase.ImageMediaCount,
        };
    }
}
