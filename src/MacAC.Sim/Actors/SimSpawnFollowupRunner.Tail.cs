using System.Collections.Immutable;
using System.Numerics;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Kinetics;

namespace MacAC.Sim.Actors;

// The initial tail: adoption, the teleport hook, and replaying children and relations that waited
// on this entity
internal sealed partial class SimSpawnFollowupRunner
{
    private SimSpawnExecutionStatus ExecuteRear(
        SimActorRecord canon,
        in SimSpawnTenancyTicket ticket,
        in SimSpawnTenancyStub residenceReceipt,
        Progress headway)
    {
        if (headway.RearStage == TailPhase.NotStarted)
        {
            if (!_tenancies.AdoptFinishedStance(canon, ticket))
                return SimSpawnExecutionStatus.RejectedAuthority;
            headway.Trace.Add(new SimSpawnExecutedAction(
                SimSpawnExecutedActionKind.InitialAdoption,
                0UL,
                -1,
                null,
                SimWarpHookPhase.None));
            headway.RearStage = TailPhase.Adopted;
        }

        if (headway.RearStage == TailPhase.Adopted)
        {
            if (residenceReceipt.TeleportHookPhase
                == SimWarpHookPhase.AfterEnterWorld)
            {
                headway.Trace.Add(new SimSpawnExecutedAction(
                    SimSpawnExecutedActionKind.TeleportHookRequest,
                    0UL,
                    -1,
                    null,
                    SimWarpHookPhase.AfterEnterWorld));
            }
            headway.RearStage = TailPhase.HookRecorded;
        }

        if (headway.RearStage == TailPhase.HookRecorded)
        {
            if (!RerunPostponedDescendants(canon, headway))
                return SimSpawnExecutionStatus.RejectedAuthority;
            headway.RearStage = TailPhase.DeferredReplayed;
        }

        if (headway.RearStage == TailPhase.DeferredReplayed)
        {
            if (!RerunPostponedApprovedRelations(canon, headway))
                return SimSpawnExecutionStatus.RejectedAuthority;
            headway.RearStage = TailPhase.RelationsReplayed;
        }

        return SimSpawnExecutionStatus.Completed;
    }

    private bool RerunPostponedDescendants(SimActorRecord canon, Progress headway)
    {
        if (!_actors.IsCurrent(canon))
            return false;

        var detached =
            _actors.AncestorAttachments.UnfastenPostponedCreates(
                canon.ServerGuid, out HeldRerunWindowTicket pane);
        for (int ordinal = 0; ordinal < detached.Length; ++ordinal)
        {
            if (!_actors.IsCurrent(canon))
            {
                _actors.AncestorAttachments.ReinstatePostponedCreates(
                    pane,
                    detached.AsSpan()[ordinal..]);
                return false;
            }

            var postponed = detached[ordinal];
            SimHeldChildRerunUpshot verdict;
            try
            {
                var result =
                    _registerDeferredChild(postponed.Spawn, postponed.IsLocalPlayer);
                verdict = result.Canonical is not null
                    ? SimHeldChildRerunUpshot.Registered
                    : result.DeferredForParent
                        ? SimHeldChildRerunUpshot.ReDeferred
                        : SimHeldChildRerunUpshot.Rejected;
            }
            catch (Exception problem)
            {
                NoteRerunMiss(problem);
                verdict = SimHeldChildRerunUpshot.Rejected;
            }
            headway.Trace.Add(new SimSpawnExecutedAction(
                SimSpawnExecutedActionKind.DeferredChildReplay,
                0UL,
                -1,
                null,
                SimWarpHookPhase.None,
                verdict));
            headway.ReplayedPostponedDescendantTally++;
        }
        // Nothing left to restore on a full pass - releases the window
        _actors.AncestorAttachments.ReinstatePostponedCreates(
            pane, ReadOnlySpan<HeldAnchorCreate>.Empty);
        return true;
    }

    private bool RerunPostponedApprovedRelations(SimActorRecord canon, Progress headway)
    {
        if (!_actors.IsCurrent(canon))
            return false;

        var detached =
            _actors.AncestorAttachments.UnfastenPostponedApprovedRelations(
                canon.ServerGuid, out HeldRerunWindowTicket pane);
        for (int ordinal = 0; ordinal < detached.Length; ++ordinal)
        {
            if (!_actors.IsCurrent(canon))
            {
                _actors.AncestorAttachments.ReinstatePostponedApprovedRelations(
                    pane,
                    detached.AsSpan()[ordinal..]);
                return false;
            }

            var listing = detached[ordinal];
            SimAnchorRelationUpshot verdict;
            try
            {
                verdict = ImposeReplayedAncestorRelation(canon, listing);
            }
            catch (Exception problem)
            {
                NoteRerunMiss(problem);
                verdict = SimAnchorRelationUpshot.Rejected;
            }
            headway.Trace.Add(new SimSpawnExecutedAction(
                SimSpawnExecutedActionKind.ParentRelationReplay,
                0UL,
                -1,
                null,
                SimWarpHookPhase.None,
                null,
                null,
                SimPositionConstrainPhase.None,
                false,
                false,
                false,
                false,
                false,
                verdict));
            if (verdict == SimAnchorRelationUpshot.DeferredAwaitingParent)
            {
                // Relation still names an incarnation that has not arrived yet - wait for the NEXT one.
                _actors.AncestorAttachments.QueuePostponedApprovedRelation(listing);
            }
        }
        _actors.AncestorAttachments.ReinstatePostponedApprovedRelations(
            pane, ReadOnlySpan<HeldGrantedAnchorRelation>.Empty);
        return true;
    }

    private SimAnchorRelationUpshot ImposeReplayedAncestorRelation(
        SimActorRecord ancestor,
        in HeldGrantedAnchorRelation listing)
    {
        if (!_actors.TryFetchEngaged(listing.ChildGuid, out SimActorRecord descendant)
            || descendant.Key != listing.ChildKey)

            return SimAnchorRelationUpshot.Rejected;

        if (listing.AncestorInstSeries is { } relationAncestorInst
            && ancestor.Incarnation != relationAncestorInst)
        {
            return KineticStampGate.IsNewer(relationAncestorInst, ancestor.Incarnation)
                ? SimAnchorRelationUpshot.DiscardedStaleParent
                : SimAnchorRelationUpshot.DeferredAwaitingParent;
        }

        SealAncestorAffix(descendant, default, rebaseline: false, buf: null);
        return SimAnchorRelationUpshot.Applied;
    }
}
