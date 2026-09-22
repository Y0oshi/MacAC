namespace MacAC.Client.Sound;

public sealed class RealmAudioSessionTurnstile(
    OpenAlSoundEngine engine,
    AmbientSoundDriver? ambient)
{
    private readonly OpenAlSoundEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly AmbientSoundDriver? _ambient = ambient;

    public void SuspendForSessRestart()
    {
        _engine.SuspendRealmSound();
        _ambient?.HaltAll();
    }

    public void ReactivateForRealmListing() => _engine.ReactivateRealmSound();
}
