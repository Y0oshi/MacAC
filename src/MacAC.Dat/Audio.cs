#nullable disable

namespace MacAC.Dat;

public class SoundWeights
{
    public float Priority { get; set; }
    public float Probability { get; set; }
    public float Volume { get; set; }
    public static SoundWeights Read(ref DatCursor c) => new() { Priority = c.F32(), Probability = c.F32(), Volume = c.F32() };
}

public class SoundChoice
{
    public uint ClipId { get; set; }
    public float Priority { get; set; }
    public float Probability { get; set; }
    public float Volume { get; set; }
    public static SoundChoice Read(ref DatCursor c) => new() { ClipId = c.U32(), Priority = c.F32(), Probability = c.F32(), Volume = c.F32() };
}

public class SoundChoices
{
    public List<SoundChoice> Choices { get; set; } = [];
    public int Unknown { get; set; }
    public static SoundChoices Read(ref DatCursor c)
    {
        int n = (int)c.U32();
        var choices = new List<SoundChoice>(n);
        for (int i = 0; i < n; i++) choices.Add(SoundChoice.Read(ref c));
        return new SoundChoices { Choices = choices, Unknown = c.I32() };
    }
}

// Which clips play for which sound event, per creature/object.
public class SoundBook : DatRecord, IDatRecord<SoundBook>
{
    public static RecordKind Kind => RecordKind.SoundTable;
    public static (uint First, uint Last) IdRange => (0x20000000, 0x2000FFFF);
    public int HashKey { get; set; }
    public Dictionary<uint, SoundWeights> Weights { get; set; } = [];
    public Dictionary<SoundTag, SoundChoices> Sounds { get; set; } = [];
    public static SoundBook Read(ref DatCursor c)
    {
        uint id = c.U32();
        int key = c.I32();
        int n = c.I32();
        var weights = new Dictionary<uint, SoundWeights>(n);
        for (int i = 0; i < n; i++) { uint k = c.U32(); weights[k] = SoundWeights.Read(ref c); }
        n = c.I32();
        var sounds = new Dictionary<SoundTag, SoundChoices>(n);
        for (int i = 0; i < n; i++) { var k = (SoundTag)c.U32(); sounds[k] = SoundChoices.Read(ref c); }
        return new SoundBook { Id = id, HashKey = key, Weights = weights, Sounds = sounds };
    }
}

// A wave: the RIFF-style header and sample data stored separately.
public class SoundClip : DatRecord, IDatRecord<SoundClip>
{
    public static RecordKind Kind => RecordKind.Wave;
    public static (uint First, uint Last) IdRange => (0x0A000000, 0x0A00FFFF);
    public byte[] Header { get; set; } = [];
    public byte[] Data { get; set; } = [];
    public static SoundClip Read(ref DatCursor c)
    {
        uint id = c.U32();
        int nh = c.I32(), nd = c.I32();
        return new SoundClip { Id = id, Header = c.Array(nh), Data = c.Array(nd) };
    }
}
