namespace MacAC.Mechanics.Sound;

/// <summary>Decoded PCM ready for a device buffer.</summary>
public sealed class PcmClip
{
    public int LaneTally { get; init; }

    public int SpecimenRate { get; init; }

    /// <summary>8 or 16.</summary>
    public int BitsetPerSpecimen { get; init; }

    public byte[] PcmOctets { get; init; } = [];

    public TimeSpan Duration { get; init; }
}

/// <summary>What the game needs from an audio device.</summary>
public interface IAudioBackend : IDisposable
{
    float MasterVolume { get; set; }

    float SfxVolume { get; set; }

    float AmbientVolume { get; set; }

    void AssignListener(float spotX, float spotY, float spotZ, float bearingDeg);

    /// <summary>A flat UI sound with no falloff.</summary>
    void PlayWidget(SfxId ident);

    /// <summary>A positional sound in world space.</summary>
    void Play3D(SfxId ident, float x, float y, float z);
}
