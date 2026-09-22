using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

internal sealed partial class SimSpawnFollowupRunner
{
    // A create-parent follow-up: stamps the position, then attaches now or holds the relation until
    // the parent exists
    private (bool Success, SimAnchorRelationUpshot Outcome) ImposeBuildAncestorContinuation(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimActorKey tag,
        SimSpawnTailAction act,
        List<HeldPublish>? buf)
    {
        var buildAncestor = act.CreateParent!.Value;
        if (!ImposeBuildAncestorLocusStampSole(canon, buildAncestor))
            return (false, default);

        if (!_actors.TryFetchEngaged(buildAncestor.ParentGuid, out _))
        {
            _actors.AncestorAttachments.QueuePostponedApprovedRelation(canon.ServerGuid, tag, null, buildAncestor, act.AcceptedTimestamps);
            return (true, SimAnchorRelationUpshot.DeferredAwaitingParent);
        }
        SealAncestorAffix(canon, ticket, rebaseline: true, buf);
        return (true, SimAnchorRelationUpshot.Applied);
    }

    private SimSpawnExecutionStatus ImposeContinuation(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimActorKey tag,
        in SimSpawnTenancyFollowup continuation,
        in SimSpawnExecutionInputs feeds,
        Progress headway)
    {
        if (continuation.Kind
            == SimSpawnFollowupKind.SameIncarnationCreate)
        {
            return ExecuteEnvelope(canon, ticket, tag, continuation, feeds, headway);
        }

        if (headway.QueuedContinuationStance.IsValid)
        {
            var reactivateCondition =
                ReactivateQueuedStance(canon, tag, headway, out SimSovereignPositionRoute course);
            if (reactivateCondition != SimSpawnExecutionStatus.Completed)
                return reactivateCondition;
            headway.Trace.Add(PositionTrace(continuation.Sequence, -1, course));
            return SimSpawnExecutionStatus.Completed;
        }

        var act = continuation.Actions[0];
        switch (continuation.Kind)
        {
            case SimSpawnFollowupKind.ObjDesc:
                if (!ImposeObjRefDscAct(canon, ticket, act, null))
                    return Abandon(canon, tag);
                headway.Trace.Add(Trace(
                    SimSpawnExecutedActionKind.ObjDesc,
                    continuation.Sequence));
                return SimSpawnExecutionStatus.Completed;
            case SimSpawnFollowupKind.Parent:
                return ImposeAncestorContinuation(canon, ticket, tag, continuation, act, headway);
            case SimSpawnFollowupKind.Pickup:
                if (!ImposeLiftAct(canon, ticket, act, null))
                    return Abandon(canon, tag);
                headway.Trace.Add(Trace(
                    SimSpawnExecutedActionKind.Pickup,
                    continuation.Sequence));
                return SimSpawnExecutionStatus.Completed;
            case SimSpawnFollowupKind.Movement:
                if (!ImposeTravelAct(canon, ticket, act, null))
                    return Abandon(canon, tag);
                headway.Trace.Add(Trace(
                    SimSpawnExecutedActionKind.Movement,
                    continuation.Sequence));
                return SimSpawnExecutionStatus.Completed;
            case SimSpawnFollowupKind.State:
                if (!ImposePhaseAct(canon, ticket, act, null))
                    return Abandon(canon, tag);
                headway.Trace.Add(Trace(
                    SimSpawnExecutedActionKind.State,
                    continuation.Sequence));
                return SimSpawnExecutionStatus.Completed;
            case SimSpawnFollowupKind.Vector:
                if (!ImposeVectorAct(canon, ticket, act, null))
                    return Abandon(canon, tag);
                headway.Trace.Add(Trace(
                    SimSpawnExecutedActionKind.Vector,
                    continuation.Sequence));
                return SimSpawnExecutionStatus.Completed;
            case SimSpawnFollowupKind.Position:
                return ImposeLocusAct(
                    canon,
                    ticket,
                    tag,
                    continuation.Sequence,
                    -1,
                    act,
                    feeds,
                    headway,
                    null);
            default:
                throw new InvalidOperationException(
                    $"Not supported initial-Create continuation kind {continuation.Kind}.");
        }
    }

