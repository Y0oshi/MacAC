namespace MacAC.Mechanics.Kinetics.Gait;

public interface IMotionDoneTap
{
    void MotionDone(uint locomotion, bool success);
}

/// <summary>A motion waiting for its animations to finish before it is reported done.</summary>
public sealed class QueuedMotion(uint locomotion, uint countAnims)
{
    public uint Motion = locomotion;
    public uint CountAnims = countAnims;
}

public static class MotionTableKeeperError
{
    /// <summary>0 - success.</summary>
    public const uint Success = 0u;
    /// <summary>7 - no motion table loaded.</summary>
    public const uint NoChart = 7u;
    /// <summary>0x43 - DoObjectMotion/StopObjectMotion returned failure.</summary>
    public const uint LocomotionFailed = 0x43u;
    public const uint NotHandled = 0xFFFFFFFFu;
}

/// <summary>A motion-table request: which kind, which motion, how fast.</summary>
public readonly struct MotionTableGait(TravelKind kind, uint locomotion, float pace)
{
    public readonly TravelKind Type = kind;
    public readonly uint Motion = locomotion;
    public readonly float Speed = pace;

    public static MotionTableGait Interpreted(uint locomotion, float pace) => new(TravelKind.InterpretedCommand, locomotion, pace);

    public static MotionTableGait HaltInterpreted(uint locomotion, float pace) => new(TravelKind.StopInterpretedCommand, locomotion, pace);

    public static MotionTableGait StopCompletely() => new(TravelKind.StopCompletely, 0u, 1f);
}

public sealed class MotionTableKeeper
{
    private const uint CycleClassBit = 0x40000000u;
    private const uint ModifierClassBit = 0x20000000u;
    private const uint ActClassBit = 0x10000000u;

    private const uint CycleRearChunkBitmask = 0xb0000000u;

    private const uint StylingRearChunkBitmask = 0x70000000u;

    public const uint PrimedSentinel = 0x41000003u;

    private readonly MotionTableRecord? _chart;
    private readonly AnimTrack _track;
    private readonly IMotionDoneTap _done;
    private readonly LinkedList<QueuedMotion> _owed = new(); // pending_animations
    private int _animCounter; // animation_counter (@0x20)

    public MotionTableKeeper(MotionTableRecord? chart, LocomotionPhase phase, AnimTrack series, IMotionDoneTap drain)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(drain);

        _chart = chart;
        State = phase;
        _track = series;
        _done = drain;
    }

    public LocomotionPhase State { get; }

    /// <summary>Read-only inspection surface for tests: the pending queue in head-to-tail order.</summary>
    public IEnumerable<QueuedMotion> QueuedAnims => _owed;

    public int AnimCounter => _animCounter;

    public void AppendToFifo(uint locomotion, uint beats)
    {
        _owed.AddLast(new QueuedMotion(locomotion, beats));
        DropRedundantLinks();
    }

    public void DropRedundantLinks()
    {
        // Skip trailing zero-tick nodes to find the newest real motion
        var newest = _owed.Last;
        while (newest is not null && newest.Value.CountAnims is 0)
            newest = newest.Previous;
        if (newest is null)
            return;

        uint locomotion = newest.Value.Motion;
        bool isCycle = (locomotion & CycleClassBit) is not 0 && (locomotion & ModifierClassBit) is 0;
        bool isStyling = (int)locomotion < 0;
        if (!isCycle && !isStyling)
            return;

        // Cycles must also match on tick count; styles only on motion.
        // Each class has its own notion of which intervening node blocks the search.
        uint chunkBitmask = isCycle ? CycleRearChunkBitmask : StylingRearChunkBitmask;
        for (LinkedListNode<QueuedMotion>? scan = newest.Previous; scan is not null; scan = scan.Previous)
        {
            var contender = scan.Value;
            if (contender.Motion == locomotion && (!isCycle || contender.CountAnims is not 0))
            {
                TruncateAnimRoster(scan);
                return;
            }
            if (contender.CountAnims is not 0 && (contender.Motion & chunkBitmask) is not 0)
                return; // blocked by an intervening "important" non-zero node
        }
    }

    public void AnimationDone(bool success)
    {
        var front = _owed.First;
        if (front is null)
            return;

        _animCounter += 1;
        while (front is not null && front.Value.CountAnims <= _animCounter)
        {
            Settle(front.Value, success);
            _animCounter -= (int)front.Value.CountAnims;
            _owed.RemoveFirst();
            front = _owed.First;
        }

        if (_animCounter is not 0 && front is null)
            _animCounter = 0;
    }

    public void VerifyForFinishedMotions()
    {
        while (_owed.First is { Value.CountAnims: 0 } front)
        {
            Settle(front.Value, true);
            _owed.RemoveFirst();
        }
    }

    public void WieldMoment() => VerifyForFinishedMotions();

    public void BootstrapCondition()
    {
        uint beats = 0;
        _chart?.AssignDefaultPhase(State, _track, out beats);
        AppendToFifo(PrimedSentinel, beats);
    }

    public void HandleEnterWorld()
    {
        _track.DeleteAllConnectAnims();
        EmptyFifo();
    }

    public void ServiceQuitRealm() => EmptyFifo();

    public uint PerformMovement(MotionTableGait travel)
    {
        if (_chart is null)
            return MotionTableKeeperError.NoChart; // 7

        uint beats;
        switch (travel.Type)
        {
            case TravelKind.InterpretedCommand:
                if (!_chart.DoObjectLocomotion(travel.Motion, State, _track, travel.Speed, out beats))
                    return MotionTableKeeperError.LocomotionFailed; // 0x43
                AppendToFifo(travel.Motion, beats);
                return MotionTableKeeperError.Success;

            case TravelKind.StopInterpretedCommand:
                if (!_chart.HaltObjectLocomotion(travel.Motion, travel.Speed, State, _track, out beats))
                    return MotionTableKeeperError.LocomotionFailed;
                AppendToFifo(PrimedSentinel, beats);
                return MotionTableKeeperError.Success;

            case TravelKind.StopCompletely:
                _chart.HaltObjectCompletely(State, _track, out beats);
                AppendToFifo(PrimedSentinel, beats); // UNCONDITIONAL - queued regardless of return value
                return MotionTableKeeperError.Success;

            default:
                return MotionTableKeeperError.NotHandled;
        }
    }

    private void TruncateAnimRoster(LinkedListNode<QueuedMotion> haltAtExclusive)
    {
        uint removedBeats = 0;
        for (LinkedListNode<QueuedMotion>? joint = _owed.Last; !ReferenceEquals(joint, haltAtExclusive); joint = joint.Previous)
        {
            if (joint is null)
                return; // stopAtExclusive wasn't actually in the list -> abort quietly
            removedBeats += joint.Value.CountAnims;
            joint.Value.CountAnims = 0;
        }
        _track.RemoveLinkAnimations((int)removedBeats);
    }

    private void Settle(QueuedMotion owed, bool success)
    {
        if ((owed.Motion & ActClassBit) is not 0)
            State.DropActFront();
        _done.MotionDone(owed.Motion, success);
    }

    private void EmptyFifo()
    {
        while (_owed.First is not null)
            AnimationDone(false);
    }
}
