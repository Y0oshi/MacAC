using System.Numerics;

namespace MacAC.Client.Graphics.Batching;

/// <summary>
/// Everything about one instance that varies between the instances sharing a <see cref="ClusterTag"/>.
/// A group's shared state lives in the key; this is the rest, and there is exactly one of these per
/// instance the group draws.
/// </summary>
internal readonly record struct InstFacts(
    Matrix4x4 Model,
    int SubmissionOrder,
    uint ClipSlot,
    RealmPaintRouter.InstLampGroup Lamps,
    uint Inside,
    uint SpecificsBucket,
    float Opacity,
    Vector2 PickIllumination);

/// <summary>
/// The instances of one draw group. Every channel holds the same number of entries in the same
/// order, which is why an instance goes in whole through <see cref="Append"/> and nothing else
/// writes: a slot filled only in part would draw with another instance's lighting or opacity and
/// raise nothing. The per-channel readers exist because the emit loops copy one channel at a time
/// into one GPU buffer at a time.
/// </summary>
internal sealed class InstRoster
{
    private readonly List<Matrix4x4> _models = [];
    private readonly List<int> _submissionOrders = [];
    private readonly List<uint> _clipSlots = [];
    private readonly List<RealmPaintRouter.InstLampGroup> _lamps = [];
    private readonly List<uint> _inside = [];
    private readonly List<uint> _specificsBuckets = [];
    private readonly List<float> _opacities = [];
    private readonly List<Vector2> _pickIllumination = [];

    /// <summary>How many instances this group draws. Every channel holds exactly this many.</summary>
    public int Count => _models.Count;

    /// <summary>Adds one instance, writing every channel. There is no way to write only some.</summary>
    public void Append(in InstFacts inst)
    {
        _models.Add(inst.Model);
        _submissionOrders.Add(inst.SubmissionOrder);
        _clipSlots.Add(inst.ClipSlot);
        _lamps.Add(inst.Lamps);
        _inside.Add(inst.Inside);
        _specificsBuckets.Add(inst.SpecificsBucket);
        _opacities.Add(inst.Opacity);
        _pickIllumination.Add(inst.PickIllumination);
    }

    public Matrix4x4 Model(int ordinal) => _models[ordinal];

    public int SubmissionOrder(int ordinal) => _submissionOrders[ordinal];

    public uint ClipSlot(int ordinal) => _clipSlots[ordinal];

    public RealmPaintRouter.InstLampGroup Lamps(int ordinal) => _lamps[ordinal];

    public uint Inside(int ordinal) => _inside[ordinal];

    public uint SpecificsBucket(int ordinal) => _specificsBuckets[ordinal];

    public float Opacity(int ordinal) => _opacities[ordinal];

    public Vector2 PickIllumination(int ordinal) => _pickIllumination[ordinal];

    /// <summary>Reads one instance back as a whole, for the paths that forward all of it at once.</summary>
    public InstFacts this[int ordinal] => new(
        _models[ordinal],
        _submissionOrders[ordinal],
        _clipSlots[ordinal],
        _lamps[ordinal],
        _inside[ordinal],
        _specificsBuckets[ordinal],
        _opacities[ordinal],
        _pickIllumination[ordinal]);

    /// <summary>Drops every instance, keeping the capacity for the next cycle.</summary>
    public void Clear()
    {
        _models.Clear();
        _submissionOrders.Clear();
        _clipSlots.Clear();
        _lamps.Clear();
        _inside.Clear();
        _specificsBuckets.Clear();
        _opacities.Clear();
        _pickIllumination.Clear();
    }

    /// <summary>Room currently reserved, in instances. Observed when checking that a retired group let go.</summary>
    internal int Capacity => _models.Capacity;

    /// <summary>Drops every instance and hands the capacity back.</summary>
    public void Release()
    {
        Clear();
        _models.TrimExcess();
        _submissionOrders.TrimExcess();
        _clipSlots.TrimExcess();
        _lamps.TrimExcess();
        _inside.TrimExcess();
        _specificsBuckets.TrimExcess();
        _opacities.TrimExcess();
        _pickIllumination.TrimExcess();
    }
}
