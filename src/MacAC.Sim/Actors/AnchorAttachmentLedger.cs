using System.Collections.Immutable;
using MacAC.Wire;
using MacAC.Wire.Messages;
using MacAC.Mechanics.Kinetics;

namespace MacAC.Sim.Actors;

public sealed partial class AnchorAttachmentLedger
{
    private readonly Dictionary<uint, Queue<AnchorAttachmentRelation>> _expectingAncestorByDescendant = new();

    private readonly Dictionary<uint, AnchorAttachmentRelation> _projectedByDescendant = new();

    private readonly Dictionary<uint, AnchorAttachmentRelation> _backupByDescendant = new();

    private readonly Dictionary<uint, AnchorAttachmentRelation> _sealedByDescendant = new();

    private readonly Dictionary<AncestorIncarnation, List<uint>> _descendantsOfAncestor = new();

    private readonly HashSet<uint> _refusalsLogged = [];

    public int UnresolvedRelationTally =>
        _expectingAncestorByDescendant.Values.Sum(fifo => fifo.Count);

    public int LinedRelationTally => _projectedByDescendant.Count;

    public int RecoveryRelationTally => _backupByDescendant.Count;

    public int SealedRelationTally => _sealedByDescendant.Count;

    public void AdmitBuildObjectRelation(AnchorAttachmentRelation relation)
    {
        _projectedByDescendant[relation.ChildGuid] = relation;
    }

    public void Enqueue(AncestorSignal.Parsed refresh)
    {
        if (!_expectingAncestorByDescendant.TryGetValue(refresh.ChildGuid, out Queue<AnchorAttachmentRelation>? fifo))
        {
            fifo = new Queue<AnchorAttachmentRelation>();
            _expectingAncestorByDescendant.Add(refresh.ChildGuid, fifo);
        }

        fifo.Enqueue(new AnchorAttachmentRelation(
            refresh.ParentGuid,
            refresh.ChildGuid,
            refresh.ParentLocation,
            refresh.PlacementId,
            refresh.ParentInstanceSequence,
            refresh.ChildPositionSequence));
    }

    public void Resolve(
        uint descendantOid,
        Func<uint, bool> isObjectRecognized,
        Func<uint, ushort?> locateInst,
        Func<AncestorSignal.Parsed, bool> admit)
    {
        if (_projectedByDescendant.ContainsKey(descendantOid))
            return;
        if (!_expectingAncestorByDescendant.TryGetValue(descendantOid, out Queue<AnchorAttachmentRelation>? fifo))
            return;
        int contenderTally = fifo.Count;
        for (int idx = 0; idx < contenderTally; ++idx)
        {
            var relation = fifo.Dequeue();
            ushort? ancestorInst = locateInst(relation.ParentGuid);
            if (ancestorInst is null)
            {
                fifo.Enqueue(relation with { WaitOwner = AnchorAttachmentWaitHolder.Parent });
                continue;
            }

            if (ancestorInst.Value != relation.ParentInstanceSequence)
            {
                if (KineticStampGate.IsNewer(
                        relation.ParentInstanceSequence,
                        ancestorInst.Value))

                    continue;

                fifo.Enqueue(relation with { WaitOwner = AnchorAttachmentWaitHolder.Parent });
                continue;
            }

            if (!isObjectRecognized(descendantOid))
            {
                fifo.Enqueue(relation with { WaitOwner = AnchorAttachmentWaitHolder.Child });
                continue;
            }

            AncestorSignal.Parsed refresh = new AncestorSignal.Parsed(
                relation.ParentGuid,
                relation.ChildGuid,
                relation.ParentLocation,
                relation.PlacementId,
                relation.ParentInstanceSequence,
                relation.ChildPositionSequence);
            if (!admit(refresh))
                continue;

            _projectedByDescendant[descendantOid] = relation with
            {
                WaitOwner = AnchorAttachmentWaitHolder.Unknown,
            };
            break;
        }

        if (fifo.Count is 0)
            _expectingAncestorByDescendant.Remove(descendantOid);
    }

    public bool TryFetchProj(uint descendantOid, out AnchorAttachmentRelation relation)
    {
        return _projectedByDescendant.TryGetValue(descendantOid, out relation)
        || _backupByDescendant.TryGetValue(descendantOid, out relation);
    }

    public bool TryFetchLinedProj(
        uint descendantOid,
        out AnchorAttachmentRelation relation) =>
        _projectedByDescendant.TryGetValue(descendantOid, out relation);

    public bool TryFetchRecoveryProj(
        uint descendantOid,
        out AnchorAttachmentRelation relation) =>
        _backupByDescendant.TryGetValue(descendantOid, out relation);

