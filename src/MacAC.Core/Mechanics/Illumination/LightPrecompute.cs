using System.Numerics;

namespace MacAC.Mechanics.Illumination;

public static class LightPrecompute
{
    private const float TwoPtSpan = 1.5f;   // LIGHT_POINT_RANGE + LIGHT_POINT_RANGE
    private const float EncloseBias = 0.5f;        // (2 · LIGHT_POINT_RANGE) − 1.0
    private const float NegligibleReach = 1e-4f;

    public static Vector3 PtContribution(Vector3 vtxRealmSpot, Vector3 vtxRealmNorm, LightEmitter lamp)
    {
        // Vertex-to-light, deliberately left un-normalised. Scalar arithmetic
        // on purpose: the bake must stay bit-identical to the retail values.
        float dx = lamp.RealmPosition.X - vtxRealmSpot.X;
        float dy = lamp.RealmPosition.Y - vtxRealmSpot.Y;
        float dz = lamp.RealmPosition.Z - vtxRealmSpot.Z;

        float distanceSq = dx * dx + dy * dy + dz * dz;
        float distance = MathF.Sqrt(distanceSq);
        float reach = lamp.Range;
        if (distance >= reach || reach <= NegligibleReach)
            return Vector3.Zero;

        float enclose = (1f / TwoPtSpan)
            * (vtxRealmNorm.X * dx + vtxRealmNorm.Y * dy + vtxRealmNorm.Z * dz + EncloseBias * distance);
        if (enclose <= 0f)
            return Vector3.Zero;

        float attenuation = distanceSq > 1f ? distanceSq * distance : distance;
        float scaling = (1f - distance / reach) * lamp.Intensity * (enclose / attenuation);

        Vector3 colour = lamp.TintLinear;
        return new Vector3(
            MathF.Min(scaling * colour.X, colour.X),
            MathF.Min(scaling * colour.Y, colour.Y),
            MathF.Min(scaling * colour.Z, colour.Z));
    }

    public static Vector3 CalculateVertTint(
        Vector3 vtxRealmSpot,
        Vector3 vtxRealmNorm,
        IReadOnlyList<LightEmitter> reaching)
    {
        float r = 0f, g = 0f, b = 0f;
        foreach (LightEmitter lamp in reaching)
        {
            if (!lamp.IsLit || lamp.Kind == LampFlavor.Directional)
                continue;
            Vector3 piece = PtContribution(vtxRealmSpot, vtxRealmNorm, lamp);
            r += piece.X;
            g += piece.Y;
            b += piece.Z;
        }
        return new Vector3(Math.Clamp(r, 0f, 1f), Math.Clamp(g, 0f, 1f), Math.Clamp(b, 0f, 1f));
    }
}
