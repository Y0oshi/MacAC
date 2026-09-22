using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Sound;
using DRWSound =  MacAC.Dat.SoundTag;

namespace MacAC.Client.Sound;

public sealed class SoundTapDrain(
    OpenAlSoundEngine engine,
    DatWaveCache cache,
    IActorSoundChart entitySoundTables,
    IAudioDice? rng = null) : IAnimHookTap
{
    private readonly OpenAlSoundEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly DatWaveCache _stash = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IActorSoundChart _actorSfxCharts = entitySoundTables ?? throw new ArgumentNullException(nameof(entitySoundTables));
    private readonly IAudioDice _rng = rng ?? new AudioDice();

    public void OnTap(uint actorIdent, Vector3 actorRealmLocus, Cue tap)
    {
        if (!_engine.IsAvailable) return;

        switch (tap)
        {
            case SoundCue s:
                Play(actorIdent, actorRealmLocus, (uint)s.WaveId, volume: 1f, precedence: 1f);
                break;

            case SoundTableCue st:
                PlayFromSfxChart(actorIdent, actorRealmLocus, st.Sound);
                break;

            case SoundTweakedCue stw:
                Play(actorIdent, actorRealmLocus,
                    waveIdent: (uint)stw.WaveId,
                    volume: stw.Volume > 0 ? stw.Volume : 1f,
                    precedence: stw.Priority);
                break;

        }
    }

    public void PlaySrvSfx(
        uint actorIdent,
        Vector3 realmLocus,
        uint sfxKind,
        float wireVolume)
    {
        if (!_engine.IsAvailable) return;

        uint chartIdent = _actorSfxCharts.FetchSfxChartIdent(actorIdent);
        if (chartIdent is 0)
        {
            WireSensor(actorIdent, sfxKind, "no-sound-table");
            return;
        }

        SoundBook? chart = _stash.FetchSfxChart(chartIdent);
        if (chart is null)
        {
            WireSensor(actorIdent, sfxKind, $"table-0x{chartIdent:X8}-unloadable");
            return;
        }

        SoundChoice? listing = SoundRecipes.Select(chart, (DRWSound)sfxKind, _rng);
        if (listing is null)
        {
            WireSensor(actorIdent, sfxKind, "slot-missing-or-gate-silence");
            return;
        }

        WireSensor(actorIdent, sfxKind,
            $"play wave=0x{(uint)listing.ClipId:X8} pos=({realmLocus.X:F0},{realmLocus.Y:F0},{realmLocus.Z:F0})");
        Play(
            actorIdent, realmLocus,
            waveIdent: (uint)listing.ClipId,
            volume: wireVolume,
            precedence: listing.Priority);
    }

    public void OnWidgetTap(uint actorIdent, Cue tap)
    {
        if (!_engine.IsAvailable) return;

        switch (tap)
        {
            case SoundCue s:
                PlayWidget((uint)s.WaveId, volume: 1f);
                break;

            case SoundTableCue st:
                uint chartIdent = _actorSfxCharts.FetchSfxChartIdent(actorIdent);
                if (chartIdent is 0) return;
                SoundBook? chart = _stash.FetchSfxChart(chartIdent);
                if (chart is null) return;
                SoundChoice? listing = SoundRecipes.Select(chart, st.Sound, _rng);
                if (listing is null) return;
                PlayWidget((uint)listing.ClipId, listing.Volume);
                break;

            case SoundTweakedCue stw:
                PlayWidget(
                    (uint)stw.WaveId,
                    stw.Volume > 0 ? stw.Volume : 1f);
                break;
        }
    }

    private static void WireSensor(uint actorIdent, uint sfxKind, string verdict)
    {
        if (!AudioTelemetry.SensorWireSfxListTurnedOn) return;
        Console.WriteLine(FormattableString.Invariant(
            $"[sound-wire] local=0x{actorIdent:X8} slot=0x{sfxKind:X2} {verdict}"));
    }

    private void PlayWidget(uint waveIdent, float volume)
    {
        if (waveIdent is 0) return;
        PcmClip? wave = _stash.FetchWave(waveIdent);
        if (wave is null) return;
        _engine.PlayWidgetWave(waveIdent, wave, volume);
    }

    private void PlayFromSfxChart(
        uint actorIdent, Vector3 realmSpot, DRWSound sfx,
        float volumeMult = 1f)
    {
        uint chartIdent = _actorSfxCharts.FetchSfxChartIdent(actorIdent);
        if (chartIdent is 0) return;

        SoundBook? chart = _stash.FetchSfxChart(chartIdent);
        if (chart is null) return;

        SoundChoice? listing = SoundRecipes.Select(chart, sfx, _rng);
        if (listing is null) return;

        Play(
            actorIdent, realmSpot,
            waveIdent: (uint)listing.ClipId,
            volume: listing.Volume * volumeMult,
            precedence: listing.Priority);
    }

    private void Play(uint actorIdent, Vector3 realmSpot, uint waveIdent,
        float volume, float precedence)
    {
        if (waveIdent is 0) return;
        PcmClip? wave = _stash.FetchWave(waveIdent);
        if (wave is null) return;
        _engine.Play3DWave(
            actorIdent,
            waveIdent,
            wave,
            realmSpot,
            volume,
            precedence);
    }
}

public interface IActorSoundChart
{
    uint FetchSfxChartIdent(uint actorIdent);
}

public sealed class DictionaryActorSoundChart : IActorSoundChart
{
    private readonly Dictionary<uint, uint> _chart = [];

    public void Set(uint actorIdent, uint sfxChartIdent) => _chart[actorIdent] = sfxChartIdent;
    public void Remove(uint actorIdent) => _chart.Remove(actorIdent);

    public uint FetchSfxChartIdent(uint actorIdent) =>
        _chart.TryGetValue(actorIdent, out var ident) ? ident : 0;
}
