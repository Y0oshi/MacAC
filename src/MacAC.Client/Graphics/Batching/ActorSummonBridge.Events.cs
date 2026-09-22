using MacAC.Mechanics.Realm;
using MacAC.Sim.Actors;

namespace MacAC.Client.Graphics.Batching;

public sealed partial class ActorSummonBridge
{
    public AnimatedActorLedger? OnCreate(RealmActor actor)
        => OnCreate(new SimActorKey(actor.Id, 0), actor);

    public AnimatedActorLedger? OnCreate(
        SimActorKey tag,
        RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.ServerGuid is 0) return null;
        if (tag.LocalEntityId is 0 || tag.LocalEntityId != actor.Id)
        {
            throw new InvalidOperationException(
                "The exact Runtime projection key must match the RealmActor local ID");
        }

        actor.RenewAabb();

        var scheduler = _schedulerMaker(actor);
        AnimatedActorLedger phase = new AnimatedActorLedger(scheduler);

        phase.ConcealPieces(actor.ConcealedPiecesBitmask);
        foreach (var swap in actor.PieceSubstitutions)
            phase.AssignPieceOverride(swap.PartIndex, swap.GfxObjId);

        HashSet<ulong> triMeshIdents = _triMeshBridge is null
            ? []
            : GatherTriMeshIdents(actor.MeshRefs, actor.PieceSubstitutions);

        Holder substituteHolder = new Holder(tag, actor, phase, triMeshIdents);
        if (_ownersByKey.TryGetValue(tag, out Holder? displacedHolder))
        {
            if (displacedHolder.DeletionQueued)
            {
                throw new ActorDisplayRemovalDeferredException(actor.ServerGuid);
            }

            if (displacedHolder.Transition != DisplayTransition.None)
            {
                throw new InvalidOperationException(
                    $"Live entity 0x{actor.ServerGuid:X8} replacement was requested while its presentation transition was by now in progress");
            }

            if (displacedHolder.HasExhibitAssetList && !SuspendExhibit(displacedHolder))
            {
                throw new InvalidOperationException(
                    $"Live entity 0x{actor.ServerGuid:X8} replacement was requested while its presentation teardown was by now in progress");
            }

            _ownersByKey[tag] = substituteHolder;
        }
        else
        {
            _ownersByKey.Add(tag, substituteHolder);
        }

        return phase;
    }

    public bool OnLooksAltered(
        RealmActor actor,
        IReadOnlyList<TriMeshRef> triMeshRefs,
        IReadOnlyList<PartSwap> pieceSubstitutions,
        Action broadcastLooks,
        Action? followingBulletin = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(triMeshRefs);
        ArgumentNullException.ThrowIfNull(pieceSubstitutions);
        ArgumentNullException.ThrowIfNull(broadcastLooks);

        if (!TrySeekHolder(actor, out _, out Holder holder)
            || holder.DeletionQueued
            || holder.Transition != DisplayTransition.None)

            return false;

        HashSet<ulong> upcomingTriMeshIdents = _triMeshBridge is null
            ? []
            : GatherTriMeshIdents(triMeshRefs, pieceSubstitutions);
        holder.Transition = DisplayTransition.ChangingAppearance;
        List<ulong> acquiredThisChangeover = new List<ulong>();
        bool triMeshSetPublished = false;

        try
        {
            if (holder.IsExhibitHoused && _triMeshBridge is not null)
                ObtainAbsentTriMeshReferences(holder, upcomingTriMeshIdents, acquiredThisChangeover);

            broadcastLooks();

            holder.TriMeshIdents = upcomingTriMeshIdents;
            triMeshSetPublished = true;
            followingBulletin?.Invoke();

            List<Exception>? freeMisses = holder.IsExhibitHoused
                ? FreeTriMeshReferencesBeyond(holder, upcomingTriMeshIdents)
                : FreeAllTriMeshReferences(holder);
            return freeMisses is not null
                ? throw new AggregateException(
                    $"Live entity 0x{holder.Entity.ServerGuid:X8} appearance mesh retirement failed",
                    freeMisses)
                : true;
        }
        catch (Exception obtainOrBulletinMiss) when (!triMeshSetPublished)
        {
            var undoMisses = RollBackAcquiredTriMeshReferences(
                holder,
                acquiredThisChangeover);
            if (undoMisses is null)
                throw;

            undoMisses.Insert(0, obtainOrBulletinMiss);
            throw new AggregateException(
                $"Live entity 0x{holder.Entity.ServerGuid:X8} appearance publication and mesh rollback failed",
                undoMisses);
        }
        finally
        {
            holder.Transition = DisplayTransition.None;
        }
    }

    public void OnRemove(uint srvOid)
    {
        foreach ((SimActorKey tag, Holder holder) in _ownersByKey)
        {
            if (holder.Entity.ServerGuid == srvOid)
            {
                _ = TryDrop(tag, holder.Entity);
                return;
            }
        }
    }

    public bool OnRemove(RealmActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return TrySeekHolder(actor, out SimActorKey tag, out _)
            && TryDrop(tag, actor);
    }
}
