using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class ActorSummonBridge
{
    public bool AssignExhibitHoused(RealmActor actor, bool housed)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return !TrySeekHolder(actor, out _, out Holder holder)
            || holder.DeletionQueued
            || (housed ? holder.IsFullyHoused : holder.IsFullySuspended)
            ? false
            : housed
            ? ReactivateExhibit(holder)
            : SuspendExhibit(holder);
    }

    public AnimatedActorLedger? FetchPhase(uint srvOid)
    {
        foreach (Holder holder in _ownersByKey.Values)
        {
            if (holder.Entity.ServerGuid == srvOid)
                return holder.State;
        }

        return null;
    }

    private bool TryDrop(
        SimActorKey tag,
        RealmActor anticipatedActor)
    {
        if (!_ownersByKey.TryGetValue(tag, out Holder? holder)
            || !ReferenceEquals(holder.Entity, anticipatedActor))

            return false;

        holder.DeletionQueued = true;
        if (holder.Transition != DisplayTransition.None)
            throw new ActorDisplayRemovalDeferredException(
                holder.Entity.ServerGuid);

        if (holder.HasExhibitAssetList && !SuspendExhibit(holder))
            return false;

        if (_ownersByKey.TryGetValue(tag, out Holder? latest)
            && ReferenceEquals(latest, holder))
        {
            _ownersByKey.Remove(tag);
            return true;
        }

        return false;
    }

    private bool TrySeekHolder(
        RealmActor actor,
        out SimActorKey tag,
        out Holder holder)
    {
        foreach ((SimActorKey contenderTag, Holder contender) in _ownersByKey)
        {
            if (ReferenceEquals(contender.Entity, actor))
            {
                tag = contenderTag;
                holder = contender;
                return true;
            }
        }

        tag = default;
        holder = null!;
        return false;
    }

    private bool ReactivateExhibit(Holder holder)
    {
        if (holder.Transition != DisplayTransition.None)
            return false;

        holder.Transition = DisplayTransition.Resuming;
        List<ulong> acquiredThisChangeover = new List<ulong>();
        bool wantedTriMeshSetAcquired = false;
        try
        {
            if (_triMeshBridge is not null)
                ObtainAbsentTriMeshReferences(holder, holder.TriMeshIdents, acquiredThisChangeover);
            wantedTriMeshSetAcquired = true;

            holder.TextureFreeNeeded = true;
            holder.IsExhibitHoused = true;

            var freeMisses = FreeTriMeshReferencesBeyond(
                holder,
                holder.TriMeshIdents);
            return freeMisses is not null
                ? throw new AggregateException(
                    $"Live entity 0x{holder.Entity.ServerGuid:X8} presentation mesh reconciliation failed",
                    freeMisses)
                : true;
        }
        catch (Exception obtainMiss) when (!wantedTriMeshSetAcquired)
        {
            var undoMisses = RollBackAcquiredTriMeshReferences(
                holder,
                acquiredThisChangeover);
            if (undoMisses is null)
                throw;

            undoMisses.Insert(0, obtainMiss);
            throw new AggregateException(
                $"Live entity 0x{holder.Entity.ServerGuid:X8} presentation resume and rollback failed",
                undoMisses);
        }
        finally
        {
            holder.Transition = DisplayTransition.None;
        }
    }

    private bool SuspendExhibit(Holder holder)
    {
        if (holder.Transition != DisplayTransition.None)
            return false;

        holder.Transition = DisplayTransition.Suspending;

        List<Exception>? misses = null;
        if (holder.TextureFreeNeeded)
        {
            try
            {
                _textureLifespan.FreeHolder(holder.Entity.Id);
                holder.TextureFreeNeeded = false;
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }

        var triMeshMisses = FreeAllTriMeshReferences(holder);
        if (triMeshMisses is not null)
            (misses ??= []).AddRange(triMeshMisses);

        if (misses is not null)
        {
            holder.Transition = DisplayTransition.None;
            throw new AggregateException(
                $"Live entity 0x{holder.Entity.ServerGuid:X8} presentation suspension failed",
                misses);
        }

        holder.IsExhibitHoused = false;
        holder.Transition = DisplayTransition.None;
        return true;
    }

    private static HashSet<ulong> GatherTriMeshIdents(
        IReadOnlyList<TriMeshRef> triMeshRefs,
        IReadOnlyList<PartSwap> pieceSubstitutions)
    {
        HashSet<ulong> unique = new HashSet<ulong>();
        for (int idx = 0; idx < triMeshRefs.Count; ++idx)
            unique.Add(triMeshRefs[idx].GfxObjId);
        for (int idx = 0; idx < pieceSubstitutions.Count; ++idx)
            unique.Add(pieceSubstitutions[idx].GfxObjId);
        return unique;
    }

    private void ObtainAbsentTriMeshReferences(
        Holder holder,
        HashSet<ulong> wantedTriMeshIdents,
        List<ulong> acquiredThisChangeover)
    {
        if (_triMeshBridge is null)
            return;

        foreach (ulong triMeshIdent in wantedTriMeshIdents)
        {
            if (holder.TriMeshReferencesPinned.Contains(triMeshIdent))
                continue;

            try
            {
                _triMeshBridge.IncrementRefTally(triMeshIdent);
                holder.TriMeshReferencesPinned.Add(triMeshIdent);
                acquiredThisChangeover.Add(triMeshIdent);
            }
            catch (TriMeshRefAlterationFault problem)
            {
                if (problem.AlterationSealed)
                {
                    holder.TriMeshReferencesPinned.Add(triMeshIdent);
                    acquiredThisChangeover.Add(triMeshIdent);
                }

                throw;
            }
        }
    }

    private List<Exception>? RollBackAcquiredTriMeshReferences(
        Holder holder,
        List<ulong> acquiredThisChangeover)
    {
        if (_triMeshBridge is null)
            return null;

        List<Exception>? misses = null;
        for (int idx = acquiredThisChangeover.Count - 1; idx >= 0; --idx)
        {
            ulong triMeshIdent = acquiredThisChangeover[idx];
            if (!holder.TriMeshReferencesPinned.Contains(triMeshIdent))
                continue;

            try
            {
                _triMeshBridge.DecrementRefTally(triMeshIdent);
                holder.TriMeshReferencesPinned.Remove(triMeshIdent);
            }
            catch (Exception problem)
            {
                if (problem is TriMeshRefAlterationFault { AlterationSealed: true })
                    holder.TriMeshReferencesPinned.Remove(triMeshIdent);
                (misses ??= []).Add(problem);
            }
        }

        return misses;
    }

    private List<Exception>? FreeTriMeshReferencesBeyond(
        Holder holder,
        HashSet<ulong> wantedTriMeshIdents)
    {
        if (_triMeshBridge is null || holder.TriMeshReferencesPinned.Count is 0)
            return null;

        List<Exception>? misses = null;
        ulong[] pinnedCapture = [.. holder.TriMeshReferencesPinned];
        foreach (ulong triMeshIdent in pinnedCapture)
        {
            if (wantedTriMeshIdents.Contains(triMeshIdent))
                continue;

            try
            {
                _triMeshBridge.DecrementRefTally(triMeshIdent);
                holder.TriMeshReferencesPinned.Remove(triMeshIdent);
            }
            catch (Exception problem)
            {
                if (problem is TriMeshRefAlterationFault { AlterationSealed: true })
                    holder.TriMeshReferencesPinned.Remove(triMeshIdent);
                (misses ??= []).Add(problem);
            }
        }

        return misses;
    }

    private List<Exception>? FreeAllTriMeshReferences(Holder holder)
    {
        return _triMeshBridge is null || holder.TriMeshReferencesPinned.Count is 0 ? null : FreeTriMeshReferencesBeyond(holder, []);
    }
}
