using System.Globalization;
using MacAC.Mechanics.Realm;

namespace MacAC.Sim.Realm;

public sealed class SimRealmAmbienceLedger : ISimRealmAmbienceLens
{
    private const int NoCluster = -1;
    private const long NoDay = long.MinValue;
    private const int DaysPerYear =
        DerethDateMoment.DaysInAMonth * DerethDateMoment.MonthsInAYear;

    private readonly Action<string> _trace;
    private readonly DebugCycle _diag = new();
    private SimRealmAmbienceSpec? _spec;
    private long _engagedDayOrdinal = NoDay;
    private long _rev;

    public SimRealmAmbienceLedger(
        TimeProvider? momentSupplier = null,
        Action<string>? trace = null,
        Action<string>? momentSynchronizeProbe = null)
    {
        WorldTime = new WorldClock(
            SkyStateSource.Default(),
            new DerethAlmanac(),
            momentSupplier,
            momentSynchronizeProbe);
        Weather = new WeatherEngine();
        _trace = trace ?? (static _ => { });
    }

    public WorldClock WorldTime { get; }
    public WeatherEngine Weather { get; }
    public int EngagedDayClusterIdx { get; private set; } = NoCluster;
    public bool IsInitialized => _spec is not null;

    public SimRealmAmbienceCapture Snapshot
    {
        get
        {
            return new(
        _rev,
        IsInitialized,
        EngagedDayClusterIdx,
        _engagedDayOrdinal,
        Weather.Kind,
        Weather.Override);
        }
    }

    public SimRealmAmbienceHoldingCapture Ownership => CaptureOwnership();

    public SimRealmAmbienceHoldingCapture CaptureOwnership()
    {
        return new(
        IsInitialized,
        _spec?.DayGroups.Count ?? 0,
        EngagedDayClusterIdx >= 0 ? 1 : 0);
    }

    public void Bootstrap(SimRealmAmbienceSpec definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_spec is not null)
        {
            throw new InvalidOperationException(
                "The world environment is a one-shot Runtime lifetime owner");
        }

        _spec = definition;
        WorldTime.Calendar.AssignOriginShift(definition.OriginOffsetBeats);
        WorldTime.TickSize = 1.0;
        EngagedDayClusterIdx = NoCluster;
        _engagedDayOrdinal = NoDay;
        Bump();

        if (definition.DayGroups.Count > 0)
        {
            _trace(string.Create(
                CultureInfo.InvariantCulture,
                $"sky: loaded Region 0x13000000 — {definition.DayGroups.Count} day groups, "
                + $"SkyDesc.TickSize={definition.SrcBeatDims} (throttle, not rate), "
                + $"LightTickSize={definition.LightTickSize}"));
            RenewDayCluster();
        }

        WorldTime.SyncFromServer(DerethDateMoment.DayBeats / 16.0);
        Bump();
    }

    public void SynchronizeFromServer(double beats)
    {
        WorldTime.SyncFromServer(beats);
        Bump();
        if (IsInitialized)
            RenewDayCluster();
    }

    public SimAmbienceEffect EnactAdminEnvirons(uint editKind)
    {
        if (editKind <= 0x06u)
        {
            MechEnvironOverride fog = (MechEnvironOverride)editKind;
            Weather.Override = fog;
            Bump();
            _trace($"live: AdminEnvirons fog override = {fog}");
            return new SimAmbienceEffect(SimAmbienceEffectKind.FogOverride, editKind, fog);
        }

        if (!Enum.IsDefined(typeof(SimAmbienceAudioCue), (int)editKind))
        {
            _trace(
                $"live: AdminEnvirons sound cue = Unknown(0x{editKind:X2}) "
                + $"(0x{editKind:X2}) — audio binding pending");
            return new SimAmbienceEffect(SimAmbienceEffectKind.Unknown, editKind);
        }

        SimAmbienceAudioCue cue = (SimAmbienceAudioCue)(int)editKind;
        _trace(
            $"live: AdminEnvirons sound cue = {cue}Sound "
            + $"(0x{editKind:X2}) — audio binding pending");
        return new SimAmbienceEffect(SimAmbienceEffectKind.SoundCue, editKind, SoundCue: cue);
    }

    public void RenewDayCluster()
    {
        if (_spec is not { DayGroups.Count: > 0 } spec)
            return;

        double beats = WorldTime.InstantBeats;
        var calendar = WorldTime.Calendar;
        int year = calendar.AbsoluteYear(beats);
        int dayOfYear = calendar.DayOfYear(beats);
        long dayOrdinal = (long)year * DaysPerYear + dayOfYear;
        int ordinal = SkyDayGroupPicker.SelectIndex(
            spec.DayGroups.Count,
            year,
            DaysPerYear,
            dayOfYear,
            spec.ForcedDayClusterOrdinal);

        if (dayOrdinal == _engagedDayOrdinal && ordinal == EngagedDayClusterIdx)
            return;

        _engagedDayOrdinal = dayOrdinal;
        EngagedDayClusterIdx = ordinal;
        var cluster = spec.DayGroups[ordinal];
        WorldTime.AssignSupplier(cluster.Sky);
        Weather.AssignSortFromDayClusterLabel(cluster.Name);
        Bump();

        _trace(string.Create(
            CultureInfo.InvariantCulture,
            $"sky: PY{year} day{dayOfYear} → DayGroup[{ordinal}] \"{cluster.Name}\" "
            + $"(Chance={cluster.ChanceOfOccur:F2}, {cluster.SkyObjectCount} objects, "
            + $"{cluster.Sky.KeyframeTally} keyframes, weather={Weather.Kind})"));
    }

    public string CycleTimeOfDay()
    {
        float? ratio = _diag.UpcomingMoment();
        Bump();
        if (ratio is not { } val)
        {
            WorldTime.WipeDiagMoment();
            return "Time override cleared";
        }

        WorldTime.AssignDiagMoment(val);
        return string.Create(CultureInfo.InvariantCulture, $"Time override = {val:F2}");
    }

    public string CycleWeather()
    {
        WeatherKind sort = _diag.UpcomingWeather();
        Weather.ForceWeather(sort);
        Bump();
        return $"Weather = {sort}";
    }

    private void Bump() => _rev++;

    private sealed class DebugCycle
    {
        private static readonly float?[] Times = [null, 0.0f, 0.25f, 0.5f, 0.75f];
        private static readonly WeatherKind[] Weathers =
        [
            WeatherKind.Clear,
            WeatherKind.Overcast,
            WeatherKind.Rain,
            WeatherKind.Snow,
            WeatherKind.Storm,
        ];

        private int _moment;
        private int _weather;

        public float? UpcomingMoment() => Times[_moment = (_moment + 1) % Times.Length];

        public WeatherKind UpcomingWeather() => Weathers[_weather = (_weather + 1) % Weathers.Length];
    }
}
