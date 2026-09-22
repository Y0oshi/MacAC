using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Sim.Presence;

public enum SimToonPickLifespan
{
    Inactive,
    Connecting,
    AwaitingSelection,
    EnteringWorld,
    InWorld,
}

public enum SimToonPickOperation
{
    None,
    DeleteRequested,
    DeleteAcknowledged,
    RestoreRequested,
    RestoreSucceeded,
    RestoreRejected,
}

public enum SimToonPickDiffKind
{
    Reset,
    RosterChanged,
    HighlightChanged,
    DeleteConfirmationOpened,
    DeleteConfirmationCancelled,
    DeleteRequested,
    DeleteAcknowledged,
    RestoreRequested,
    RestoreCompleted,
    RestoreCorrelationExpired,
    ErrorChanged,
    EnteringWorld,
    EnteredWorld,
    WorldNameChanged,
}

public readonly record struct SimToonPickEntry(
    int ActiveIndex,
    uint CharacterId,
    string Name,
    uint SecondsGreyedOut)
{
    public bool IsQueuedErase => SecondsGreyedOut is not 0u;

    public bool CanJoin => CharacterId is not 0u && !IsQueuedErase;
}

public readonly record struct SimToonPickButtons(
    bool CanEnter,
    bool CanDelete,
    bool CanRestore,
    bool DeleteVisible,
    bool RestoreVisible,
    bool CanCreate = false)
{
    public static SimToonPickButtons None { get; } =
        new(false, false, false, true, false);
}

public readonly record struct SimToonPickError(
    uint RawCode,
    CharacterFault.WireCode Code,
    string Message);

public readonly record struct SimToonPickCapture(
    SimEpochTicket Generation,
    SimToonPickLifespan Lifecycle,
    long Revision,
    string AccountName,
    int SlotCount,
    int RosterCount,
    string WorldName,
    uint HighlightedCharacterId,
    int HighlightedDisplayIndex,
    uint PendingDeleteCharacterId,
    uint LastRestoreRequestedCharacterId,
    SimToonPickOperation Operation,
    SimToonPickError? Error,
    SimToonPickButtons Buttons)
{
    public bool IsActive
    {
        get
        {
            return Lifecycle is SimToonPickLifespan.AwaitingSelection
            or SimToonPickLifespan.EnteringWorld;
        }
    }
}

public readonly record struct SimToonPickDiff(
    SimEpochTicket Generation,
    ulong Sequence,
    long Revision,
    SimToonPickDiffKind Kind,
    uint CharacterId = 0u,
    uint ErrorCode = 0u);

public interface ISimToonPickVisitor
{
    void Visit(in SimToonPickEntry toon);
}

public interface ISimToonPickWatcher
{
    void OnToonPickAltered(
        in SimToonPickDiff diff);
}

public interface ISimToonPickEventFeed
{
    IDisposable Subscribe(ISimToonPickWatcher watcher);
}

public interface ISimToonPickLens
    : ISimToonPickEventFeed
{
    SimToonPickCapture Snapshot { get; }

    bool TryFetchAt(
        int readoutOrdinal,
        out SimToonPickEntry toon);

    bool TryGet(
        uint toonIdent,
        out SimToonPickEntry toon);

    void Call(ISimToonPickVisitor visitor);
}

public interface ISimToonPickDirectives
{
    SimDirectiveResult Highlight(
        SimEpochTicket anticipatedGen,
        uint toonIdent);

    SimDirectiveResult Enter(
        SimEpochTicket anticipatedGen);

    SimDirectiveResult ReqErase(
        SimEpochTicket anticipatedGen);

    SimDirectiveResult ConfirmErase(
        SimEpochTicket anticipatedGen);

    SimDirectiveResult Restore(
        SimEpochTicket anticipatedGen);

    SimDirectiveResult Cancel(
        SimEpochTicket anticipatedGen);
}