    public void DuplicateQueuedProjDescendantsTo(List<uint> dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        dest.Clear();
        foreach (uint descendantOid in _projectedByDescendant.Keys)
            dest.Add(descendantOid);
        foreach (uint descendantOid in _backupByDescendant.Keys)
        {
            if (!_projectedByDescendant.ContainsKey(descendantOid))
                dest.Add(descendantOid);
        }
    }

    public bool IsSealed(AnchorAttachmentRelation relation)
    {
        return _sealedByDescendant.TryGetValue(
            relation.ChildGuid,
            out AnchorAttachmentRelation committed)
        && committed == relation;
    }

    public bool HasSealedAncestor(uint descendantOid) =>
        _sealedByDescendant.ContainsKey(descendantOid);

    public bool TryFetchSealedAncestor(
        uint descendantOid,
        out uint ancestorOid,
        out ushort ancestorInstSeries)
    {
        if (_sealedByDescendant.TryGetValue(
                descendantOid,
                out AnchorAttachmentRelation relation))
        {
            ancestorOid = relation.ParentGuid;
            ancestorInstSeries = relation.ParentInstanceSequence;
            return true;
        }
        ancestorOid = 0u;
        ancestorInstSeries = 0;
        return false;
    }

    public bool IsPending(
        AnchorAttachmentRelation relation,
        AnchorMirrorCandidateKind sort)
    {
        return (sort is AnchorMirrorCandidateKind.Staged
            ? _projectedByDescendant
            : _backupByDescendant).TryGetValue(
                relation.ChildGuid,
                out AnchorAttachmentRelation queued)
        && queued == relation;
    }

    public void FlagProjected(
        AnchorAttachmentRelation relation,
        AnchorMirrorCandidateKind sort)
    {
        Dictionary<uint, AnchorAttachmentRelation> src =
            sort is AnchorMirrorCandidateKind.Staged
                ? _projectedByDescendant
                : _backupByDescendant;
        if (src.TryGetValue(
                relation.ChildGuid,
                out AnchorAttachmentRelation queued)
            && queued == relation)

            src.Remove(relation.ChildGuid);
    }

    public bool CanSealIncarnation(
        AnchorAttachmentRelation relation,
        Func<uint, ushort?>? locateAncestorInst)
    {
        if (locateAncestorInst is null
            || locateAncestorInst(relation.ParentGuid) is not { } onlineAncestorInst
            || onlineAncestorInst == relation.ParentInstanceSequence)

            return true;

        if (_refusalsLogged.Add(relation.ChildGuid))
        {
            Console.Error.WriteLine(
                $"[parent-attach] refused: child=0x{relation.ChildGuid:X8} names " +
                $"parent 0x{relation.ParentGuid:X8} incarnation " +
                $"{relation.ParentInstanceSequence}, but the parent's live " +
                $"incarnation is {onlineAncestorInst}. " +
                "Logged once for this child; further refusals for the same " +
                "child are suppressed");
        }
        return false;
    }

    public bool SealProj(
        AnchorAttachmentRelation relation,
        Func<uint, ushort?>? locateAncestorInst = null)
    {
        if (!_projectedByDescendant.TryGetValue(
                relation.ChildGuid,
                out AnchorAttachmentRelation lined)
            || lined != relation)

            return false;

        if (!CanSealIncarnation(relation, locateAncestorInst))
            return false;

        UncommitDescendant(relation.ChildGuid);
        _sealedByDescendant[relation.ChildGuid] = relation;
        AncestorIncarnation ancestor = new AncestorIncarnation(
            relation.ParentGuid,
            relation.ParentInstanceSequence);
        if (!_descendantsOfAncestor.TryGetValue(
                ancestor,
                out List<uint>? descendants))
        {
            descendants = [];
            _descendantsOfAncestor.Add(ancestor, descendants);
        }
        descendants.Add(relation.ChildGuid);
        _projectedByDescendant.Remove(relation.ChildGuid);
        _backupByDescendant[relation.ChildGuid] = relation;
        return true;
    }

    public void RejectProj(AnchorAttachmentRelation relation)
    {
        if (_projectedByDescendant.TryGetValue(
                relation.ChildGuid,
                out AnchorAttachmentRelation lined)
            && lined == relation)

            _projectedByDescendant.Remove(relation.ChildGuid);
    }

    public bool ReinstatePreviousApproved(uint descendantOid)
    {
        if (!_sealedByDescendant.TryGetValue(descendantOid, out AnchorAttachmentRelation relation))
            return false;
        _backupByDescendant[descendantOid] = relation;
        return true;
    }

