using MacAC.Client.Realm;
using MacAC.Mechanics.Gear;
using MacAC.Sim.Actors;
using MacAC.Wire;
using MacAC.Wire.Messages;

namespace MacAC.Client.Graphics;

public sealed partial class EquippedChildRenderDriver
{
    public void OnSummon(RealmSession.MoverSpawn summon)
    {
        lock (_datMutex)
        {
            if (summon.ParentGuid is { } ancestorOid and not 0
                && summon.ParentLocation is { } ancestorLocale)
            {
                uint stanceIdent = summon.PlacementId ?? 0u;
                AdmitLateTiedBuildObjectRelation(
                    ancestorOid,
                    summon.Guid,
                    ancestorLocale,
                    stanceIdent,
                    summon.PositionSequence);
            }

            LocateAndTryRealize(summon.Guid);

            ReattemptWaitingDescendants(summon.Guid);
        }
    }

    public void OnRealmActorRegistered(uint oid)
    {
        lock (_datMutex)
        {
            ReattemptWaitingDescendants(oid);
        }
    }

    public void OnPosturePublished(uint oid)
    {
        lock (_datMutex)
            ReattemptWaitingDescendants(oid);
    }

    public void OnAncestorSignal(AncestorSignal.Parsed refresh)
    {
        lock (_datMutex)
        {
            Relations.Enqueue(refresh);
            if (LocateAndTryRealize(refresh.ChildGuid))
                ReattemptWaitingDescendants(refresh.ChildGuid);
        }
    }

    public void OnLogicalTeardown(OnlineActorRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        lock (_datMutex)
            TearDownCaptureProjections(capture);
    }

    public DescendantUnparentDisposition OnDescendantBecameUnparented(
        uint descendantOid,
        Action? continuation = null)
    {
        if (!_onlineActors.TryFetchRecord(descendantOid, out OnlineActorRecord capture))
        {
            Relations.FinishDescendantProj(descendantOid);
            return DescendantUnparentDisposition.NotAttached;
        }

        SimActorKey tag = DemandProjTag(capture);
        var queued = new QueuedUnparentChangeover(
            capture,
            capture.PositionAuthorityVersion,
            GrabProjSubtreeAncestorLead(
                capture,
                revertTrunkRelation: false,
                revertDescendantRelations: true),
            continuation);
        return ProgressUnparentChangeover(tag, queued);
    }

    public void OnBuildAncestorApproved(CreateAnchorUpdate refresh)
    {
        lock (_datMutex)
        {
            AdmitLateTiedBuildObjectRelation(
                refresh.ParentGuid,
                refresh.ChildGuid,
                refresh.ParentLocation,
                refresh.PlacementId,
                refresh.ChildPositionSequence);
            LocateAndTryRealize(refresh.ChildGuid);
        }
    }

    private void OnObjectDeletionClassified(ObjectRemoval deletion)
    {
        if (deletion.Reason is not ObjectRemovalCause.Ordinary)
            return;

        uint oid = deletion.Object.ObjectId;
        TearDownCurrentObjectProjections(oid);
        // InventoryDrop removes the item from the UI view, not the live physics generation. Preserve
        // fresher pending ParentEvents so a same-generation world/parent update can still replay them.
    }

    private void OnObjectMoved(ObjectRelocation relocate)
    {
        if (relocate.Previous.EquipLocation != WieldBitmask.None
            && relocate.Current.EquipLocation == WieldBitmask.None)
            BeginDetachedRemoval(relocate.ItemId);
    }

    private void OnRelocateRolledBack(ClientThing gear)
    {
        if (gear.CurrentlyEquippedLocale == WieldBitmask.None)
            return;
        if (!_onlineActors.TryFetchRecord(
                gear.ObjectId,
                out OnlineActorRecord capture)
            || capture.ProjTag is not { } tag)

            return;
        if (_attachedByChild.TryGetValue(tag, out AffixedDescendant? affixed)
            && _onlineActors.IsLatestCapture(affixed.ChildRecord)
            && affixed.ChildRecord.IsSpatiallyProjected)
        {
            _pendingDetachedRemovalByChild.Remove(tag);
            return;
        }
        if (_pendingDetachedRemovalByChild.TryGetValue(
                tag,
                out PendingMirrorSubtree queued))
        {
            ProgressDetachedDeletion(tag, queued);
            if (_pendingDetachedRemovalByChild.ContainsKey(tag))
                return;
        }

        if (Relations.ReinstatePreviousApproved(gear.ObjectId))
        {
            lock (_datMutex)
            {
                if (LocateAndTryRealize(gear.ObjectId))
                    ReattemptWaitingDescendants(gear.ObjectId);
            }
        }
    }
}
