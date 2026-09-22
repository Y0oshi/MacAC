namespace MacAC.Cockpit.Panels.Settings;

/// <summary><c>PlaySoundOnlyWhenActive</c> is stored only; there is no window-focus mute yet.</summary>
public sealed record SoundPrefs(
    float Master,
    float Sfx,
    float Ambient,
    int SoundFeatures = 0,
    bool SfxEnabled = true,
    bool AmbientEnabled = true,
    bool InterfaceEnabled = true,
    float InterfaceVolume = 1.0f,
    bool PlaySoundOnlyWhenActive = true)
{
    public static SoundPrefs Default { get; } = new(Master: 1.0f, Sfx: 1.0f, Ambient: 1.0f);
}