    public IReadOnlyList<uint> DescendantsWaitingForAncestor(uint ancestorOid)
    {
        HashSet<uint> outcome = new HashSet<uint>();
        foreach ((uint descendantOid, AnchorAttachmentRelation relation) in _projectedByDescendant)
        {
            if (relation.ParentGuid == ancestorOid)
                outcome.Add(descendantOid);
        }

        foreach ((uint descendantOid, AnchorAttachmentRelation relation) in _backupByDescendant)
        {
            if (relation.ParentGuid == ancestorOid)
                outcome.Add(descendantOid);
        }

        foreach ((uint descendantOid, Queue<AnchorAttachmentRelation> fifo) in _expectingAncestorByDescendant)
        {
            if (fifo.Any(relation => relation.ParentGuid == ancestorOid))
                outcome.Add(descendantOid);
        }

        return outcome.ToArray();
    }

    public IReadOnlyList<uint> DescendantsUnresolvedForAncestor(uint ancestorOid)
    {
        List<uint>? outcome = null;
        foreach ((uint descendantOid, Queue<AnchorAttachmentRelation> fifo) in _expectingAncestorByDescendant)
        {
            if (fifo.Any(relation => relation.ParentGuid == ancestorOid))
            {
                outcome ??= new List<uint>();
                outcome.Add(descendantOid);
            }
        }
        return (IReadOnlyList<uint>?)outcome ?? Array.Empty<uint>();
    }

    public IReadOnlyList<uint> DescendantsAffixedToAncestor(
        uint ancestorOid,
        ushort ancestorInstSeries)
    {
        AncestorIncarnation ancestor = new AncestorIncarnation(ancestorOid, ancestorInstSeries);
        return _descendantsOfAncestor.TryGetValue(
            ancestor,
            out List<uint>? descendants)
                ? descendants
                : Array.Empty<uint>();
    }

    public void DropObject(uint oid)
    {
        DiscardPinnedCreatesOfDescendant(oid);
        DiscardPinnedRelationsOfDescendant(oid);
        _projectedByDescendant.Remove(oid);
        _backupByDescendant.Remove(oid);
        UncommitDescendant(oid);
        _expectingAncestorByDescendant.Remove(oid);

        DiscardAncestorFrom(_projectedByDescendant, oid);
        DiscardAncestorFrom(_backupByDescendant, oid);
        DropAncestorEverywhere(oid);

        uint[] descendants = _expectingAncestorByDescendant.Keys.ToArray();
        for (int idx = 0; idx < descendants.Length; ++idx)
        {
            uint descendantOid = descendants[idx];
            Queue<AnchorAttachmentRelation> kept = new(
                _expectingAncestorByDescendant[descendantOid].Where(relation => relation.ParentGuid != oid));
            if (kept.Count is 0)
                _expectingAncestorByDescendant.Remove(descendantOid);
            else
                _expectingAncestorByDescendant[descendantOid] = kept;
        }
    }

    public void FinishGen(uint oid, ushort substituteGen)
    {
        SiftPinnedCreates(contender =>
            contender.Spawn.Guid != oid
            || contender.Spawn.InstanceSequence == substituteGen
            || KineticStampGate.IsNewer(
                substituteGen,
                contender.Spawn.InstanceSequence));
        SiftPinnedRelations(contender =>
            contender.ChildGuid != oid
            || contender.ChildKey.Incarnation == substituteGen
            || KineticStampGate.IsNewer(
                substituteGen,
                contender.ChildKey.Incarnation));
        SiftByDescendant(
            oid,
            relation => relation.WaitOwner is AnchorAttachmentWaitHolder.Parent);
        _projectedByDescendant.Remove(oid);
        _backupByDescendant.Remove(oid);
        UncommitDescendant(oid);
        DiscardAncestorFrom(_projectedByDescendant, oid);
        DiscardAncestorFrom(_backupByDescendant, oid);
        DropAncestorEverywhere(oid);
        SiftByAncestor(
            oid,
            relation => relation.ParentInstanceSequence == substituteGen
                || KineticStampGate.IsNewer(
                    substituteGen,
                    relation.ParentInstanceSequence));
    }

    public void EraseGen(uint oid, ushort deletedGen)
    {
        AbortPostponedDescendantGen(oid, deletedGen);
        SiftByDescendant(
            oid,
            relation => relation.WaitOwner is AnchorAttachmentWaitHolder.Parent);
        _projectedByDescendant.Remove(oid);
        _backupByDescendant.Remove(oid);
        UncommitDescendant(oid);
        DiscardAncestorFrom(_projectedByDescendant, oid);
        DiscardAncestorFrom(_backupByDescendant, oid);
        DropAncestorEverywhere(oid);
        SiftByAncestor(
            oid,
            relation => KineticStampGate.IsNewer(
                deletedGen,
                relation.ParentInstanceSequence));
    }

