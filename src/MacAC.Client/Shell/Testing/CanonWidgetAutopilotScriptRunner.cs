using MacAC.Cockpit.Input;

namespace MacAC.Client.Shell.Testing;

public enum CanonWidgetAutopilotCheckpointStatus
{
    Pending,
    Succeeded,
    Failed,
    Cancelled,
}

public interface ICanonWidgetAutopilotCheckpoint
{
    int Sequence { get; }
    string Name { get; }
    CanonWidgetAutopilotCheckpointStatus Status { get; }
    string? Error { get; }
}

public enum CanonWidgetAutopilotRenderPackPhase
{
    Retail,
    CandidatePending,
    Active,
    FailedToRetail,
}

public readonly record struct CanonWidgetAutopilotRenderPackStatus(
    CanonWidgetAutopilotRenderPackPhase State,
    string PackId,
    string PresetId,
    long ActivationGeneration,
    string? FailureReason)
{
    public static CanonWidgetAutopilotRenderPackStatus Retail { get; } = new(
        CanonWidgetAutopilotRenderPackPhase.Retail,
        "retail",
        "off",
        ActivationGeneration: 0,
        FailureReason: null);
}

public interface ICanonWidgetAutopilotEngine
{
    bool IsRealmReady { get; }
    bool IsRealmViewRectShown { get; }
    int GatewayMaterializationCount { get; }
    int RasterizeBundlePerformanceSpecimenTally => 0;
    bool RasterizeBundleFailedToCanon => false;
    CanonWidgetAutopilotRenderPackStatus RasterizeBundleCondition =>
        CanonWidgetAutopilotRenderPackStatus.Retail;
    int FramebufferWidth => 0;
    int FramebufferHeight => 0;
    bool TryPickRasterizeBundle(string presetIdent, out string problem)
    {
        problem = "render-pack selection automation is unavailable";
        return false;
    }
    bool TryDeactivateRasterizeBundle(out string problem)
    {
        problem = "render-pack selection automation is unavailable";
        return false;
    }
    bool TryReenableRasterizeBundle(out string problem)
    {
        problem = "render-pack selection automation is unavailable";
        return false;
    }
    bool TryRescaleFramebuffer(int width, int height, out string problem)
    {
        problem = "framebuffer resize automation is unavailable";
        return false;
    }
    bool TryRestartRasterizeBundlePerformance(out string problem)
    {
        problem = "render-pack performance automation is unavailable";
        return false;
    }
    bool TryReqClientShut(out string problem)
    {
        problem = "client-close automation is unavailable";
        return false;
    }
    bool TryReqCheckpoint(
        string label,
        out ICanonWidgetAutopilotCheckpoint? checkpoint,
        out string problem);
    void AbortCheckpoint(ICanonWidgetAutopilotCheckpoint checkpoint);
    bool TryReqScreenshot(string label, out string problem);
    bool IsScreenshotDone(string label);
    bool TryIsAutomationSignalPublished(
        string label,
        out bool published,
        out string problem)
    {
        published = false;
        problem = "automation signals require MACAC_AUTOMATION_ARTIFACT_DIR";
        return false;
    }
}

public sealed partial class CanonWidgetAutopilotScriptRunner : IDisposable
{
    private readonly CanonWidgetAutopilotProbe _sensor;

    private readonly Action<string> _trace;

    private readonly Action<string>? _submitDirective;

    private readonly Func<FeedAct, bool>? _pressFeed;

    private readonly Func<FeedAct, bool, bool>? _setFeedPinned;

    private readonly ICanonWidgetAutopilotEngine? _runtime;

    private readonly Action<float, float>? _fifoPointerGazeDiff;

    private readonly List<ScriptDirective> _commands = [];

    private readonly HashSet<FeedAct> _pinnedFeeds = [];

    private readonly bool _printOnBegin;

    private readonly string? _pullProblem;

    private ICanonWidgetAutopilotCheckpoint? _checkpoint;

    private int _checkpointDirectiveOrdinal = -1;

    private int _ordinal;

    private int _engagedOrdinal = -1;

    private double _directivePassedMsec;

    private double _directivePassedCompensationMsec;

    private bool _begun;
    private bool _destroyed;

    public CanonWidgetAutopilotScriptRunner(
        CanonWidgetAutopilotProbe probe,
        string? programTrail,
        bool printOnBegin,
        Action<string>? trace = null,
        Action<string>? submitDirective = null,
        Func<FeedAct, bool>? pressFeed = null,
        Func<FeedAct, bool, bool>? setFeedPinned = null,
        ICanonWidgetAutopilotEngine? core = null,
        Action<float, float>? fifoPointerGazeDiff = null)
    {
        _sensor = probe ?? throw new ArgumentNullException(nameof(probe));
        _trace = trace ?? (_ => { });
        _submitDirective = submitDirective;
        _pressFeed = pressFeed;
        _setFeedPinned = setFeedPinned;
        _runtime = core;
        _fifoPointerGazeDiff = fifoPointerGazeDiff;
        _printOnBegin = printOnBegin;

        if (!string.IsNullOrWhiteSpace(programTrail))
        {
            try
            {
                int strokeNumber = 0;
                foreach (var raw in File.ReadAllLines(programTrail))
                {
                    ++strokeNumber;
                    string stroke = StripComment(raw).Trim();
                    if (stroke.Length is 0) continue;
                    _commands.Add(new ScriptDirective(strokeNumber, stroke, Split(stroke)));
                }
            }
            catch (Exception exc)
            {
                _pullProblem = $"failed to load UI probe script '{programTrail}': {exc.Message}";
            }
        }
    }

    private readonly record struct ScriptDirective(int LineNumber, string Text, string[] Parts);
}
