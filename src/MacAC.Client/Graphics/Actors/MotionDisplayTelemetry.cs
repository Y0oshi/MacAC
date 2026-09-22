namespace MacAC.Client.Graphics;

// Startup-resolved diagnostic gates for final PartArray presentation
internal sealed record MotionDisplayTelemetry(
    bool RemoteVelocityEnabled,
    bool DumpMotionEnabled,
    Func<double> Now)
{
    public static MotionDisplayTelemetry FromEnvironment()
    {
        return new(
        RemoteVelocityEnabled:
            Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1",
        DumpMotionEnabled:
            Environment.GetEnvironmentVariable("MACAC_DUMP_MOTION") == "1",
        Now: static () =>
            (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds);
    }

    public static MotionDisplayTelemetry Disabled { get; } = new(
        false,
        false,
        static () => 0d);
}
