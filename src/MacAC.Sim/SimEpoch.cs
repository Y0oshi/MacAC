using System.Globalization;

namespace MacAC.Sim;

/// <summary>Monotonic session-lifetime counter; every start/stop pair moves it forward.</summary>
public readonly record struct SimEpochTicket(ulong Value)
{
    public static SimEpochTicket Initial => default;

    public SimEpochTicket Next() => new(checked(Value + 1UL));

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public enum SimLifespanPhase
{
    Constructed,
    Stopped,
    Starting,
    InWorld,
    Stopping,
    Faulted,
    Disposed,
}

public readonly record struct SimLifespanCapture(
    SimEpochTicket Generation,
    SimLifespanPhase State,
    uint PlayerGuid,
    bool HasTransport);

[Flags]
public enum SimTeardownStage
{
    None = 0,
    CommandsInert = 1 << 0,
    InboundDetached = 1 << 1,
    TransportDisposed = 1 << 2,
    HostReset = 1 << 3,
    Complete = CommandsInert | InboundDetached | TransportDisposed | HostReset,
}

public readonly record struct SimTeardownAck(
    SimEpochTicket RetiredGeneration,
    SimEpochTicket CurrentGeneration,
    SimDirectiveStatus Status,
    SimTeardownStage CompletedStages,
    Exception? Error = null)
{
    private const SimTeardownStage All = SimTeardownStage.Complete;

    public bool IsComplete
    {
        get
        {
            return Status == SimDirectiveStatus.Accepted
        && (CompletedStages & All) == All
        && Error is null;
        }
    }
}
