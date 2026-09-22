using System.Numerics;
using System.Runtime.InteropServices;
using MacAC.Mechanics.Realm;

namespace MacAC.Mechanics.Illumination;

/// <summary>One light as the shader sees it: four vec4s, 64 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PackedLight
{
    public Vector4 SpotAndSort;
    public Vector4 DirectionAndSpan;
    public Vector4 TintAndIntensity;
    public Vector4 ConeAngleEtc;

    public static PackedLight Empty => default;

    public static PackedLight FromSrc(LightEmitter emitter)
    {
        return new()
        {
            SpotAndSort = new Vector4(emitter.RealmPosition, (int)emitter.Kind),
            DirectionAndSpan = new Vector4(emitter.RealmAhead, emitter.Range),
            TintAndIntensity = new Vector4(emitter.TintLinear, emitter.Intensity),
            ConeAngleEtc = new Vector4(emitter.ConeAngle, 0f, 0f, 0f),
        };
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SceneLightBlock
{
    private const int LampSockets = 8;

    public PackedLight Light0;
    public PackedLight Light1;
    public PackedLight Light2;
    public PackedLight Light3;
    public PackedLight Light4;
    public PackedLight Light5;
    public PackedLight Light6;
    public PackedLight Light7;

    public Vector4 ChamberAmbient;

    /// <summary>x = fog start, y = fog end, z = lightning flash, w = fog mode.</summary>
    public Vector4 FogParams;

    public Vector4 FogColor;

    public Vector4 CamAndMoment;

    public const int SizeInBytes = LampSockets * 64 + 4 * 16;   // 576

    public const int MappingPt = 1;

    public static SceneLightBlock Build(
        LightKeeper lamps,
        in AtmosphereFrame atmo,
        Vector3 camRealmSpot,
        float dayRatio)
    {
        ArgumentNullException.ThrowIfNull(lamps);

        SceneLightBlock chunk = new SceneLightBlock();
        var engaged = lamps.Active;
        int tally = Math.Min(engaged.Length, LampSockets);

        var sockets = MemoryMarshal.CreateSpan(ref chunk.Light0, LampSockets);
        for (int idx = 0; idx < tally; ++idx)
        {
            if (engaged[idx] is { } lamp)
                sockets[idx] = PackedLight.FromSrc(lamp);
        }

        chunk.ChamberAmbient = new Vector4(lamps.LatestAmbient.AmbientColor, tally);
        chunk.FogParams = new Vector4(atmo.FogStart, atmo.FogEnd, atmo.LightningFlash, (int)atmo.FogMode);
        chunk.FogColor = new Vector4(atmo.FogColor, 0f);
        chunk.CamAndMoment = new Vector4(camRealmSpot, dayRatio);
        return chunk;
    }
}
