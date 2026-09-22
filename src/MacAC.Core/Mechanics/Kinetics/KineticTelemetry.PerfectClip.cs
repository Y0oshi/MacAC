namespace MacAC.Mechanics.Kinetics;

public static partial class KineticTelemetry
{
    private static int _orbToiCamOnlineTally;
    private static int _orbToiUnverifiedTally;
    private static int _orbToiUnverifiedAnnounced;
    private static int _cylToiCamOnlineTally;
    private static int _cylToiUnverifiedTally;
    private static int _cylToiUnverifiedAnnounced;

    public static int OrbToiCamOnlineTally => _orbToiCamOnlineTally;
    public static int OrbToiUnverifiedTally => _orbToiUnverifiedTally;
    public static int CylToiCamOnlineTally => _cylToiCamOnlineTally;
    public static int CylToiUnverifiedTally => _cylToiUnverifiedTally;

    public static void CaptureOrbPerfectClipRearReach(bool carrierIsBeholder)
    {
        CapturePerfectClipRearReachCore("Sphere", carrierIsBeholder, ref _orbToiCamOnlineTally, ref _orbToiUnverifiedTally, ref _orbToiUnverifiedAnnounced);
    }

    public static void CaptureCylPerfectClipRearReach(bool carrierIsBeholder)
    {
        CapturePerfectClipRearReachCore("Cyl", carrierIsBeholder, ref _cylToiCamOnlineTally, ref _cylToiUnverifiedTally, ref _cylToiUnverifiedAnnounced);
    }

    public static void RestartPerfectClipRearGuardForTest()
    {
        _orbToiCamOnlineTally = 0;
        _orbToiUnverifiedTally = 0;
        _orbToiUnverifiedAnnounced = 0;
        RestartPerfectClipRearGuardForTestRest();
    }

    private static void RestartPerfectClipRearGuardForTestRest()
    {
        _cylToiCamOnlineTally = 0;
        _cylToiUnverifiedTally = 0;
        _cylToiUnverifiedAnnounced = 0;
    }

    private static void CapturePerfectClipRearReachCore(
        string rear, bool carrierIsBeholder,
        ref int camOnlineTally, ref int unverifiedTally, ref int unverifiedAnnounced)
    {
        if (carrierIsBeholder)
        {
            Interlocked.Increment(ref camOnlineTally);
            return;
        }

        Interlocked.Increment(ref unverifiedTally);
        if (Interlocked.Exchange(ref unverifiedAnnounced, 1) is not 0)
            return;

        Console.WriteLine(
            $"[perfectclip-tail] UNVERIFIED mover reached the {rear} PerfectClip "
            + "time-of-impact tail; the verified-reachable population is the camera / "
            + "IsViewer only. A non-viewer mover just executed this path and its "
            + "reachability was never re-verified, so do not trust the result without "
            + "checking it");
    }
}
