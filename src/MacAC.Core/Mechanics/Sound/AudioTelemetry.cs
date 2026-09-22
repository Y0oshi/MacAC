namespace MacAC.Mechanics.Sound;

public static class AudioTelemetry
{
    public static bool SensorWireSfxListTurnedOn { get; set; } =
        Environment.GetEnvironmentVariable("MACAC_PROBE_SOUND_WIRE") == "1";
}
