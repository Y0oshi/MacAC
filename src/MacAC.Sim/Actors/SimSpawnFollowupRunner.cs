using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal sealed partial class SimSpawnFollowupRunner
{
    private enum TailPhase : byte
    {
        NotStarted,
        Adopted,
        HookRecorded,
        DeferredReplayed,
        RelationsReplayed,
    }

    private readonly record struct HeldPublish(
        SimActorChange Change,
        Func<bool> Matches,
        SimPlacementAbortStub Cancellation);

    private sealed class Progress
    {
        internal required ulong LeaseId { get; init; }
        internal ulong ImposedThroughSeries { get; set; }
        internal TailPhase RearStage { get; set; }
        internal int EnvelopeJunctureOrdinal { get; set; } = -1;
        internal SimActorPlacementTicket QueuedContinuationStance { get; set; }
        internal ulong QueuedContinuationSeries { get; set; }
        internal SimSovereignPositionRoute QueuedContinuationCourse { get; set; }
        internal bool LocusCombineSealedForReattempt { get; set; }
        internal ulong LocusCombineSealedVer { get; set; }
        internal int ReplayedPostponedDescendantTally { get; set; }
        internal List<HeldPublish> EnvelopeBuf { get; } = [];
        internal ImmutableArray<SimSpawnExecutedAction>.Builder Trace { get; } =
            ImmutableArray.CreateBuilder<SimSpawnExecutedAction>();
    }

    private readonly SimActorIndex _actors;

    private readonly SimSpawnTenancyLedger _tenancies;

    private readonly SimKineticsLedger _physics;

    private readonly SimActorObjectEventFlow _signals;

    private readonly Func<RealmSession.MoverSpawn, bool, SimActorRegistrationResult> _registerDeferredChild;

    private readonly Func<SimActorRecord, ulong, RealmSession.MoverSpawn, bool, bool> _foldApprovedSummon;

    private readonly Dictionary<SimActorKey, Progress> _progress = [];

    private readonly HashSet<SimActorKey> _occupied = [];

    private Func<SimEpochTicket>? _gen;

    private Func<bool>? _srvLocusRule;

    private Func<Vector3?>? _avatarLocus;

    private bool _feedsTied;

    internal SimSpawnFollowupRunner(
        SimActorIndex entities,
        SimSpawnTenancyLedger residences,
        SimKineticsLedger physics,
        SimActorObjectEventFlow events,
        Func<RealmSession.MoverSpawn, bool, SimActorRegistrationResult>
            registerDeferredChild,
        Func<SimActorRecord, ulong, RealmSession.MoverSpawn, bool, bool>
            applyAcceptedSpawn)
    {
        _actors = entities ?? throw new ArgumentNullException(nameof(entities));
        _tenancies = residences
            ?? throw new ArgumentNullException(nameof(residences));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _signals = events ?? throw new ArgumentNullException(nameof(events));
        _registerDeferredChild = registerDeferredChild
            ?? throw new ArgumentNullException(nameof(registerDeferredChild));
        _foldApprovedSummon = applyAcceptedSpawn
            ?? throw new ArgumentNullException(nameof(applyAcceptedSpawn));
    }

    internal void AttachGen(Func<SimEpochTicket> gen)
    {
        ArgumentNullException.ThrowIfNull(gen);
        if (_gen is not null)
        {
            throw new InvalidOperationException(
                "The initial-create continuation executor's generation source is by now bound");
        }
        _gen = gen;
    }

    internal void AttachOnlineFeeds(
        Func<bool> useLocusFromSrv,
        Func<Vector3?> ownAvatarLocus)
    {
        ArgumentNullException.ThrowIfNull(useLocusFromSrv);
        ArgumentNullException.ThrowIfNull(ownAvatarLocus);
        if (_feedsTied)
        {
            throw new InvalidOperationException(
                "The initial-create continuation executor's live-input sources are by now bound");
        }
        _srvLocusRule = useLocusFromSrv;
        _avatarLocus = ownAvatarLocus;
        _feedsTied = true;
    }

    internal bool TryFetchQueuedContinuationStance(
        SimActorKey tag,
        out SimActorPlacementTicket stance)
    {
        if (_progress.TryGetValue(tag, out Progress? headway)
            && headway.QueuedContinuationStance.IsValid)
        {
            stance = headway.QueuedContinuationStance;
            return true;
        }
        stance = default;
        return false;
    }

    internal int HeadwayTally => _progress.Count;

    internal long RerunMissTally { get; private set; }

    internal Exception? PreviousRerunMiss { get; private set; }

    internal bool TryFetchQueuedContinuationCourse(
        SimActorKey tag,
        out SimSovereignPositionRoute course)
    {
        if (_progress.TryGetValue(tag, out Progress? headway)
            && headway.QueuedContinuationStance.IsValid)
        {
            course = headway.QueuedContinuationCourse;
            return true;
        }
        course = default;
        return false;
    }

    internal void TossHeadway(SimActorKey tag)
    {
        _finishedStubs.Remove(tag);
        if (!_progress.Remove(tag, out Progress? headway))
            return;
        if (headway.QueuedContinuationStance.IsValid)
        {
            var abort =
                _physics.SetPosition.DropPreciseStance(
                    headway.QueuedContinuationStance);
            _physics.SetPosition.BroadcastAbort(abort);
        }
    }

    internal void TossAll()
    {
        foreach (Progress headway in _progress.Values)
        {
            if (!headway.QueuedContinuationStance.IsValid)
                continue;
            var abort =
                _physics.SetPosition.DropPreciseStance(
                    headway.QueuedContinuationStance);
            _physics.SetPosition.BroadcastAbort(abort);
        }
        _progress.Clear();
        _finishedStubs.Clear();
    }

    internal SimSpawnExecutionStatus Perform(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        in SimSpawnExecutionInputs feeds,
        out SimSpawnExecutionStub receipt)
    {
        ArgumentNullException.ThrowIfNull(canon);
        receipt = default;
        if (!ticket.IsValid || canon.Key is not { } tag)
            return SimSpawnExecutionStatus.RejectedToken;

        if (!_occupied.Add(tag))
            return SimSpawnExecutionStatus.RejectedAuthority;

        try
        {
            return Run(canon, ticket, feeds, tag, out receipt);
        }
        finally
        {
            _occupied.Remove(tag);
        }
    }

    private SimSpawnExecutionInputs FeedsInstant(
        SimActorRecord canon,
        in SimSpawnExecutionInputs feeds)
    {
        bool useLocusFromSrv = _srvLocusRule is { } src
            ? src()
            : feeds.UsePositionFromServer;
        float avatarGap = feeds.PlayerDistance;
        if (_avatarLocus?.Invoke() is { } ownAvatarLocus
            && (canon.Snapshot.Physics?.Position
                ?? canon.Snapshot.Position) is { } approved)
        {
            Vector3 mark = new Vector3(
                approved.PositionX, approved.PositionY, approved.PositionZ);
            avatarGap = Vector3.Distance(mark, ownAvatarLocus);
        }
        return new SimSpawnExecutionInputs(
            useLocusFromSrv, avatarGap);
    }

    private void NoteRerunMiss(Exception problem)
    {
        ++RerunMissTally;
        PreviousRerunMiss = problem;
    }

    private SimSpawnExecutionStatus Run(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        in SimSpawnExecutionInputs feeds,
        SimActorKey tag,
        out SimSpawnExecutionStub receipt)
    {
        receipt = default;
        var netFeeds =
            FeedsInstant(canon, feeds);

        if (_progress.TryGetValue(tag, out Progress? extant)
            && extant.LeaseId != ticket.LeaseId)
        {
            TossHeadway(tag);
            return SimSpawnExecutionStatus.RejectedAuthority;
        }

        Progress? headway = extant;

        while (true)
        {
            if (headway is not null && headway.QueuedContinuationStance.IsValid)
            {
                _tenancies.AdvanceExecutorBaseline(
                    canon,
                    ticket,
                    SimRunnerBaselineFields.FullCellId
                        | SimRunnerBaselineFields.PlacementCommitVersion);
            }
            var wrapUp =
                _tenancies.Complete(canon, ticket, out SimSpawnTenancyStub residenceReceipt);
            switch (wrapUp)
            {
                case SimSpawnTenancyFinishStatus.PendingPlacement:
                    return SimSpawnExecutionStatus.PendingPlacement;
                case SimSpawnTenancyFinishStatus.RejectedToken:
                    TossHeadway(tag);
                    return SimSpawnExecutionStatus.RejectedToken;
                case SimSpawnTenancyFinishStatus.RejectedAuthority:
                    TossHeadway(tag);
                    return SimSpawnExecutionStatus.RejectedAuthority;
            }

            if (headway is null)
            {
                headway = new Progress { LeaseId = ticket.LeaseId };
                _progress[tag] = headway;
            }

            if (headway.RearStage != TailPhase.DeferredReplayed)
            {
                var rearCondition =
                    ExecuteRear(canon, ticket, residenceReceipt, headway);
                if (rearCondition != SimSpawnExecutionStatus.Completed)
                    return Abandon(canon, tag);
            }

            while (headway.ImposedThroughSeries
                < (ulong)residenceReceipt.Continuations.Length)
            {
                if (!_actors.IsCurrent(canon) || canon.Key != ticket.Entity)
                    return Abandon(canon, tag);

                int ordinal = (int)headway.ImposedThroughSeries;
                var continuation =
                    residenceReceipt.Continuations[ordinal];
                if (continuation.InstanceSequence != canon.Incarnation)
                    return Abandon(canon, tag);

                var enactCondition =
                    ImposeContinuation(canon, ticket, tag, continuation, netFeeds, headway);
                if (enactCondition
                    == SimSpawnExecutionStatus.AwaitingContinuationPlacement)

                    return enactCondition;
                if (enactCondition != SimSpawnExecutionStatus.Completed)
                    return enactCondition;

                headway.ImposedThroughSeries = continuation.Sequence;
                headway.EnvelopeJunctureOrdinal = -1;
            }

            var free =
                _tenancies.AbsorbExecuted(
                    canon,
                    residenceReceipt.Adoption,
                    headway.ImposedThroughSeries);
            switch (free)
            {
                case SimSpawnTenancyRunnerReleaseStatus.Released:
                    {
                        SimSpawnExecutionStub finishedReceipt = new SimSpawnExecutionStub(
                            tag,
                            residenceReceipt.FullCellId,
                            residenceReceipt.TeleportHookPhase,
                            headway.Trace.ToImmutable(),
                            headway.ReplayedPostponedDescendantTally);
                        receipt = finishedReceipt;
                        var publicWrapUp =
                            ProjectWrapUp(finishedReceipt);
                        _progress.Remove(tag);
                        _physics.SetPosition.BroadcastExecutorWrapUp(
                            canon,
                            priorBroadcast: ticket =>
                                _finishedStubs[tag] =
                                    (ticket.Sequence, finishedReceipt, publicWrapUp));
                        return SimSpawnExecutionStatus.Completed;
                    }
                case SimSpawnTenancyRunnerReleaseStatus.Revised:
                    continue;
                default:
                    return Abandon(canon, tag);
            }
        }
    }

    private SimSpawnExecutionStatus Abandon(
        SimActorRecord canon,
        SimActorKey tag)
    {
        if (_tenancies.Drop(
                canon,
                out _,
                out SimPlacementAbortStub abort))

            _physics.SetPosition.BroadcastAbort(abort);
        TossHeadway(tag);
        return SimSpawnExecutionStatus.RejectedAuthority;
    }

    private SimEpochTicket EpochInstant() => _gen?.Invoke() ?? default;
}
