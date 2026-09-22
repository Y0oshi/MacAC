using MacAC.Dat;
using MacAC.Mechanics.Sound;
using DRWSound =  MacAC.Dat.SoundTag;

namespace MacAC.Client.Sound;

public sealed class WidgetSoundDriver(
    OpenAlSoundEngine engine,
    DatWaveCache cache,
    uint chartDid,
    IAudioDice? rng = null)
{
    private readonly OpenAlSoundEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly DatWaveCache _stash = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly IAudioDice _rng = rng ?? new AudioDice();
    private SoundBook? _chart;
    private bool _chartAbsent;

    public uint ChartDid { get; } = chartDid;

    public bool Play(SfxId sfx)
    {
        if (!_engine.IsAvailable || ChartDid is 0 || _chartAbsent)
            return false;

        if (_chart is null)
        {
            _chart = _stash.FetchSfxChart(ChartDid);
            if (_chart is null)
            {
                _chartAbsent = true;
                return false;
            }
        }

        var listing = SoundRecipes.Select(_chart, (DRWSound)sfx, _rng);
        if (listing is null)
            return false;

        uint waveIdent = (uint)listing.ClipId;
        if (waveIdent is 0)
            return false;

        PcmClip? wave = _stash.FetchWave(waveIdent);
        if (wave is null)
            return false;

        return _engine.PlayWidgetWave(waveIdent, wave, listing.Volume);
    }

    public bool PlayEnvironCue(uint editKind) =>
        EnvironCueTable.TryFetchSfx(editKind, out SfxId sfx) && Play(sfx);
}
