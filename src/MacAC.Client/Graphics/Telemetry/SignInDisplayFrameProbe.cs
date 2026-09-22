namespace MacAC.Client.Graphics;

internal static class RenderDisplayTelemetry
{
    public static bool SensorSigninCycles { get; } =
        Environment.GetEnvironmentVariable("MACAC_PROBE_LOGIN_FRAMES") == "1";
}

internal sealed class SignInDisplayFrameProbe(
    Func<bool> tunnelSceneVisible,
    IRenderSignInStateSource login,
    Action<string> log) : IRenderFramePostTelemetryPhase
{
    private readonly Func<bool> _tunnelTableauShown = tunnelSceneVisible
            ?? throw new ArgumentNullException(nameof(tunnelSceneVisible));
    private readonly IRenderSignInStateSource _signin = login ?? throw new ArgumentNullException(nameof(login));
    private readonly Action<string> _trace = log ?? throw new ArgumentNullException(nameof(log));
    private long _cycle;
    private double _elapsedSeconds;
    private string? _previousClass;

    public void Process(RasterizeCycleFeed feed, RenderFrameVerdict verdict)
    {
        ++_cycle;
        _elapsedSeconds += feed.DeltaSeconds;

        bool realm = verdict.World.NormalWorldDrawn;
        bool tunnel = _tunnelTableauShown();
        bool cover = verdict.Presentation.PortalViewportDrawn;
        bool waiting = _signin.IsWaitingForSignin;

        string presentClass =
            realm ? "world"
            : tunnel ? "tunnel"
            : cover ? "black"
            : "void";

        if (presentClass == _previousClass)
            return;
        _previousClass = presentClass;
        _trace(
            $"[login-frames] frame={_cycle} t={_elapsedSeconds:F3}s "
            + $"present={presentClass} waiting={(waiting ? 1 : 0)} "
            + $"cover={(cover ? 1 : 0)} tunnel={(tunnel ? 1 : 0)} "
            + $"world={(realm ? 1 : 0)}");
    }
}
