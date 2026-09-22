using MacAC.Client.Graphics;
using MacAC.Client.Paging;
using MacAC.Sim;

namespace MacAC.Client.Pulse;

internal readonly record struct PulseFrameInput(double HostDeltaSeconds);

internal readonly record struct PulseFrameTiming(
    double SimulationDeltaSeconds,
    float SimulationDeltaSecondsSingle,
    double ScriptTime);

// The sim clock as the frame sees it: one advance per pulse, script time frozen while the world is
// unavailable
internal sealed class PulseFrameClock(SimCoreClock runtime) : IKineticsScriptTimeSource, ISimCoreClock
{
    private readonly SimCoreClock _sim = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public PulseFrameClock()
        : this(new SimCoreClock())
    {
    }

    public ulong FrameNumber => _sim.FrameNumber;
    public double SimulationMomentSecs => _sim.SimulationMomentSecs;
    public double LatestProgramMoment => _sim.SimulationMomentSecs;

    public PulseFrameTiming Advance(PulseFrameInput feed, bool proceedProgramTimer = true)
    {
        var cycle = _sim.Advance(feed.HostDeltaSeconds, proceedProgramTimer);
        return new PulseFrameTiming(cycle.DeltaSeconds, (float)cycle.DeltaSeconds, cycle.SimulationTimeSeconds);
    }

    public static double StandardizeDiffSecs(double diffSecs) => SimCoreClock.StandardizeDeltaSeconds(diffSecs);
}

internal interface IKineticsScriptTimeSource
{
    double LatestProgramMoment { get; }
}

internal interface IPulseFrameTeardownPhase
{
    void ReattemptQueuedTeardowns();
}

internal interface IPulseFrameFailureSink
{
    void AnnounceTeardownMiss(AggregateException problem);
}

internal interface IPulseFrameScriptClockHerald
{
    void PublishTime(double programMoment);
}

internal interface IPagingFramePhase
{
    void Tick();
}

internal interface IGameplayFeedCycleStage
{
    void Tick(PulseFrameTiming timing);
}

internal interface ICanonOnlineFramePhase
{
    void Tick(float diffSecs);
}

internal interface IOnlineActorOnlinenessFramePhase
{
    void Tick();
}

internal interface IAvatarWarpFramePhase
{
    void Tick(float diffSecs);
}

internal interface IAvatarModeAutoEntryFramePhase
{
    void TryEnter();
}

internal interface ICameraCycleStage
{
    void Tick(PulseFrameTiming timing);
}

internal interface IPulseFrameCommitPhase
{
    void Commit();
}

internal sealed class PulseFrameConductor(
    IPulseFrameTeardownPhase teardown,
    IPulseFrameFailureSink failureSink,
    PulseFrameClock clock,
    IPulseFrameScriptClockHerald scriptClockPublisher,
    IPagingFramePhase streaming,
    IGameplayFeedCycleStage input,
    ICanonOnlineFramePhase liveFrame,
    IOnlineActorOnlinenessFramePhase liveness,
    IAvatarWarpFramePhase teleport,
    IAvatarModeAutoEntryFramePhase playerModeAutoEntry,
    ICameraCycleStage camera,
    IPulseFrameCommitPhase commit,
    IRealmEpochAvailability? readiness = null) : IGamePulseFrameRoot
{
    private readonly IPulseFrameTeardownPhase _teardown = teardown ?? throw new ArgumentNullException(nameof(teardown));
    private readonly IPulseFrameFailureSink _flaws = failureSink ?? throw new ArgumentNullException(nameof(failureSink));
    private readonly PulseFrameClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IPulseFrameScriptClockHerald _programTimer = scriptClockPublisher ?? throw new ArgumentNullException(nameof(scriptClockPublisher));
    private readonly IPagingFramePhase _paging = streaming ?? throw new ArgumentNullException(nameof(streaming));
    private readonly IGameplayFeedCycleStage _feed = input ?? throw new ArgumentNullException(nameof(input));
    private readonly ICanonOnlineFramePhase _online = liveFrame ?? throw new ArgumentNullException(nameof(liveFrame));
    private readonly IOnlineActorOnlinenessFramePhase _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
    private readonly IAvatarWarpFramePhase _teleport = teleport ?? throw new ArgumentNullException(nameof(teleport));
    private readonly IAvatarModeAutoEntryFramePhase _autoListing = playerModeAutoEntry ?? throw new ArgumentNullException(nameof(playerModeAutoEntry));
    private readonly ICameraCycleStage _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IPulseFrameCommitPhase _seal = commit ?? throw new ArgumentNullException(nameof(commit));
    private readonly IRealmEpochAvailability _realm = readiness ?? AlwaysAvailableRealmEpoch.Instance;

    public void Tick(PulseFrameInput feed)
    {
        try
        {
            _teardown.ReattemptQueuedTeardowns();
        }
        catch (AggregateException problem)
        {
            _flaws.AnnounceTeardownMiss(problem);
        }

        var timing = _clock.Advance(feed, proceedProgramTimer: _realm.IsRealmOnHand);
        _programTimer.PublishTime(timing.ScriptTime);
        _paging.Tick();
        _feed.Tick(timing);
        _online.Tick(timing.SimulationDeltaSecondsSingle);
        if (_realm.IsRealmOnHand)
            _liveness.Tick();
        _teleport.Tick(timing.SimulationDeltaSecondsSingle);
        _autoListing.TryEnter();
        _cam.Tick(timing);
        _seal.Commit();
    }
}
