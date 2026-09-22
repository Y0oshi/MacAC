using MacAC.Wire;

namespace MacAC.Sim.Presence;

public enum SimLinkStatus
{
    Inactive,
    Connecting,
    CheckingData,
    Ready,
    Unsupported,
    Failed,
}

public readonly record struct SimLinkCapture(SimLinkStatus Status, float ConnectionProgress, float UpdateProgress, string? Error = null);

public interface ISimLinkLens
{
    SimLinkCapture Snapshot { get; }
}

public sealed class SimLinkLedger : ISimLinkLens
{
    private readonly object _latch = new();
    private SimLinkCapture _grab;

    public static ISimLinkLens Inactive { get; } = new SimLinkLedger();

    public SimLinkCapture Snapshot
    {
        get { lock (_latch) return _grab; }
    }

    internal void Reset()
    {
        lock (_latch)
            _grab = default;
    }

    internal void Apply(ConnectionHeadway headway)
    {
        SimLinkStatus condition = headway.Phase switch
        {
            LinkPhase.Connecting => SimLinkStatus.Connecting,
            LinkPhase.CheckingData => SimLinkStatus.CheckingData,
            LinkPhase.Ready => SimLinkStatus.Ready,
            LinkPhase.Unsupported => SimLinkStatus.Unsupported,
            LinkPhase.Failed => SimLinkStatus.Failed,
            _ => SimLinkStatus.Inactive,
        };
        lock (_latch)
        {
            // The connection bar fills once the link is up; the update bar only when data checks pass.
            bool linked = condition is SimLinkStatus.CheckingData or SimLinkStatus.Ready or SimLinkStatus.Unsupported;
            _grab = new SimLinkCapture(condition, linked ? 1 : _grab.ConnectionProgress, condition == SimLinkStatus.Ready ? 1 : 0, headway.Error);
        }
    }

    internal void Fail(Exception problem)
    {
        Apply(new(problem is UnknownDataUpdateException ? LinkPhase.Unsupported : LinkPhase.Failed, problem.Message));
    }
}
