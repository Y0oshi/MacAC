using MacAC.Mechanics.Realm;

namespace MacAC.Sim.Realm;

/// <summary>One sky "day group" from the region file: its odds, object count and keyframes.</summary>
public sealed record SimRealmDayGroupSpec(
    string Name,
    float ChanceOfOccur,
    int SkyObjectCount,
    SkyStateSource Sky)
{
    public string Name { get; } = Name ?? string.Empty;
    public SkyStateSource Sky { get; } =
        Sky ?? throw new ArgumentNullException(nameof(Sky));
}

/// <summary>Everything the ambience ledger needs from the region to run the sky.</summary>
public sealed class SimRealmAmbienceSpec
{
    private readonly SimRealmDayGroupSpec[] _clusters;

    public SimRealmAmbienceSpec(
        double originOffsetTicks,
        double sourceTickSize,
        double lightTickSize,
        IEnumerable<SimRealmDayGroupSpec>? dayClusters,
        int? forcedDayClusterOrdinal = null)
    {
        OriginOffsetBeats = Finite(originOffsetTicks, nameof(originOffsetTicks));
        SrcBeatDims = Finite(sourceTickSize, nameof(sourceTickSize));
        LightTickSize = Finite(lightTickSize, nameof(lightTickSize));
        _clusters = dayClusters?.ToArray() ?? [];
        ForcedDayClusterOrdinal =
            forcedDayClusterOrdinal is { } forced && forced >= 0 && forced < _clusters.Length
                ? forced
                : null;
    }

    public double OriginOffsetBeats { get; }
    public double SrcBeatDims { get; }
    public double LightTickSize { get; }
    public IReadOnlyList<SimRealmDayGroupSpec> DayGroups => _clusters;
    public int? ForcedDayClusterOrdinal { get; }

    private static double Finite(double val, string parameterLabel)
    {
        return double.IsFinite(val) ? val : throw new ArgumentOutOfRangeException(parameterLabel);
    }
}

public enum SimAmbienceEffectKind
{
    Unknown,
    FogOverride,
    SoundCue,
}

public enum SimAmbienceAudioCue
{
    Roar = 0x65,
    Bell = 0x66,
    Chant1 = 0x67,
    Chant2 = 0x68,
    DarkWhispers1 = 0x69,
    DarkWhispers2 = 0x6A,
    DarkLaugh = 0x6B,
    DarkWind = 0x6C,
    DarkSpeech = 0x6D,
    Drums = 0x6E,
    GhostSpeak = 0x6F,
    Breathing = 0x70,
    Howl = 0x71,
    LostSouls = 0x72,
    Squeal = 0x75,
    Thunder1 = 0x76,
    Thunder2 = 0x77,
    Thunder3 = 0x78,
    Thunder4 = 0x79,
    Thunder5 = 0x7A,
    Thunder6 = 0x7B,
}

public readonly record struct SimAmbienceEffect(
    SimAmbienceEffectKind Kind,
    uint RawValue,
    MechEnvironOverride FogOverride = MechEnvironOverride.None,
    SimAmbienceAudioCue? SoundCue = null);

public readonly record struct SimRealmAmbienceCapture(
    long Revision,
    bool IsInitialized,
    int ActiveDayGroupIndex,
    long ActiveDayIndex,
    WeatherKind Weather,
    MechEnvironOverride EnvironOverride);

public readonly record struct SimRealmAmbienceHoldingCapture(
    bool IsInitialized,
    int DayGroupDefinitionCount,
    int ActiveDayGroupCount);

public interface ISimRealmAmbienceLens
{
    SimRealmAmbienceCapture Snapshot { get; }
    SimRealmAmbienceHoldingCapture Ownership { get; }
}