    public void FinishDescendantProj(uint descendantOid)
    {
        _projectedByDescendant.Remove(descendantOid);
        _backupByDescendant.Remove(descendantOid);
        UncommitDescendant(descendantOid);
    }

    public void DeleteDescendant(uint descendantOid)
    {
        DiscardPinnedCreatesOfDescendant(descendantOid);
        DiscardPinnedRelationsOfDescendant(descendantOid);
        _projectedByDescendant.Remove(descendantOid);
        _backupByDescendant.Remove(descendantOid);
        UncommitDescendant(descendantOid);
        _expectingAncestorByDescendant.Remove(descendantOid);
        _refusalsLogged.Remove(descendantOid);
    }

    public void Clear()
    {
        _pinnedCreatesByAncestor.Clear();
        _pinnedRelationsByAncestor.Clear();
        _openBuildPanes.Clear();
        _openRelationPanes.Clear();
        _expectingAncestorByDescendant.Clear();
        _projectedByDescendant.Clear();
        _backupByDescendant.Clear();
        _sealedByDescendant.Clear();
        foreach (List<uint> descendants in _descendantsOfAncestor.Values)
            descendants.Clear();
        _descendantsOfAncestor.Clear();
        _refusalsLogged.Clear();
    }

    private void UncommitDescendant(uint descendantOid)
    {
        if (!_sealedByDescendant.Remove(
                descendantOid,
                out AnchorAttachmentRelation relation))

            return;

        AncestorIncarnation ancestor = new AncestorIncarnation(
            relation.ParentGuid,
            relation.ParentInstanceSequence);
        if (!_descendantsOfAncestor.TryGetValue(
                ancestor,
                out List<uint>? descendants))

            return;

        int descendantOrdinal = descendants.IndexOf(descendantOid);
        if (descendantOrdinal >= 0)
        {
            UncommitDescendantBranch(descendants, descendantOrdinal);
        }

        if (descendants.Count is 0)
            _descendantsOfAncestor.Remove(ancestor);
    }

    private void UncommitDescendantBranch(List<uint> descendants, int descendantOrdinal)
    {
        int previousOrdinal = descendants.Count - 1;
        descendants[descendantOrdinal] = descendants[previousOrdinal];
        descendants.RemoveAt(previousOrdinal);
    }

    private void DropAncestorEverywhere(uint ancestorOid)
    {
        uint[] descendants = _sealedByDescendant
            .Where(duo => duo.Value.ParentGuid == ancestorOid)
            .Select(duo => duo.Key)
            .ToArray();
        for (int idx = 0; idx < descendants.Length; ++idx)
            UncommitDescendant(descendants[idx]);
    }

    private static void DiscardAncestorFrom(
        Dictionary<uint, AnchorAttachmentRelation> relations,
        uint ancestorOid)
    {
        uint[] descendants = relations
            .Where(duo => duo.Value.ParentGuid == ancestorOid)
            .Select(duo => duo.Key)
            .ToArray();
        for (int idx = 0; idx < descendants.Length; ++idx)
            relations.Remove(descendants[idx]);
    }

    private void SiftByAncestor(
        uint ancestorOid,
        Func<AnchorAttachmentRelation, bool> retain)
    {
        uint[] descendants = _expectingAncestorByDescendant.Keys.ToArray();
        for (int idx = 0; idx < descendants.Length; ++idx)
        {
            uint descendantOid = descendants[idx];
            var fifo = _expectingAncestorByDescendant[descendantOid];
            var kept = new Queue<AnchorAttachmentRelation>(
                fifo.Where(relation => relation.ParentGuid != ancestorOid || retain(relation)));
            if (kept.Count is 0)
                _expectingAncestorByDescendant.Remove(descendantOid);
            else
                _expectingAncestorByDescendant[descendantOid] = kept;
        }
    }

    private void SiftByDescendant(
        uint descendantOid,
        Func<AnchorAttachmentRelation, bool> retain)
    {
        if (!_expectingAncestorByDescendant.TryGetValue(
                descendantOid,
                out Queue<AnchorAttachmentRelation>? fifo))

            return;

        var kept = new Queue<AnchorAttachmentRelation>(fifo.Where(retain));
        if (kept.Count is 0)
            _expectingAncestorByDescendant.Remove(descendantOid);
        else
            _expectingAncestorByDescendant[descendantOid] = kept;
    }

    private readonly record struct AncestorIncarnation(
        uint ServerGuid,
        ushort InstanceSequence);
}
