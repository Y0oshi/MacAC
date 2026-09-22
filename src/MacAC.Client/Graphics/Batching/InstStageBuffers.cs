using System.Numerics;
using System.Runtime.CompilerServices;
using MacAC.Mechanics.Illumination;

namespace MacAC.Client.Graphics.Batching;

/// <summary>
/// The per-instance data a frame stages for the GPU: model matrix, clip socket, light set, indoor
/// flag, detail bucket, opacity and picking illumination, one slot per instance across seven
/// channels.
///
/// The arrays are scratch, reused between frames and never cleared, so a slot written only in part
/// keeps the previous frame's values in whatever was missed. Hence one writer taking a whole
/// instance, one growth policy covering every channel, and reads handed out as spans of exactly the
/// requested length.
/// </summary>
internal sealed class InstStageBuffers
{
    /// <summary>Spare room kept past what a frame asked for, so a frame or two of growth settles.</summary>
    private const int Headroom = 256;

    private const int LampsPerInstance = LightKeeper.UpperLightsPerObject;

    private float[] _models = new float[Headroom * 16];
    private uint[] _clipSockets = new uint[Headroom];
    private int[] _lamps = new int[Headroom * LampsPerInstance];
    private uint[] _inside = new uint[Headroom];
    private uint[] _specificsBuckets = new uint[Headroom];
    private float[] _opacities = new float[Headroom];
    private Vector2[] _pickIllumination = new Vector2[Headroom];

    /// <summary>How many instances there is currently room for.</summary>
    public int Capacity => Math.Max(_models.Length / 16, _lamps.Length / LampsPerInstance);

    /// <summary>Grows every channel to hold <paramref name="instances"/>, keeping some headroom.</summary>
    public void EnsureRoom(int instances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instances);
        if (instances <= Capacity)
            return;

        int roomy = checked(instances + Headroom);
        Grow(ref _models, roomy * 16);
        Grow(ref _clipSockets, roomy);
        Grow(ref _lamps, roomy * LampsPerInstance);
        Grow(ref _inside, roomy);
        Grow(ref _specificsBuckets, roomy);
        Grow(ref _opacities, roomy);
        Grow(ref _pickIllumination, roomy);
    }

    /// <summary>
    /// Reallocates every channel at exactly <paramref name="instances"/>, for handing memory back
    /// when a frame needs far less than the last peak.
    /// </summary>
    public void Reset(int instances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(instances);
        _models = new float[checked(instances * 16)];
        _clipSockets = new uint[instances];
        _lamps = new int[checked(instances * LampsPerInstance)];
        _inside = new uint[instances];
        _specificsBuckets = new uint[instances];
        _opacities = new float[instances];
        _pickIllumination = new Vector2[instances];
    }

    /// <summary>
    /// Fills every channel of one instance slot. The submission order is not staged: it decides what
    /// order the draws are issued in, and the GPU never reads it.
    /// </summary>
    public void Write(int slot, in InstFacts inst)
    {
        WriteModel(_models.AsSpan(slot * 16, 16), inst.Model);
        _clipSockets[slot] = inst.ClipSlot;
        WriteLamps(_lamps.AsSpan(slot * LampsPerInstance, LampsPerInstance), inst.Lamps);
        _inside[slot] = inst.Inside;
        _specificsBuckets[slot] = inst.SpecificsBucket;
        _opacities[slot] = inst.Opacity;
        _pickIllumination[slot] = inst.PickIllumination;
    }

    public Span<float> Models(int instances) => _models.AsSpan(0, checked(instances * 16));

    public Span<uint> ClipSockets(int instances) => _clipSockets.AsSpan(0, instances);

    public Span<int> Lamps(int instances) =>
        _lamps.AsSpan(0, checked(instances * LampsPerInstance));

    public Span<uint> Inside(int instances) => _inside.AsSpan(0, instances);

    public Span<uint> SpecificsBuckets(int instances) => _specificsBuckets.AsSpan(0, instances);

    public Span<float> Opacities(int instances) => _opacities.AsSpan(0, instances);

    public Span<Vector2> PickIllumination(int instances) => _pickIllumination.AsSpan(0, instances);

    /// <summary>What these buffers are holding, for the scratch-memory ledger.</summary>
    public long OctetsHeld => checked(
        (long)_models.Length * sizeof(float)
        + (long)_clipSockets.Length * sizeof(uint)
        + (long)_lamps.Length * sizeof(int)
        + (long)_inside.Length * sizeof(uint)
        + (long)_specificsBuckets.Length * sizeof(uint)
        + (long)_opacities.Length * sizeof(float)
        + (long)_pickIllumination.Length * Unsafe.SizeOf<Vector2>());

    private static void Grow<T>(ref T[] channel, int length)
    {
        if (channel.Length < length)
            channel = new T[length];
    }

    private static void WriteModel(Span<float> dest, in Matrix4x4 m)
    {
        dest[0] = m.M11; dest[1] = m.M12; dest[2] = m.M13; dest[3] = m.M14;
        dest[4] = m.M21; dest[5] = m.M22; dest[6] = m.M23; dest[7] = m.M24;
        dest[8] = m.M31; dest[9] = m.M32; dest[10] = m.M33; dest[11] = m.M34;
        dest[12] = m.M41; dest[13] = m.M42; dest[14] = m.M43; dest[15] = m.M44;
    }

    private static void WriteLamps(Span<int> dest, in RealmPaintRouter.InstLampGroup lamps)
    {
        dest[0] = lamps.L0; dest[1] = lamps.L1; dest[2] = lamps.L2; dest[3] = lamps.L3;
        dest[4] = lamps.L4; dest[5] = lamps.L5; dest[6] = lamps.L6; dest[7] = lamps.L7;
    }
}
