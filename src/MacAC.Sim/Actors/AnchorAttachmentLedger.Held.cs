using System.Collections.Immutable;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class AnchorAttachmentLedger
{
    private readonly Dictionary<uint, Queue<HeldAnchorCreate>> _pinnedCreatesByAncestor = [];

    private readonly Dictionary<uint, Queue<HeldGrantedAnchorRelation>> _pinnedRelationsByAncestor = [];

    private ulong _nextDeferredCreateAdmissionId;

    private sealed class CreateWindow
    {
        internal required uint AncestorOid { get; init; }
        internal List<Func<HeldAnchorCreate, bool>> Filters { get; } = [];
    }

    private sealed class RelationWindow
    {
        internal required uint AncestorOid { get; init; }
        internal List<Func<HeldGrantedAnchorRelation, bool>> Filters { get; } = [];
    }

    private readonly Dictionary<ulong, CreateWindow> _openBuildPanes = [];

    private readonly Dictionary<ulong, RelationWindow> _openRelationPanes = [];

    private ulong _paneIdents;

    internal int PostponedBuildTally =>
        _pinnedCreatesByAncestor.Values.Sum(fifo => fifo.Count);

    internal int PostponedApprovedRelationTally =>
        _pinnedRelationsByAncestor.Values.Sum(fifo => fifo.Count);

    internal void EnqueueDeferredCreate(
        RealmSession.MoverSpawn spawn,
        bool isOwnAvatar)
    {
        uint ancestorOid = spawn.ParentGuid
            ?? spawn.Physics?.Parent?.Guid
            ?? 0u;
        if (spawn.Guid is 0u || ancestorOid is 0u)
        {
            throw new ArgumentException(
                "A deferred parent ObjectCreation needs nonzero child and parent GUIDs",
                nameof(spawn));
        }
        if (_nextDeferredCreateAdmissionId == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The deferred parent ObjectCreation admission sequence is exhausted");
        }
        if (!_pinnedCreatesByAncestor.TryGetValue(
                ancestorOid,
                out Queue<HeldAnchorCreate>? fifo))
        {
            fifo = new Queue<HeldAnchorCreate>();
            _pinnedCreatesByAncestor.Add(ancestorOid, fifo);
        }
        ulong admissionIdent = _nextDeferredCreateAdmissionId + 1UL;
        fifo.Enqueue(new HeldAnchorCreate(
            admissionIdent,
            SimSpawnIntakeLocker.Freeze(spawn),
            isOwnAvatar));
        _nextDeferredCreateAdmissionId = admissionIdent;
    }

    internal bool TryGlimpsePostponedBuild(
        uint ancestorOid,
        out HeldAnchorCreate postponed)
    {
        if (!_pinnedCreatesByAncestor.TryGetValue(
                ancestorOid,
                out Queue<HeldAnchorCreate>? fifo)
            || !fifo.TryPeek(out postponed))
        {
            postponed = default;
            return false;
        }
        return true;
    }

    internal bool AbsorbPostponedBuild(
        uint ancestorOid,
        in HeldAnchorCreate anticipated)
    {
        if (!_pinnedCreatesByAncestor.TryGetValue(
                ancestorOid,
                out Queue<HeldAnchorCreate>? fifo)
            || !fifo.TryPeek(out HeldAnchorCreate latest)
            || latest != anticipated)

            return false;
        _ = fifo.Dequeue();
        if (fifo.Count is 0)
            _pinnedCreatesByAncestor.Remove(ancestorOid);
        return true;
    }

    internal ImmutableArray<HeldAnchorCreate> UnfastenPostponedCreates(
        uint ancestorOid,
        out HeldRerunWindowTicket pane)
    {
        if (!_pinnedCreatesByAncestor.Remove(
                ancestorOid,
                out Queue<HeldAnchorCreate>? fifo))
        {
            pane = default;
            return ImmutableArray<HeldAnchorCreate>.Empty;
        }
        ulong ident = ++_paneIdents;
        _openBuildPanes[ident] = new CreateWindow { AncestorOid = ancestorOid };
        pane = new HeldRerunWindowTicket(ident, ancestorOid, HeldRerunBucketKind.Creates);
        return [.. fifo];
    }

    internal void ReinstatePostponedCreates(
        in HeldRerunWindowTicket pane,
        ReadOnlySpan<HeldAnchorCreate> listings)
    {
        if (pane.Kind != HeldRerunBucketKind.Creates
            || !_openBuildPanes.Remove(pane.Id, out CreateWindow? phase))

            return;
        if (listings.Length is 0)
            return;
        IEnumerable<HeldAnchorCreate> filtered = listings.ToArray();
        foreach (Func<HeldAnchorCreate, bool> sift in phase.Filters)
            filtered = filtered.Where(sift);
        var survivors = filtered.ToArray();
        if (survivors.Length is 0)
            return;
        var restored = new Queue<HeldAnchorCreate>(survivors.Length);
        foreach (HeldAnchorCreate listing in survivors)
            restored.Enqueue(listing);
        if (_pinnedCreatesByAncestor.TryGetValue(
                pane.ParentGuid,
                out Queue<HeldAnchorCreate>? extant))
        {
            foreach (HeldAnchorCreate listing in extant)
                restored.Enqueue(listing);
        }
        _pinnedCreatesByAncestor[pane.ParentGuid] = restored;
    }

    internal bool ContainsPostponedBuild(
        uint descendantOid,
        ushort instSeries)
    {
        foreach (Queue<HeldAnchorCreate> fifo
            in _pinnedCreatesByAncestor.Values)
        {
            if (fifo.Any(contender =>
                    contender.Spawn.Guid == descendantOid
                    && contender.Spawn.InstanceSequence == instSeries))

                return true;
        }
        return false;
    }

    internal void QueuePostponedApprovedRelation(
        uint descendantOid,
        SimActorKey descendantTag,
        AncestorSignal.Parsed? standalone,
        CreateAnchorUpdate? envelope,
        GrantedKineticsTimestamps approvedTimestamps)
    {
        uint ancestorOid = standalone?.ParentGuid ?? envelope?.ParentGuid ?? 0u;
        if (ancestorOid is 0u || descendantOid is 0u)
        {
            throw new ArgumentException(
                "A deferred accepted parent relation needs nonzero parent and child GUIDs");
        }
        if (_nextDeferredCreateAdmissionId == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                "The deferred parent ObjectCreation admission sequence is exhausted");
        }
        ulong admissionIdent = _nextDeferredCreateAdmissionId + 1UL;
        QueuePostponedApprovedRelation(new HeldGrantedAnchorRelation(
            admissionIdent, descendantOid, descendantTag, standalone, envelope, approvedTimestamps));
        _nextDeferredCreateAdmissionId = admissionIdent;
    }

    internal void QueuePostponedApprovedRelation(
        in HeldGrantedAnchorRelation relation)
    {
        uint ancestorOid = relation.Standalone?.ParentGuid
            ?? relation.Envelope?.ParentGuid
            ?? 0u;
        if (!_pinnedRelationsByAncestor.TryGetValue(
                ancestorOid,
                out Queue<HeldGrantedAnchorRelation>? fifo))
        {
            fifo = new Queue<HeldGrantedAnchorRelation>();
            _pinnedRelationsByAncestor.Add(ancestorOid, fifo);
        }
        fifo.Enqueue(relation);
    }

    internal ImmutableArray<HeldGrantedAnchorRelation> UnfastenPostponedApprovedRelations(
        uint ancestorOid,
        out HeldRerunWindowTicket pane)
    {
        if (!_pinnedRelationsByAncestor.Remove(
                ancestorOid,
                out Queue<HeldGrantedAnchorRelation>? fifo))
        {
            pane = default;
            return ImmutableArray<HeldGrantedAnchorRelation>.Empty;
        }
        ulong ident = ++_paneIdents;
        _openRelationPanes[ident] = new RelationWindow { AncestorOid = ancestorOid };
        pane = new HeldRerunWindowTicket(ident, ancestorOid, HeldRerunBucketKind.AcceptedRelations);
        return [.. fifo];
    }

    internal void ReinstatePostponedApprovedRelations(
        in HeldRerunWindowTicket pane,
        ReadOnlySpan<HeldGrantedAnchorRelation> listings)
    {
        if (pane.Kind != HeldRerunBucketKind.AcceptedRelations
            || !_openRelationPanes.Remove(pane.Id, out RelationWindow? phase))

            return;
        if (listings.Length is 0)
            return;
        IEnumerable<HeldGrantedAnchorRelation> filtered = listings.ToArray();
        foreach (Func<HeldGrantedAnchorRelation, bool> sift in phase.Filters)
            filtered = filtered.Where(sift);
        var survivors = filtered.ToArray();
        if (survivors.Length is 0)
            return;
        var restored = new Queue<HeldGrantedAnchorRelation>(survivors.Length);
        foreach (HeldGrantedAnchorRelation listing in survivors)
            restored.Enqueue(listing);
        if (_pinnedRelationsByAncestor.TryGetValue(
                pane.ParentGuid,
                out Queue<HeldGrantedAnchorRelation>? extant))
        {
            foreach (HeldGrantedAnchorRelation listing in extant)
                restored.Enqueue(listing);
        }
        _pinnedRelationsByAncestor[pane.ParentGuid] = restored;
    }

    internal bool ContainsPostponedApprovedRelation(
        uint descendantOid,
        SimActorKey descendantTag)
    {
        foreach (Queue<HeldGrantedAnchorRelation> fifo
            in _pinnedRelationsByAncestor.Values)
        {
            if (fifo.Any(contender =>
                    contender.ChildGuid == descendantOid
                    && contender.ChildKey == descendantTag))

                return true;
        }
        return false;
    }

    internal void AbortPostponedDescendantGen(
        uint descendantOid,
        ushort terminalInstSeries)
    {
        SiftPinnedCreates(
            contender => contender.Spawn.Guid != descendantOid
                || KineticStampGate.IsNewer(
                    terminalInstSeries,
                    contender.Spawn.InstanceSequence));
        SiftPinnedRelations(
            contender => contender.ChildGuid != descendantOid
                || KineticStampGate.IsNewer(
                    terminalInstSeries,
                    contender.ChildKey.Incarnation));
    }

    private void DiscardPinnedCreatesOfDescendant(uint descendantOid)
    {
        SiftPinnedCreates(
                contender => contender.Spawn.Guid != descendantOid);
    }

    private void DiscardPinnedRelationsOfDescendant(uint descendantOid)
    {
        SiftPinnedRelations(
                contender => contender.ChildGuid != descendantOid);
    }

    private void SiftPinnedCreates(
        Func<HeldAnchorCreate, bool> retain)
    {
        uint[] parents = _pinnedCreatesByAncestor.Keys.ToArray();
        for (int ordinal = 0; ordinal < parents.Length; ++ordinal)
        {
            uint ancestorOid = parents[ordinal];
            Queue<HeldAnchorCreate> kept = new(
                _pinnedCreatesByAncestor[ancestorOid].Where(retain));
            if (kept.Count is 0)
                _pinnedCreatesByAncestor.Remove(ancestorOid);
            else
                _pinnedCreatesByAncestor[ancestorOid] = kept;
        }
        foreach (CreateWindow phase in _openBuildPanes.Values)
            phase.Filters.Add(retain);
    }

    private void SiftPinnedRelations(
        Func<HeldGrantedAnchorRelation, bool> retain)
    {
        uint[] parents = _pinnedRelationsByAncestor.Keys.ToArray();
        for (int ordinal = 0; ordinal < parents.Length; ++ordinal)
        {
            uint ancestorOid = parents[ordinal];
            Queue<HeldGrantedAnchorRelation> kept = new(
                _pinnedRelationsByAncestor[ancestorOid].Where(retain));
            if (kept.Count is 0)
                _pinnedRelationsByAncestor.Remove(ancestorOid);
            else
                _pinnedRelationsByAncestor[ancestorOid] = kept;
        }
        foreach (RelationWindow phase in _openRelationPanes.Values)
            phase.Filters.Add(retain);
    }
}