    private SimSpawnExecutionStatus ImposeAncestorContinuation(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimActorKey tag,
        in SimSpawnTenancyFollowup continuation,
        SimSpawnTailAction act,
        Progress headway)
    {
        var ancestorRefresh = act.Parent!.Value;
        if (!ImposeAncestorLocusStampSole(canon, ancestorRefresh))
            return Abandon(canon, tag);

        SimAnchorRelationUpshot verdict;
        if (!_actors.TryFetchEngaged(ancestorRefresh.ParentGuid, out SimActorRecord ancestor))
        {
            _actors.AncestorAttachments.QueuePostponedApprovedRelation(
                canon.ServerGuid, tag, ancestorRefresh, null, act.AcceptedTimestamps);
            verdict = SimAnchorRelationUpshot.DeferredAwaitingParent;
        }
        else if (ancestor.Incarnation != ancestorRefresh.ParentInstanceSequence)
        {
            if (KineticStampGate.IsNewer(ancestorRefresh.ParentInstanceSequence, ancestor.Incarnation))
            {
                verdict = SimAnchorRelationUpshot.DiscardedStaleParent;
            }
            else
            {
                _actors.AncestorAttachments.QueuePostponedApprovedRelation(
                    canon.ServerGuid, tag, ancestorRefresh, null, act.AcceptedTimestamps);
                verdict = SimAnchorRelationUpshot.DeferredAwaitingParent;
            }
        }
        else
        {
            SealAncestorAffix(canon, ticket, rebaseline: true, null);
            verdict = SimAnchorRelationUpshot.Applied;
        }

        headway.Trace.Add(Trace(
            SimSpawnExecutedActionKind.Parent,
            continuation.Sequence,
            ancestorRelationVerdict: verdict));
        return SimSpawnExecutionStatus.Completed;
    }

    private bool ImposeAncestorLocusStampSole(
        SimActorRecord canon,
        AncestorSignal.Parsed refresh)
    {
        if (!_actors.ImposeApprovedAncestorCapture(
                canon.ServerGuid,
                refresh,
                out RealmSession.MoverSpawn stamped))

            return false;
        _actors.RenewCapture(canon, stamped);
        return true;
    }

    private bool ImposeBuildAncestorLocusStampSole(
        SimActorRecord canon,
        CreateAnchorUpdate refresh)
    {
        if (!_actors.ImposeApprovedBuildAncestorCapture(
                canon.ServerGuid,
                refresh,
                out RealmSession.MoverSpawn stamped))

            return false;
        _actors.RenewCapture(canon, stamped);
        return true;
    }

