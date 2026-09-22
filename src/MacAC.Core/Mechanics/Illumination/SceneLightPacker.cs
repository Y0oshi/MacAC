namespace MacAC.Mechanics.Illumination;

public static class SceneLightPacker
{
    public const int FloatsPerLamp = 16;

    private const int GrowthSlack = FloatsPerLamp * 16;

    public static int Pack(IReadOnlyList<LightEmitter>? capture, ref float[] buf)
    {
        int tally = capture?.Count ?? 0;
        int needed = Math.Max(tally, 1) * FloatsPerLamp;
        if (buf.Length < needed)
            buf = new float[needed + GrowthSlack];
        Array.Clear(buf, 0, needed);

        for (int idx = 0; idx < tally; ++idx)
        {
            var lamp = capture![idx];
            Span<float> socket = buf.AsSpan(idx * FloatsPerLamp, FloatsPerLamp);

            socket[0] = lamp.RealmPosition.X;
            socket[1] = lamp.RealmPosition.Y;
            socket[2] = lamp.RealmPosition.Z;
            socket[3] = (int)lamp.Kind;

            socket[4] = lamp.RealmAhead.X;
            socket[5] = lamp.RealmAhead.Y;
            socket[6] = lamp.RealmAhead.Z;
            // Already scaled by the static/dynamic reach factor in LightInfoReader.
            socket[7] = lamp.Range;

            socket[8] = lamp.TintLinear.X;
            socket[9] = lamp.TintLinear.Y;
            socket[10] = lamp.TintLinear.Z;
            socket[11] = lamp.Intensity;

            socket[12] = lamp.ConeAngle;
            // 1 = D3D 1/d attenuation, 0 = static 1/d³
            socket[13] = lamp.IsDynamic ? 1f : 0f;
        }
        return tally;
    }
}
