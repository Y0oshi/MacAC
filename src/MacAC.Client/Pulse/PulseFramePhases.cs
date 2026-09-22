using System.Diagnostics;
using MacAC.Client.Controls;
using MacAC.Client.Realm;
using MacAC.Mechanics.Effects;

namespace MacAC.Client.Pulse;

// Thin phase bridges: each wraps one production object behind the
// conductor's phase interface so the frame order is testable with fakes.

internal sealed class OnlineActorTeardownFramePhase(OnlineActorCore runtime) : IPulseFrameTeardownPhase
{
    private readonly OnlineActorCore _actors = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public void ReattemptQueuedTeardowns() => _actors.ReattemptPendingTeardowns();
}

internal sealed class ConsolePulseFrameFailureSink : IPulseFrameFailureSink
{
    public void AnnounceTeardownMiss(AggregateException problem) =>
        Console.Error.WriteLine($"[live-entity-teardown] {problem}");
}

internal sealed class KineticsScriptClockHerald(KineticScriptRunner runner) : IPulseFrameScriptClockHerald
{
    private readonly KineticScriptRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    public void PublishTime(double programMoment) => _runner.PublishTime(programMoment);
}

internal interface IClientMonotonicMomentOrigin
{
    double Instant { get; }
}

internal sealed class StopwatchClientMonotonicMomentOrigin : IClientMonotonicMomentOrigin
{
    public double Instant => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}

internal sealed class OnlineActorOnlinenessFramePhase(OnlineActorOnlinenessDriver liveness, IClientMonotonicMomentOrigin clock)
    : IOnlineActorOnlinenessFramePhase
{
    private readonly OnlineActorOnlinenessDriver _liveness = liveness ?? throw new ArgumentNullException(nameof(liveness));
    private readonly IClientMonotonicMomentOrigin _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public void Tick() => _liveness.Tick(_clock.Instant);
}

internal sealed class AvatarModeAutoEntryFramePhase(AvatarMannerAutoListing autoEntry) : IAvatarModeAutoEntryFramePhase
{
    private readonly AvatarMannerAutoListing _autoListing = autoEntry ?? throw new ArgumentNullException(nameof(autoEntry));

    public void TryEnter() => _autoListing.TryEnter();
}
