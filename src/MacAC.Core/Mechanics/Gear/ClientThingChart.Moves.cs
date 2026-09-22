namespace MacAC.Mechanics.Gear;

public sealed partial class ClientThingChart
{
    // How a relocation files the item into its destination container
    private enum ShelfFiling
    {
        // Re-sort the destination by declared slot
        BySlotOrder,

        // Retail insert: evict from every list, then splice into the pack/item run
        CanonRunInsert,
    }

    public bool ShiftGear(uint gearIdent, uint newVesselIdent, int newSocket = -1,
        WieldBitmask newWieldLocale = WieldBitmask.None, uint? vesselKindHint = null)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;

        ObjectPlacement mark = new ObjectPlacement(newVesselIdent, newSocket, gear.WielderIdent, newWieldLocale);
        return Relocate(gear, mark, vesselKindHint, ObjectRelocationOrigin.LocalProjection, ShelfFiling.BySlotOrder);
    }

    public bool ImposeSrvRelocate(
        uint gearIdent,
        uint newVesselIdent,
        uint newWielderIdent,
        int newSocket = -1,
        WieldBitmask newWieldLocale = WieldBitmask.None,
        uint? vesselKindHint = null)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;

        ObjectPlacement mark = new ObjectPlacement(newVesselIdent, newSocket, newWielderIdent, newWieldLocale);
        return Relocate(gear, mark, vesselKindHint, ObjectRelocationOrigin.AuthoritativeResponse, ShelfFiling.CanonRunInsert);
    }

    public bool ImposeConfirmedSrvRelocate(
        uint gearIdent,
        uint newVesselIdent,
        uint newWielderIdent,
        int newSocket = -1,
        WieldBitmask newWieldLocale = WieldBitmask.None,
        uint? vesselKindHint = null)
    {
        ConfirmRelocate(gearIdent);
        if (ImposeSrvRelocate(gearIdent, newVesselIdent, newWielderIdent, newSocket, newWieldLocale, vesselKindHint))
            return true;

        ObjectPlacement mark = new ObjectPlacement(newVesselIdent, newSocket, newWielderIdent, newWieldLocale);
        if (gearIdent is not 0u && newVesselIdent is not 0u && !_objects.ContainsKey(gearIdent))
            _postponedStances[gearIdent] = new DeferredPlacement(mark, vesselKindHint);

        ObjectMoved?.Invoke(new ObjectRelocation(
            gearIdent, Item: null, ObjectPlacement.Nowhere, mark, ObjectRelocationOrigin.AuthoritativeResponse));
        return false;
    }

    public bool ImposeConfirmedSrvWield(uint gearIdent, uint wielderIdent, WieldBitmask wieldLocale)
    {
        ConfirmRelocate(gearIdent);
        ObjectPlacement mark = new ObjectPlacement(ContainerId: 0u, ContainerSlot: 0, wielderIdent, wieldLocale);
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
        {
            ObjectMoved?.Invoke(new ObjectRelocation(
                gearIdent, Item: null, ObjectPlacement.Nowhere, mark, ObjectRelocationOrigin.AuthoritativeResponse));
            WieldConfirmed?.Invoke(gearIdent);
            return false;
        }

        if (!Relocate(gear, mark, vesselKindHint: null, ObjectRelocationOrigin.AuthoritativeResponse, ShelfFiling.BySlotOrder))
            return false;

        WieldConfirmed?.Invoke(gearIdent);
        return true;
    }

    public bool ShiftGearOptimistic(uint gearIdent, uint newVesselIdent, int newSocket)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;
        _queued.Open(gearIdent, gear);

        var prior = ObjectPlacement.From(gear);
        uint formerVessel = prior.ContainerId;
        gear.VesselTag = newVesselIdent;
        gear.CurrentlyEquippedLocale = WieldBitmask.None;
        List<uint>? touched = _shelves.Evict(gearIdent, newVesselIdent);

        // keep the source gapless too
        if (formerVessel is not 0u && formerVessel != newVesselIdent && _shelves.TryGet(formerVessel, out List<uint>? src))
        {
            src.Remove(gearIdent);
            _shelves.RenumberPlanar(src);
        }

        if (newVesselIdent is not 0u)
        {
            List<uint> dest = _shelves.FetchOrAppend(newVesselIdent);
            dest.Remove(gearIdent);
            int at = newSocket < 0 || newSocket > dest.Count ? dest.Count : newSocket;
            dest.Insert(at, gearIdent);
            _shelves.RenumberPlanar(dest);
        }
        else
        {
            gear.VesselSlot = newSocket;
        }

        BroadcastRelocation(gear, prior, ObjectRelocationOrigin.LocalProjection);
        BroadcastShelfEdits(touched);
        return true;
    }

    public bool WieldGearOptimistic(uint gearIdent, uint wielderOid, WieldBitmask wieldBitmask)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;
        _queued.Open(gearIdent, gear);
        return ShiftGear(gearIdent, wielderOid, newSocket: -1, newWieldLocale: wieldBitmask);
    }

    public void ConfirmRelocate(uint gearIdent) => _queued.Settle(gearIdent);

    public bool RevertRelocate(uint gearIdent)
    {
        if (!_queued.TryShut(gearIdent, out ObjectPlacement prior))
            return false;
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;
        if (!Relocate(gear, prior, vesselKindHint: null, ObjectRelocationOrigin.ServerRollbackProjection, ShelfFiling.BySlotOrder))
            return false;

        MoveRolledBack?.Invoke(_objects[gearIdent]);
        return true;
    }

    public bool RejectRelocate(uint gearIdent, uint weenieProblem)
    {
        bool rolledBack = RevertRelocate(gearIdent);
        MoveRequestFailed?.Invoke(new RelocationRefusal(gearIdent, weenieProblem, rolledBack));
        return rolledBack;
    }

    /// <summary>Handle a server-driven remove (destroyed item, dropped into 3D space, stolen, etc).</summary>
    public bool Remove(uint gearIdent) => Evict(gearIdent, ObjectRemovalCause.Ordinary, gen: 0);

    public bool DropLogicalGen(uint gearIdent, ushort gen) =>
        Evict(gearIdent, ObjectRemovalCause.LogicalDelete, gen);

    public ClientThing? ReplaceGen(
        WeenieRecord blob,
        ushort gen,
        Func<bool>? canInstallSubstitute = null)
    {
        Evict(blob.Guid, ObjectRemovalCause.GenerationReplacement, gen);
        if (canInstallSubstitute?.Invoke() == false)
            return null;
        return Ingest(blob);
    }

    private bool Relocate(
        ClientThing gear,
        ObjectPlacement mark,
        uint? vesselKindHint,
        ObjectRelocationOrigin origin,
        ShelfFiling filing)
    {
        var prior = ObjectPlacement.From(gear);
        bool wasBundle = FilesAsBundle(gear);
        List<uint>? touched = filing == ShelfFiling.CanonRunInsert
            ? _shelves.EvictEverywhere(gear.ObjectId, mark.ContainerId, wasBundle)
            : _shelves.Evict(gear.ObjectId, mark.ContainerId);

        gear.VesselTag = mark.ContainerId;
        gear.VesselSlot = mark.ContainerSlot;
        gear.WielderIdent = mark.WielderId;
        gear.CurrentlyEquippedLocale = mark.EquipLocation;
        if (vesselKindHint is { } hint)
            gear.VesselKindHint = hint;

        if (filing == ShelfFiling.CanonRunInsert && gear.VesselTag is not 0u)
            _shelves.SlotIntoExec(gear, mark.ContainerSlot);
        else
            _shelves.Refile(gear, prior.ContainerId);

        BroadcastRelocation(gear, prior, origin);
        BroadcastShelfEdits(touched);
        return true;
    }

    private void BroadcastRelocation(ClientThing gear, ObjectPlacement prior, ObjectRelocationOrigin origin)
    {
        var following = ObjectPlacement.From(gear);
        _worn.Shift(gear.ObjectId, prior, following);
        ObjectMoved?.Invoke(new ObjectRelocation(gear.ObjectId, gear, prior, following, origin));
    }

    private bool Evict(uint gearIdent, ObjectRemovalCause cause, ushort gen)
    {
        if (!_objects.TryRemove(gearIdent, out ClientThing? gear))
            return false;

        HaltWatchingRestrictions(gear);
        List<uint>? touched = _shelves.Evict(gearIdent, exceptVesselIdent: 0u);
        _worn.Shift(gearIdent, ObjectPlacement.From(gear), ObjectPlacement.Nowhere);
        _queued.Forget(gearIdent);   // a destroyed item must not leave a snapshot that mis-rolls-back a recycled guid
        ObjectRemoved?.Invoke(gear);
        ObjectRemovalClassified?.Invoke(new ObjectRemoval(gear, cause, gen));
        BroadcastShelfEdits(touched);
        return true;
    }
}
