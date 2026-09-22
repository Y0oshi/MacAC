using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using MacAC.Assets;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Play;
using System.Numerics;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

// The ordered projection FIFO the host drains and acknowledges, plus the deadlines after which a
// lost cell gives up
internal sealed partial class SimSetPositionLedger
{
    private SortedDictionary<ulong, SimPlacementMirrorCapture> _projFifo = [];

    private readonly List<List<SimPlacementMirrorCapture>> _reattemptTempByZDepth = [new()];

    private int _reattemptZDepth;

    private ulong _projSeq;

    private readonly Dictionary<SimActorKey, double> _lostBy = [];

    private readonly List<LostEntry> _lostHeap = [];

    private readonly Dictionary<SimActorKey, int> _lostHeapOrdinal = [];

    private ulong _lostSeq;

    private readonly LinkedList<SimActorKey> _expiredLost = [];

    private readonly Dictionary<SimActorKey, LinkedListNode<SimActorKey>> _expiredLostJoints = [];

    private readonly HashSet<SimActorPlacementTicket> _wrapUpWatches = [];

    private readonly Dictionary<SimActorPlacementTicket, SimPlacementMirrorTicket> _ackedCompletions = [];

    private readonly record struct LostEntry(
        SimActorKey Key,
        double Deadline,
        ulong Sequence);

    internal int QueuedProjTally => _projFifo.Count;

    internal void ReattemptQueuedProjections()
    {
        Live();
        int zDepth = _reattemptZDepth;
        if (zDepth == _reattemptTempByZDepth.Count)

            _reattemptTempByZDepth.Add([]);
        var capture =
            _reattemptTempByZDepth[zDepth];
        _reattemptZDepth = zDepth + 1;
        try
        {
            capture.Clear();
            foreach (KeyValuePair<ulong, SimPlacementMirrorCapture>
                     listing in _projFifo)
            {
                capture.Add(listing.Value);
            }
            for (int ordinal = 0; ordinal < capture.Count; ++ordinal)
            {
                var proj = capture[ordinal];
                if (_projFifo.TryGetValue(
                        proj.Token.Sequence,
                        out SimPlacementMirrorCapture latest)
                    && latest == proj)

                    BroadcastStance(proj);
            }
        }
        finally
        {
            capture.Clear();
            _reattemptZDepth = zDepth;
        }
    }

    internal SimPlacementMirrorTicket BroadcastExecutorWrapUp(
        SimActorRecord capture,
        Action<SimPlacementMirrorTicket>? priorBroadcast = null,
        SimPortalPlacementAuthority gateway = default)
    {
        Live();
        ArgumentNullException.ThrowIfNull(capture);
        if (capture.Key is not { } tag)
            return default;

        var corpus = capture.KineticBody;
        ulong series = checked(++_projSeq);
        SimPlacementMirrorTicket ticket = new SimPlacementMirrorTicket(
            series,
            Revision: 1UL,
            tag,
            capture.PositionAuthorityVersion,
            capture.SpatialAuthorityVersion,
            capture.PlacementCommitVersion,
            _actors.SessionLifetimeVersion,
            capture.WholeChamberTag,
            _register.AnticipatedImpactGen(capture.WholeChamberTag),
            gateway);
        var snapshot = new SimPlacementMirrorCapture(
            ticket,
            SimPlacementMirrorKind.ExecutorCompleted,
            corpus?.Position ?? Vector3.Zero,
            corpus?.Orientation ?? Quaternion.Identity,
            corpus?.CellPosition.Frame.Origin ?? Vector3.Zero,
            corpus?.InContact ?? false,
            corpus?.OnWalkable ?? false);
        _projFifo.Add(series, snapshot);
        priorBroadcast?.Invoke(ticket);
        BroadcastStance(snapshot);
        return ticket;
    }

    internal bool TryGlimpseProj(
        out SimPlacementMirrorCapture proj)
    {
        Live();
        if (_projFifo.Count is 0)
        {
            proj = default;
            return false;
        }
        proj = HeadProjection().Value;
        return true;
    }

