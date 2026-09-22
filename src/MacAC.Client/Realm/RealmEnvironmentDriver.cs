using MacAC.Dat;
using MacAC.Client.Graphics;
using MacAC.Mechanics.Data;
using MacAC.Mechanics.Realm;
using MacAC.Sim.Realm;

namespace MacAC.Client.Realm;

internal sealed class RealmEnvironmentDriver : IRealmStageHeavensStateSource
{
    private readonly Action<string> _trace;
    private readonly int? _forcedDayClusterOrdinal;
    private MountedSkyDesc? _loadedSkyDesc;

    public RealmEnvironmentDriver(Action<string>? trace = null)
        : this(
            new SimRealmAmbienceLedger(trace: trace),
            forcedDayClusterOrdinal: null,
            trace)
    {
    }

    internal RealmEnvironmentDriver(
        SimRealmAmbienceLedger runtime,
        int? forcedDayClusterOrdinal = null,
        Action<string>? trace = null,
        float? pinnedDayRatio = null)
    {
        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _forcedDayClusterOrdinal = forcedDayClusterOrdinal;
        _trace = trace ?? (_ => { });
        Runtime.WorldTime.PinnedDayRatio = pinnedDayRatio;
        if (pinnedDayRatio.HasValue)
            _trace($"sky: world time PINNED at day fraction {pinnedDayRatio.Value:F4}");
    }

    public SimRealmAmbienceLedger Runtime { get; }
    public WorldClock WorldMoment => Runtime.WorldTime;
    public WeatherEngine Weather => Runtime.Weather;

    public DayGroupRow? EngagedDayGroup
    {
        get
        {
            int ordinal = Runtime.EngagedDayClusterIdx;
            return _loadedSkyDesc is not null
                && ordinal >= 0
                && ordinal < _loadedSkyDesc.DayGroups.Count
                    ? _loadedSkyDesc.DayGroups[ordinal]
                    : null;
        }
    }

    public int EngagedDayClusterOrdinal => Runtime.EngagedDayClusterIdx;

    public float DayFraction => (float)WorldMoment.DayFraction;

    public void Prime(WorldRegion zone, IDatRecordSource? datFiles = null)
    {
        ArgumentNullException.ThrowIfNull(zone);
        Bootstrap(
            SkyDescReader.PullFromZone(zone, datFiles),
            zone.Time?.ZeroTimeOfYear);
    }

    public void SynchronizeFromSrv(double beats) =>
        Runtime.SynchronizeFromServer(beats);

    public void ImposeAdminEnvirons(uint environEditKind)
    {
        var fx = Runtime.EnactAdminEnvirons(environEditKind);
        if (fx.Kind is SimAmbienceEffectKind.SoundCue)
            EnvironSfxDrain?.Invoke(environEditKind);
    }

    public Action<uint>? EnvironSfxDrain { get; set; }

    public void RefreshSkyForCurrentDay() => Runtime.RenewDayCluster();

    public string CycleTimeOfDay() => Runtime.CycleTimeOfDay();

    public string CycleWeather() => Runtime.CycleWeather();

    internal void Bootstrap(
        MountedSkyDesc? fetchedHeavensDsc,
        double? zeroMomentOfYear)
    {
        if (Runtime.IsInitialized)
        {
            throw new InvalidOperationException(
                "The world environment is a one-shot Runtime lifetime owner");
        }

        double origin = zeroMomentOfYear
            ?? DerethDateMoment.DayRatioOriginShiftBeats;
        if (zeroMomentOfYear.HasValue)
        {
            _trace(
                $"sky: GameTime ZeroTimeOfYear={zeroMomentOfYear.Value} "
                + $"(was default {DerethDateMoment.DayRatioOriginShiftBeats})");
        }

        SimRealmDayGroupSpec[] clusters =
            fetchedHeavensDsc?.DayGroups.Select(
                cluster => new SimRealmDayGroupSpec(
                    cluster.Name,
                    cluster.ChanceOfOccur,
                    cluster.SkyObjects.Count,
                    new SkyStateSource(
                        cluster.HeavensTimes.Select(val => val.Keyframe).ToList())))
            .ToArray()
            ?? [];

        SimRealmAmbienceSpec definition = new SimRealmAmbienceSpec(
            origin,
            fetchedHeavensDsc?.TickSize ?? 1.0,
            fetchedHeavensDsc?.LightTickSize ?? 1.0,
            clusters,
            _forcedDayClusterOrdinal);

        Runtime.Bootstrap(definition);
        _loadedSkyDesc = fetchedHeavensDsc;
    }
}