    private SimSpawnExecutionStatus ExecuteEnvelope(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        SimActorKey tag,
        in SimSpawnTenancyFollowup continuation,
        in SimSpawnExecutionInputs feeds,
        Progress headway)
    {
        int beginJuncture = headway.EnvelopeJunctureOrdinal < 0 ? 0 : headway.EnvelopeJunctureOrdinal;

        if (headway.QueuedContinuationStance.IsValid)
        {
            var reactivateCondition =
                ReactivateQueuedStance(canon, tag, headway, out SimSovereignPositionRoute course);
            if (reactivateCondition != SimSpawnExecutionStatus.Completed)
                return reactivateCondition;
            headway.Trace.Add(PositionTrace(continuation.Sequence, beginJuncture, course));
            ++beginJuncture;
            headway.EnvelopeJunctureOrdinal = beginJuncture;
        }

        for (int idx = beginJuncture; idx < continuation.Actions.Length; ++idx)
        {
            if (!_actors.IsCurrent(canon))
                return Abandon(canon, tag);

            var act = continuation.Actions[idx];
            switch (act.Kind)
            {
                case SimSpawnTailActionKind.PreTailDescriptionAdaptation:
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.PreTailDescriptionAdaptation,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.ObjDesc:
                    if (!ImposeObjRefDscAct(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.ObjDesc,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.CreateParent:
                    {
                        (bool success, SimAnchorRelationUpshot verdict) =
                            ImposeBuildAncestorContinuation(canon, ticket, tag, act, headway.EnvelopeBuf);
                        if (!success)
                            return Abandon(canon, tag);
                        headway.Trace.Add(Trace(
                            SimSpawnExecutedActionKind.CreateParent,
                            continuation.Sequence,
                            idx,
                            ancestorRelationVerdict: verdict));
                        break;
                    }
                case SimSpawnTailActionKind.Pickup:
                    if (!ImposeLiftAct(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.Pickup,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.Position:
                    {
                        headway.EnvelopeJunctureOrdinal = idx;
                        var condition = ImposeLocusAct(
                            canon,
                            ticket,
                            tag,
                            continuation.Sequence,
                            idx,
                            act,
                            feeds,
                            headway,
                            headway.EnvelopeBuf);
                        if (condition
                            == SimSpawnExecutionStatus.AwaitingContinuationPlacement)

                            return condition;
                        if (condition != SimSpawnExecutionStatus.Completed)
                            return condition;
                        break;
                    }
                case SimSpawnTailActionKind.Movement:
                    if (!ImposeTravelAct(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.Movement,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.State:
                    if (!ImposePhaseAct(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.State,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.Vector:
                    if (!ImposeVectorAct(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.Vector,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.WeenieDescription:
                    if (!ApplyWeenieDescriptionAction(canon, ticket, act, headway.EnvelopeBuf))
                        return Abandon(canon, tag);
                    headway.Trace.Add(Trace(
                        SimSpawnExecutedActionKind.WeenieDescription,
                        continuation.Sequence,
                        idx));
                    break;
                case SimSpawnTailActionKind.ResidentCellCleanup:
                    {
                        var tidyDisposition =
                            ImposeHousedChamberTidy(canon);
                        if (tidyDisposition is null)

                            return Abandon(canon, tag);
                        headway.Trace.Add(new SimSpawnExecutedAction(
                        SimSpawnExecutedActionKind.ResidentCellCleanup,
                        continuation.Sequence,
                        idx,
                        null,
                        SimWarpHookPhase.None,
                        null,
                        tidyDisposition));
                        break;
                    }
                default:
                    throw new InvalidOperationException(
                        $"Not supported same-incarnation tail action {act.Kind}.");
            }

            headway.EnvelopeJunctureOrdinal = idx + 1;
        }

        foreach (HeldPublish queued in headway.EnvelopeBuf)
            BroadcastInstant(canon, queued.Change, queued.Matches, queued.Cancellation);
        headway.EnvelopeBuf.Clear();
        headway.EnvelopeJunctureOrdinal = -1;
        return SimSpawnExecutionStatus.Completed;
    }

    private SimSpawnExecutionStatus ReactivateQueuedStance(
        SimActorRecord canon,
        SimActorKey tag,
        Progress headway,
        out SimSovereignPositionRoute course)
    {
        course = headway.QueuedContinuationCourse;
        var stanceTicket = headway.QueuedContinuationStance;
        if (_physics.SetPosition.IsStanceLatest(stanceTicket))
            return SimSpawnExecutionStatus.AwaitingContinuationPlacement;

        if (!_physics.SetPosition.TryGlimpseAcknowledgedStance(
                stanceTicket,
                out SimPlacementMirrorTicket proj)
            || proj.Entity != stanceTicket.Entity
            || proj.SessionLifetimeVersion != stanceTicket.SessionLifetimeVersion
            || proj.PositionAuthorityVersion != stanceTicket.PositionAuthorityVersion
            || canon.PositionAuthorityVersion != stanceTicket.PositionAuthorityVersion
            || proj.ExactCellId is 0u
            || proj.ExactCellId != canon.WholeChamberTag
            || proj.PlacementCommitVersion != canon.PlacementCommitVersion)
        {
            var forgotten =
                _physics.SetPosition.DropPreciseStance(stanceTicket);
            _physics.SetPosition.BroadcastAbort(forgotten);
            headway.QueuedContinuationStance = default;
            return Abandon(canon, tag);
        }

        if (!_physics.SetPosition.AbsorbAcknowledgedStance(stanceTicket, proj))
        {
            var forgotten =
                _physics.SetPosition.DropPreciseStance(stanceTicket);
            _physics.SetPosition.BroadcastAbort(forgotten);
            headway.QueuedContinuationStance = default;
            return Abandon(canon, tag);
        }

        headway.QueuedContinuationStance = default;
        headway.QueuedContinuationSeries = 0UL;
        return SimSpawnExecutionStatus.Completed;
    }
}