    internal bool AcknowledgeProj(
        in SimPlacementMirrorTicket ticket)
    {
        Live();
        if (!ticket.IsValid
            || _projFifo.Count is 0
            || HeadProjection().Key != ticket.Sequence
            || !_projFifo.TryGetValue(
                ticket.Sequence,
                out SimPlacementMirrorCapture queued)
            || queued.Token != ticket)

            return false;
        if (queued.Kind is SimPlacementMirrorKind.Discard
            or SimPlacementMirrorKind.ExecutorCompleted
            or SimPlacementMirrorKind.WithdrawalRestored)
        {
            _projFifo.Remove(ticket.Sequence);
            RetireLullProj(ticket.Sequence);
            if (queued.Kind is SimPlacementMirrorKind.ExecutorCompleted)
            {
                _onWrapUpAcked?.Invoke(
                    ticket.Entity,
                    ticket.Sequence);
            }
            return true;
        }
        if (!_ops.TryGetValue(
                ticket.Entity,
                out SimOperation? op)
            || op.ProjectionSequence != ticket.Sequence
            || !OpHolds(op)
            || op.SpatialAuthorityVersion
                != ticket.SpatialAuthorityVersion)

            return false;

        _projFifo.Remove(ticket.Sequence);
        RetireLullProj(ticket.Sequence);
        op.ProjectionSequence = 0UL;
        if (queued.Kind is SimPlacementMirrorKind.Place)
        {
            if (_wrapUpWatches.Remove(op.Token))
            {
                _ackedCompletions.Add(
                    op.Token,
                    queued.Token);
            }
            op.Stage = SimActorPlacementStage.AwaitingCommitAcknowledgement;
            _loadingAuthorities.Remove(op.Key);
            bool removed = _ops.Remove(op.Key);
            if (removed)
                YieldOp(op);
            return removed;
        }
        if (!op.WakeableLostCell
            && !op.InheritedLostDeadline)
        {
            _loadingAuthorities.Remove(op.Key);
            bool removed = _ops.Remove(op.Key);
            if (removed)
                YieldOp(op);
            return removed;
        }

        op.WithdrawalAcknowledged = true;
        op.Stage = op.RequiresPreparation
                || op.InheritedLostDeadline
            ? SimActorPlacementStage.AwaitingPreparation
            : op.CollisionQuiescenceHeld
                ? SimActorPlacementStage.QuiescenceHeld
                : SimActorPlacementStage.AwaitingCell;
        if (op.PreparedCommandAwaitingWithdrawalAck is { } readied)
        {
            op.PreparedCommandAwaitingWithdrawalAck = null;
            op.Stage = SimActorPlacementStage.AwaitingPreparation;
            _ = SubmitReadiedStance(op.Token, readied);
            return true;
        }
        if (op.CollisionGenerationReady)
            ReattemptShelved(op);
        return true;
    }

    internal void PulseLostChamberDeadlines()
    {
        Live();
        if (_lostHeap.Count is 0)
            return;
        double instant = _register.MonotonicInstantSecs;
        while (_lostHeap.Count is not 0)
        {
            LostEntry listing = _lostHeap[0];
            if (listing.Deadline > instant)
                break;
            TakeHeapAt(0);
            if (!_lostBy.Remove(listing.Key))
                throw new InvalidOperationException(
                    "The lost-cell deadline index diverged from its exact key owner");
            if (_ops.TryGetValue(
                    listing.Key,
                    out SimOperation? op))
                op.Expired = true;
            if (_actors.TryFetchByOwnTag(
                    listing.Key.LocalEntityId,
                    out SimActorRecord capture)
                && capture.Key == listing.Key)
            {
                if (!_expiredLostJoints.ContainsKey(listing.Key))
                {
                    var joint =
                        _expiredLost.AddLast(listing.Key);
                    _expiredLostJoints.Add(listing.Key, joint);
                }
            }
        }
    }

    internal bool TryDequeueExpiredLostChamber(out SimActorKey tag)
    {
        Live();
        if (_expiredLost.First is not { } lead)
        {
            tag = default;
            return false;
        }
        tag = lead.Value;
        _expiredLost.RemoveFirst();
        _expiredLostJoints.Remove(tag);
        return true;
    }

    private KeyValuePair<ulong, SimPlacementMirrorCapture> HeadProjection()
    {
        foreach (KeyValuePair<ulong, SimPlacementMirrorCapture> listing
                 in _projFifo)
        {
            return listing;
        }
        throw new InvalidOperationException(
            "HeadProjection needs no fewer than one pending entry");
    }

    private void ArmLostForClan(SimOperation op)
    {
        var trunk = op.Record;
        DisarmLostForClan(op);
        double deadline = _register.MonotonicInstantSecs + 25d;
        op.LostFamilyKeys ??= [];
        if (trunk.Key is { } trunkTag)
        {
            op.LostFamilyKeys.Add(trunkTag);
            ArmLost(trunkTag, deadline);
        }
        var descendants = _actors.AncestorAttachments
            .DescendantsAffixedToAncestor(trunk.ServerGuid, trunk.Incarnation);
        for (int ordinal = 0; ordinal < descendants.Count; ++ordinal)
        {
            if (_actors.TryFetchEngaged(
                    descendants[ordinal],
                    out SimActorRecord descendant)
                && descendant.Key is { } descendantTag)
            {
                op.LostFamilyKeys.Add(descendantTag);
                ArmLost(descendantTag, deadline);
            }
        }
    }

