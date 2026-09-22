using System.Numerics;
using MacAC.Dat;
using MacAC.Mechanics.Sound;
using DRWSound =  MacAC.Dat.SoundTag;

namespace MacAC.Client.Sound;

public sealed class AmbientSoundDriver
{
    private readonly OpenAlSoundEngine _engine;
    private readonly DatWaveCache _stash;
    private readonly AmbienceScheduler _scheduler;
    private readonly AmbienceCollector _gatherer;
    private readonly IAudioDice _rng;
    private readonly List<AmbienceTrigger> _firings = [];

    private WorldRegion? _zone;
    private Func<uint, ushort[]?> _lbs = static _ => null;
    private uint _latestObjRefChamber;
    private Vector3 _listenerLocus;
    private double _clock;
    private bool _suspended;

    public AmbientSoundDriver(
        OpenAlSoundEngine engine,
        DatWaveCache cache,
        IAudioDice? rng = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _stash = cache ?? throw new ArgumentNullException(nameof(cache));
        _rng = rng ?? new AudioDice();
        _scheduler = new AmbienceScheduler(_rng);
        _gatherer = new AmbienceCollector(_scheduler);
    }

    public int InstanceCount => _scheduler.Instances.Count;

    public int QueuedCount => _scheduler.QueuedCount;

    public void SetupZone(WorldRegion region, Func<uint, ushort[]?> landblocks)
    {
        _zone = region ?? throw new ArgumentNullException(nameof(region));
        _lbs = landblocks ?? throw new ArgumentNullException(nameof(landblocks));
        _latestObjRefChamber = 0;
        _scheduler.Clear();
    }

    public void WatchListener(
        uint objRefChamberIdent,
        Vector3 locus,
        Vector3 lbOwnLocus,
        bool observedBeyond = false)
    {
        _listenerLocus = locus;
        if (_zone is null || objRefChamberIdent == _latestObjRefChamber)
            return;

        _latestObjRefChamber = objRefChamberIdent;

        if (IsInsideChamber(objRefChamberIdent) && !observedBeyond)
        {
            _scheduler.Clear();
            return;
        }

        _gatherer.Rebuild(
            _zone,
            (objRefChamberIdent >> 16 << 16) | 0xFFFFu,
            lbOwnLocus,
            _lbs,
            _clock);
    }

    public void Tick(double diffSecs)
    {
        if (diffSecs > 0)
            _clock += diffSecs;

        if (_suspended || !_engine.IsAvailable || _zone is null)
            return;

        _firings.Clear();
        _scheduler.Tick(_clock, _firings, _listenerLocus);
        Emit();
    }

    public void Suspend()
    {
        _suspended = true;
        HaltAll();
    }

    public void Reactivate() => _suspended = false;

    public void HaltAll()
    {
        _scheduler.Clear();
        _latestObjRefChamber = 0;
    }

    private void Emit()
    {
        foreach (AmbienceTrigger firing in _firings)
            Play(firing);
        _firings.Clear();
    }

    private void Play(in AmbienceTrigger firing)
    {
        SoundBook? chart = _stash.FetchSfxChart(firing.Instance.SfxChartDid);
        if (chart is null)
            return;

        var listing = SoundRecipes.Select(
            chart,
            (DRWSound)(uint)firing.Instance.Descriptor.Sound,
            _rng);
        if (listing is null)
            return;

        uint waveIdent = (uint)listing.ClipId;
        if (waveIdent is 0)
            return;

        PcmClip? wave = _stash.FetchWave(waveIdent);
        if (wave is null)
            return;

        float volume = firing.Volume * _engine.AmbientVolume;

        if (firing.Position is { } locus)
        {
            _engine.PlayAmbient3DWave(waveIdent, wave, locus, volume, listing.Priority);
            return;
        }

        _engine.PlayAmbientFromMiddle(waveIdent, wave, volume, listing.Priority);
    }

    private static bool IsInsideChamber(uint objRefChamberIdent) => (objRefChamberIdent & 0xFFFFu) >= 0x0100u;
}

public interface IAmbientCycleStage
{
    void PulseAmbient(float diffSecs);
}

public sealed class AmbientCycleStage(AmbientSoundDriver ambient, IAmbientWatcherOrigin listener) : IAmbientCycleStage
{
    private readonly AmbientSoundDriver _ambient = ambient ?? throw new ArgumentNullException(nameof(ambient));
    private readonly IAmbientWatcherOrigin _listener = listener ?? throw new ArgumentNullException(nameof(listener));

    public void PulseAmbient(float diffSecs)
    {
        if (_listener.TryFetchListener(out AmbientWatcherPosture posture))
        {
            _ambient.WatchListener(
                posture.ObjCellId,
                posture.Position,
                posture.LandblockLocalPosition,
                posture.SeenOutside);
        }
        _ambient.Tick(diffSecs);
    }
}

public readonly record struct AmbientWatcherPosture(
    uint ObjCellId,
    Vector3 Position,
    Vector3 LandblockLocalPosition,
    bool SeenOutside);

public interface IAmbientWatcherOrigin
{
    bool TryFetchListener(out AmbientWatcherPosture posture);
}

public sealed class AvatarAmbientListenerSource(
    MacAC.Sim.Play.SimAvatarLocomotionLedger player,
    Func<uint, Vector3, Vector3?>? insideLbOwn = null) : IAmbientWatcherOrigin
{
    private readonly MacAC.Sim.Play.SimAvatarLocomotionLedger _avatar = player ?? throw new ArgumentNullException(nameof(player));
    private readonly Func<uint, Vector3, Vector3?> _insideLbOwn = insideLbOwn ?? ((_, _) => null);

    public bool TryFetchListener(out AmbientWatcherPosture posture)
    {
        if (_avatar.Controller is { } driver)
        {
            var chamber = driver.CellPosition;
            uint objRefChamberIdent = driver.CellId;
            Vector3 lbOwn = chamber.Frame.Origin;
            bool observedBeyond = false;

            if ((objRefChamberIdent & 0xFFFFu) >= 0x0100u && _insideLbOwn(objRefChamberIdent, chamber.Frame.Origin)
                    is { } converted)
            {
                lbOwn = converted;
                observedBeyond = true;
            }

            posture = new AmbientWatcherPosture(
                objRefChamberIdent,
                driver.Position,
                lbOwn,
                observedBeyond);
            return true;
        }

        posture = default;
        return false;
    }
}
