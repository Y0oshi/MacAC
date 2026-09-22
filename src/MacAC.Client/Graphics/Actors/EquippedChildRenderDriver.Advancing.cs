using MacAC.Client.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics;

public sealed partial class EquippedChildRenderDriver
{
    private void ProgressDetachedDeletion(
        SimActorKey descendantTag,
        PendingMirrorSubtree queued)
    {
        ProgressProjSubtree(
            _pendingDetachedRemovalByChild,
            descendantTag,
            queued);
    }

    private DescendantUnparentDisposition ProgressUnparentChangeover(
        SimActorKey descendantTag,
        QueuedUnparentChangeover queued)
    {
        var verdict = ProgressProjSubtree(
            queued.Subtree,
            out PendingMirrorSubtree upcoming);

        if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending)
        {
            _pendingUnparentByChild[descendantTag] = queued with { Subtree = upcoming };
            return verdict.Failure is not null ? throw verdict.Failure : DescendantUnparentDisposition.Pending;
        }

        if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Superseded)
        {
            _pendingUnparentByChild.Remove(descendantTag);
            return verdict.Failure is not null ? throw verdict.Failure : DescendantUnparentDisposition.Superseded;
        }

        var expectingContinuation = queued with { Subtree = upcoming };
        _pendingUnparentByChild[descendantTag] = expectingContinuation;
        if (verdict.Failure is not null)
            throw verdict.Failure;

        Relations.FinishDescendantProj(queued.Record.ServerOid);
        try
        {
            expectingContinuation.Continuation?.Invoke();
            _pendingUnparentByChild.Remove(descendantTag);
            return DescendantUnparentDisposition.Completed;
        }
        catch
        {
            bool continuationSealed =
                _onlineActors.IsLatestCapture(expectingContinuation.Record)
                && expectingContinuation.Record.PositionAuthorityVersion
                    == expectingContinuation.PositionAuthorityVersion
                && expectingContinuation.Record.IsSpatiallyProjected
                && expectingContinuation.Record.ProjSort
                    is OnlineActorMirrorKind.World;
            if (continuationSealed)
                _pendingUnparentByChild.Remove(descendantTag);
            else
                _pendingUnparentByChild[descendantTag] = expectingContinuation;
            throw;
        }
    }

    private void ProgressPlainDeletion(
        SimActorKey trunkTag,
        QueuedPlainDeletion queued)
    {
        if (!_onlineActors.IsLatestCapture(queued.RootRecord))
        {
            _pendingOrdinaryRemovalByRoot.Remove(trunkTag);
            return;
        }

        for (int idx = queued.NextIndex; idx < queued.Captures.Count; ++idx)
        {
            var grabbed = queued.Captures[idx];
            var verdict = WithdrawGrabbed(grabbed);
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending)
            {
                _pendingOrdinaryRemovalByRoot[trunkTag] = queued with { NextIndex = idx };
                if (verdict.Failure is not null)
                    throw verdict.Failure;
                return;
            }

            if (!ReferenceEquals(grabbed.Attached.ChildRecord, queued.RootRecord)
                && verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed)

                Relations.ReinstatePreviousApproved(grabbed.Attached.ChildGuid);

            if (verdict.Failure is not null)
            {
                _pendingOrdinaryRemovalByRoot[trunkTag] = queued with
                {
                    NextIndex = idx + 1,
                };
                throw verdict.Failure;
            }
        }

        _pendingOrdinaryRemovalByRoot.Remove(trunkTag);
        Relations.FinishDescendantProj(queued.RootRecord.ServerOid);
    }

    private bool ProgressProjSubtree(
        Dictionary<SimActorKey, PendingMirrorSubtree> queuedByTrunk,
        SimActorKey trunkTag,
        PendingMirrorSubtree queued)
    {
        var verdict = ProgressProjSubtree(
            queued,
            out PendingMirrorSubtree upcoming);
        bool reattempt = verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending
            || (verdict.Failure is not null && upcoming.NextIndex < upcoming.Captures.Count);
        if (reattempt)
            queuedByTrunk[trunkTag] = upcoming;
        else
            queuedByTrunk.Remove(trunkTag);
        return verdict.Failure is not null
            ? throw verdict.Failure
            : verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed
            && upcoming.NextIndex >= upcoming.Captures.Count;
    }

    private ExactMirrorWithdrawalVerdict ProgressProjSubtree(
        PendingMirrorSubtree queued,
        out PendingMirrorSubtree upcoming)
    {
        upcoming = queued;
        if (!_onlineActors.IsLatestCapture(queued.RootRecord)
            || queued.RootRecord.PositionAuthorityVersion
                != queued.RootPositionAuthorityVersion)
        {
            return new(
                ExactMirrorWithdrawalDisposition.Superseded,
                Failure: null);
        }

        for (int idx = queued.NextIndex; idx < queued.Captures.Count; ++idx)
        {
            var grabbed = queued.Captures[idx];
            var verdict = WithdrawProjGrab(grabbed);
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Pending)
            {
                upcoming = queued with { NextIndex = idx };
                return verdict;
            }
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Superseded
                && grabbed.IsRoot)
            {
                upcoming = queued with { NextIndex = idx + 1 };
                return verdict;
            }
            if (verdict.Disposition is ExactMirrorWithdrawalDisposition.Completed
                && grabbed.Attached is not null
                && (grabbed.IsRoot
                    ? queued.RestoreRootRelation
                    : queued.RestoreDescendantRelations))

                Relations.ReinstatePreviousApproved(grabbed.Record.ServerOid);
            upcoming = queued with { NextIndex = idx + 1 };
            if (verdict.Failure is not null)
                return verdict;
        }

        return new(
            ExactMirrorWithdrawalDisposition.Completed,
            Failure: null);
    }
}
