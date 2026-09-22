using MacAC.Mechanics.Fighting;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim;

public readonly record struct SimEventMark(SimEpochTicket Generation, ulong Sequence, ulong FrameNumber);

/// <summary>Hands out event marks; the ordinal restarts whenever the epoch changes.</summary>
public sealed class SimEventTicker
{
    private SimEpochTicket _epoch;
    private bool _primed;
    private ulong _index;

    public ulong PreviousSequence => _index;

    public SimEventMark Next(SimEpochTicket gen, ulong cycleNumber)
    {
        if (!_primed || gen != _epoch)
        {
            _epoch = gen;
            _index = 0;
            _primed = true;
        }
        return new SimEventMark(gen, checked(++_index), cycleNumber);
    }
}

public enum SimDirectiveDomain
{
    Session = 0,
    Selection = 1,
    Combat = 2,
    Movement = 3,
    Chat = 4,
    Portal = 5,
    InventoryState = 6,
    Spellbook = 7,
    Character = 8,
    Social = 9,
    Magic = 10,
    Fellowship = 11,
    Allegiance = 12,
}

public enum SimActorChange
{
    Registered,
    Updated,
    Rebucketed,
    Hidden,
    Withdrawn,
    Deleted,
}

public enum SimStashChange
{
    Added,
    Updated,
    Moved,
    Removed,
    Cleared,
}

public readonly record struct SimLifespanDiff(SimEventMark Stamp, SimLifespanPhase Previous, SimLifespanPhase Current);

public readonly record struct SimDirectiveDiff(
    SimEventMark Stamp,
    SimDirectiveDomain Domain,
    int Operation,
    SimDirectiveStatus Status,
    uint PrimaryObjectId = 0u,
    string? Text = null);

public readonly record struct SimActorDiff(SimEventMark Stamp, SimActorChange Change, SimActorCapture Entity);

public readonly record struct SimPlacementDiff(SimEventMark Stamp, SimPlacementMirrorCapture Placement);

public readonly record struct SimStashDiff(SimEventMark Stamp, SimStashChange Change, SimStashItemCapture Item);

public readonly record struct SimCommsEntry(long Revision, uint SenderGuid, int Kind, string Sender, string Text, string ChannelName);

public readonly record struct SimCommsDiff(SimEventMark Stamp, SimCommsEntry Entry);

public readonly record struct SimLocomotionDiff(SimEventMark Stamp, SimLocomotionCapture Movement);

public readonly record struct SimPortalDiff(SimEventMark Stamp, SimPortalCapture Portal);

public readonly record struct SimFightingDiff(SimEventMark Stamp, FightingManner Mode, int TrackedTargetCount, SimFightingAttackCapture Attack);

public interface ISimEventWatcher
{
    void OnLifecycle(in SimLifespanDiff diff);

    void OnDirective(in SimDirectiveDiff diff);

    void OnActor(in SimActorDiff diff);

    void OnSatchel(in SimStashDiff diff);

    void OnChat(in SimCommsDiff diff);

    void OnTravel(in SimLocomotionDiff diff);

    void OnGateway(in SimPortalDiff diff);

    void OnFighting(in SimFightingDiff diff);
}

public interface ISimEventFeed
{
    IDisposable Subscribe(ISimEventWatcher watcher);
}
