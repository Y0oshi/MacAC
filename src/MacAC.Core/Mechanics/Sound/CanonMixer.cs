using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Sound;

public readonly record struct CanonVoiceMix(bool Play, int Decibels, int Pan);

/// <summary>The retail DirectSound-era gain and pan arithmetic.</summary>
public static class CanonMixer
{
    /// <summary>Inside this distance gain is flat at the authored volume, metres.</summary>
    public const float VolLowerGap = 5.0f;

    public const float VolLowerGapSq = 25.0f;

    public const int VolLowerDecibels = -50;

    public const float PanScaling = -15.0f;

    public const int PanDeadzoneMetres = 5;

    public const int VoiceTally = 16;

    private const float DegToRadians = 0.0174532924f;

    public static bool TryFetchAttenuation(float gapMetres, float volume, float masterVolume, out int decibels)
    {
        float gain = gapMetres < VolLowerGap
            ? volume
            : VolLowerGapSq * volume / (gapMetres * gapMetres);
        gain = MathF.Min(gain, 1.0f) * masterVolume;

        if (gain <= 0.0f || float.IsNaN(gain))
        {
            decibels = VolLowerDecibels;
            return false;
        }

        decibels = (int)MathF.Ceiling(20.0f * MathF.Log10(gain));
        if (decibels >= VolLowerDecibels)
            return true;
        decibels = VolLowerDecibels;
        return false;
    }

    public static float CompassBearingDeg(Vector3 from, Vector3 to) => ApproachMath.PlaceBearing(from, to);

    public static float StandardizeSignedDeg(float deg)
    {
        float wrapped = deg % 360.0f;
        return wrapped <= 180.0f ? wrapped : wrapped - 360.0f;
    }

    public static int FetchPan(float bearingSrcToListener, float listenerBearingDeg, float gapMetres, bool panningTurnedOn = true)
    {
        if (!panningTurnedOn || Math.Abs((int)gapMetres) < PanDeadzoneMetres)
            return 0;
        float diff = StandardizeSignedDeg(bearingSrcToListener - listenerBearingDeg);
        int pan = (int)(MathF.Sin(diff * DegToRadians) * PanScaling);
        return Math.Clamp(pan, (int)PanScaling, (int)-PanScaling);
    }

    public static CanonVoiceMix Mix(
        Vector3 listenerLocus,
        float listenerBearingDeg,
        Vector3 srcLocus,
        float volume,
        float masterVolume,
        bool panningTurnedOn = true)
    {
        float gap = Vector3.Distance(listenerLocus, srcLocus);
        int pan = FetchPan(CompassBearingDeg(srcLocus, listenerLocus), listenerBearingDeg, gap, panningTurnedOn);
        bool play = TryFetchAttenuation(gap, volume, masterVolume, out int decibels);
        return new CanonVoiceMix(play, decibels, pan);
    }

    public static float LinearGain(int decibels) => MathF.Pow(10.0f, decibels / 20.0f);

    public static float StereoLocusFromPan(int pan)
    {
        float ratio = MathF.Pow(10.0f, pan / 20.0f);
        float locus = 4.0f / MathF.PI * MathF.Atan(ratio) - 1.0f;
        return Math.Clamp(locus, -1.0f, 1.0f);
    }

    /// <summary>How far a source can be heard before it drops under −51 dB.</summary>
    public static float AudibleRadius(float volume, float masterVolume)
    {
        float scaling = volume * masterVolume;
        if (scaling <= 0f)
            return 0f;
        float floor = MathF.Pow(10.0f, -51.0f / 20.0f);
        return MathF.Sqrt(VolLowerGapSq * scaling / floor);
    }
}