    private void DisarmLostForClan(SimOperation op)
    {
        if (op.Record.Key is { } trunkTag)
        {
            DisarmLostDeadline(trunkTag);
            DiscardExpiredLost(trunkTag);
        }
        var latestDescendants = _actors.AncestorAttachments
            .DescendantsAffixedToAncestor(
                op.Record.ServerGuid,
                op.Record.Incarnation);
        for (int ordinal = 0; ordinal < latestDescendants.Count; ++ordinal)
        {
            if (_actors.TryFetchEngaged(
                    latestDescendants[ordinal],
                    out SimActorRecord descendant)
                && descendant.Key is { } tag)
            {
                DisarmLostDeadline(tag);
                DiscardExpiredLost(tag);
            }
        }
        op.LostFamilyKeys?.Clear();
    }

    private void DisarmLost(SimActorKey tag)
    {
        DisarmLostDeadline(tag);
        DiscardExpiredLost(tag);
        foreach (SimOperation op in _ops.Values)
            op.LostFamilyKeys?.Remove(tag);
    }

    private void DiscardExpiredLost(SimActorKey tag)
    {
        if (!_expiredLostJoints.Remove(
                tag,
                out LinkedListNode<SimActorKey>? joint))

            return;
        _expiredLost.Remove(joint);
    }

    private void ArmLost(SimActorKey tag, double deadline)
    {
        TakeHeapTag(tag);
        _lostBy[tag] = deadline;
        int ordinal = _lostHeap.Count;
        _lostHeap.Add(new LostEntry(
            tag,
            deadline,
            checked(++_lostSeq)));
        _lostHeapOrdinal.Add(tag, ordinal);
        SiftUp(ordinal);
    }

    private void DisarmLostDeadline(SimActorKey tag)
    {
        _lostBy.Remove(tag);
        TakeHeapTag(tag);
    }

    private static bool Sooner(
        in LostEntry contender,
        in LostEntry latest)
    {
        return contender.Deadline < latest.Deadline
        || (contender.Deadline == latest.Deadline
            && contender.Sequence < latest.Sequence);
    }

    private void SiftUp(int ordinal)
    {
        while (ordinal > 0)
        {
            int ancestor = (ordinal - 1) / 2;
            if (!Sooner(
                    _lostHeap[ordinal],
                    _lostHeap[ancestor]))
                break;
            SwapHeap(ordinal, ancestor);
            ordinal = ancestor;
        }
    }

    private void SiftDown(int ordinal)
    {
        while (true)
        {
            int left = checked(ordinal * 2 + 1);
            if (left >= _lostHeap.Count)
                return;
            int right = left + 1;
            int earlier = right < _lostHeap.Count
                && Sooner(
                    _lostHeap[right],
                    _lostHeap[left])
                    ? right
                    : left;
            if (!Sooner(
                    _lostHeap[earlier],
                    _lostHeap[ordinal]))
                return;
            SwapHeap(ordinal, earlier);
            ordinal = earlier;
        }
    }

    private void SwapHeap(int lead, int second)
    {
        LostEntry temporary = _lostHeap[lead];
        _lostHeap[lead] = _lostHeap[second];
        _lostHeap[second] = temporary;
        _lostHeapOrdinal[_lostHeap[lead].Key] = lead;
        _lostHeapOrdinal[_lostHeap[second].Key] = second;
    }

    private void TakeHeapTag(SimActorKey tag)
    {
        if (_lostHeapOrdinal.TryGetValue(tag, out int ordinal))
            TakeHeapAt(ordinal);
    }

    private void TakeHeapAt(int ordinal)
    {
        LostEntry removed = _lostHeap[ordinal];
        _lostHeapOrdinal.Remove(removed.Key);
        int previous = _lostHeap.Count - 1;
        if (ordinal != previous)
        {
            LostEntry moved = _lostHeap[previous];
            _lostHeap[ordinal] = moved;
            _lostHeapOrdinal[moved.Key] = ordinal;
        }
        _lostHeap.RemoveAt(previous);
        if (ordinal >= _lostHeap.Count)
            return;
        int ancestor = ordinal is 0 ? -1 : (ordinal - 1) / 2;
        if (ancestor >= 0
            && Sooner(
                _lostHeap[ordinal],
                _lostHeap[ancestor]))
        {
            SiftUp(ordinal);
        }
        else
        {
            SiftDown(ordinal);
        }
    }
}
