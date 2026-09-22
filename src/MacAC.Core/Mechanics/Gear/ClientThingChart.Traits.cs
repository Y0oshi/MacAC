namespace MacAC.Mechanics.Gear;

public sealed partial class ClientThingChart
{

    public bool RefreshProps(uint gearIdent, TraitBundle incoming)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;

        TopLayer(gear, incoming);
        GrabCooldowns(gear, incoming);
        ObjectUpdated?.Invoke(gear);
        return true;
    }

    public bool RefreshAppraisal(
        uint gearIdent,
        TraitBundle incoming,
        IReadOnlyList<uint> arcanumIdents,
        double receivedAtSecs = 0d)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(arcanumIdents);
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;

        TopLayer(gear, incoming);
        gear.AppraisedArcanumIds = arcanumIdents.Count is 0 ? [] : arcanumIdents.ToArray();
        if (double.IsFinite(receivedAtSecs) && receivedAtSecs >= 0d)
        {
            long msec = checked((long)Math.Round(receivedAtSecs * 1000d, MidpointRounding.AwayFromZero));
            gear.PreviousAppraisalMomentMsec = unchecked((int)msec);
        }
        GrabCooldowns(gear, incoming);
        ObjectUpdated?.Invoke(gear);
        return true;
    }

    public void UpsertProps(uint oid, TraitBundle incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var gear = Admit(oid, out bool existed);
        TopLayer(gear, incoming);
        GrabCooldowns(gear, incoming);
        Proclaim(gear, existed);
    }

    public bool RefreshIntProp(uint gearIdent, uint propIdent, int val)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;

        var prior = ObjectPlacement.From(gear);
        gear.Properties.Ints[propIdent] = val;
        switch (propIdent)
        {
            case WidgetFxListPropIdent:
                gear.Effects = (uint)val;
                break;
            case SharedCooldownPropIdent:
                gear.CooldownId = (uint)val;
                break;
            case LatestWieldedLocalePropIdent:
                gear.CurrentlyEquippedLocale = (WieldBitmask)(uint)val;
                _worn.Shift(gearIdent, prior, ObjectPlacement.From(gear));
                break;
            case TapKindPropIdent:
                gear.HookType = (uint)val;
                break;
            case TapGearKindsPropIdent:
                gear.HookItemTypes = (uint)val;
                break;
            case AvatarKillerConditionPropIdent:
                gear.PublicWeenieBitfield = PkStatusBits.Apply(gear.PublicWeenieBitfield ?? 0u, val);
                break;
        }
        ObjectUpdated?.Invoke(gear);
        return true;
    }

    public bool RefreshInt64Prop(uint gearIdent, uint propIdent, long val)
    {
        if (!_objects.TryGetValue(gearIdent, out ClientThing? gear))
            return false;
        gear.Properties.Int64s[propIdent] = val;
        ObjectUpdated?.Invoke(gear);
        return true;
    }

    public bool RefreshPileDims(uint oid, int pileDims, int val)
    {
        if (!_objects.TryGetValue(oid, out ClientThing? gear))
            return false;
        gear.StackSize = pileDims;
        gear.Value = val;
        ObjectUpdated?.Invoke(gear);
        StackSizeUpdated?.Invoke(gear);
        return true;
    }

    public bool RefreshHouseRestrictions(uint oid, HouseAccessRecord restrictions)
    {
        ArgumentNullException.ThrowIfNull(restrictions);
        if (!_objects.TryGetValue(oid, out ClientThing? gear))
            return false;
        gear.Restrictions = restrictions;
        ObjectUpdated?.Invoke(gear);
        return true;
    }

    public ClientThing Ingest(WeenieRecord d)
    {
        var objRef = Admit(d.Guid, out bool existed);
        uint formerVessel = objRef.VesselTag;
        var prior = ObjectPlacement.From(objRef);

        Absorb(objRef, in d);

        List<uint>? touched = _shelves.Evict(objRef.ObjectId, objRef.VesselTag);
        _shelves.Refile(objRef, formerVessel);
        _worn.Shift(objRef.ObjectId, prior, ObjectPlacement.From(objRef));
        Proclaim(objRef, existed);
        BroadcastShelfEdits(touched);

        if (!existed && _postponedStances.Remove(d.Guid, out DeferredPlacement postponed))
        {
            var placement = postponed.Placement;
            ImposeSrvRelocate(d.Guid, placement.ContainerId, placement.WielderId, placement.ContainerSlot, placement.EquipLocation, postponed.ContainerTypeHint);
        }
        return objRef;
    }

    public ClientThing CaptureMembership(uint oid, uint vesselIdent = 0,
        WieldBitmask wield = WieldBitmask.None, uint? vesselKindHint = null,
        uint? precedence = null)
    {
        var objRef = Admit(oid, out bool existed);
        uint formerVessel = objRef.VesselTag;
        var prior = ObjectPlacement.From(objRef);

        if (vesselIdent is not 0u)
            objRef.VesselTag = vesselIdent;
        objRef.CurrentlyEquippedLocale = wield;
        if (wield != WieldBitmask.None)
            objRef.VesselSlot = -1;
        if (vesselKindHint is { } hint) objRef.VesselKindHint = hint;
        if (precedence is { } p) objRef.Priority = p;

        List<uint>? touched = _shelves.Evict(objRef.ObjectId, objRef.VesselTag);
        _shelves.Refile(objRef, formerVessel);
        _worn.Shift(objRef.ObjectId, prior, ObjectPlacement.From(objRef));
        Proclaim(objRef, existed);
        BroadcastShelfEdits(touched);
        return objRef;
    }

    // Copies every typed table of incoming over the item's own
    private static void TopLayer(ClientThing gear, TraitBundle incoming)
    {
        TraitBundle mine = gear.Properties;
        foreach ((uint tag, int v) in incoming.Ints) mine.Ints[tag] = v;
        foreach ((uint tag, long v) in incoming.Int64s) mine.Int64s[tag] = v;
        foreach ((uint tag, bool v) in incoming.Bools) mine.Bools[tag] = v;
        TopLayerRest(mine, incoming);
    }

    private static void TopLayerRest(TraitBundle mine, TraitBundle incoming)
    {
        foreach ((uint tag, double v) in incoming.Floats) mine.Floats[tag] = v;
        foreach ((uint tag, string v) in incoming.Texts) mine.Texts[tag] = v;
        foreach ((uint tag, uint v) in incoming.BlobIdents) mine.BlobIdents[tag] = v;
        foreach ((uint tag, uint v) in incoming.InstIdents) mine.InstIdents[tag] = v;
    }

    private static void GrabCooldowns(ClientThing gear, TraitBundle incoming)
    {
        if (incoming.Ints.TryGetValue(SharedCooldownPropIdent, out int cooldownIdent))
            gear.CooldownId = (uint)cooldownIdent;
        if (incoming.Floats.TryGetValue(CooldownIntervalPropIdent, out double cooldownInterval))
            gear.CooldownDuration = cooldownInterval;
    }

    // Applies a public weenie description
    private static void Absorb(ClientThing objRef, in WeenieRecord d)
    {
        if (!string.IsNullOrEmpty(d.Name)) objRef.Name = d.Name;
        if (!string.IsNullOrEmpty(d.PluralName)) objRef.PluralName = d.PluralName;
        if (d.Type is { } kind) objRef.Type = kind;
        if (d.WeenieClassId is not 0u) objRef.WeenieClassIdent = d.WeenieClassId;
        if (d.IconId is not 0u) objRef.IconId = d.IconId;
        if (d.IconOverlayId is not 0u) objRef.GlyphTopLayerIdent = d.IconOverlayId;
        if (d.IconUnderlayId is not 0u) objRef.GlyphUnderlayIdent = d.IconUnderlayId;
        objRef.Effects = d.Effects;

        objRef.Value = d.Value ?? objRef.Value;
        objRef.StackSize = d.StackSize ?? objRef.StackSize;
        objRef.PileDimsUpper = d.StackSizeMax ?? objRef.PileDimsUpper;
        objRef.Burden = d.Burden ?? objRef.Burden;
        objRef.VesselTag = d.ContainerId ?? objRef.VesselTag;
        objRef.WielderIdent = d.WielderId ?? objRef.WielderIdent;
        if (d.ValidLocations is { } valid) objRef.ValidLocations = (WieldBitmask)valid;
        if (d.CurrentWieldedLocation is { } worn) objRef.CurrentlyEquippedLocale = (WieldBitmask)worn;
        objRef.Priority = d.Priority ?? objRef.Priority;
        objRef.Useability = d.Useability ?? objRef.Useability;
        objRef.TargetType = d.TargetType ?? objRef.TargetType;
        objRef.PublicWeenieBitfield = d.PublicWeenieBitfield ?? objRef.PublicWeenieBitfield;
        objRef.PetHolderIdent = d.PetOwnerId ?? objRef.PetHolderIdent;
        objRef.CombatUse = d.CombatUse ?? objRef.CombatUse;
        objRef.AmmoType = d.AmmoType ?? objRef.AmmoType;
        objRef.SpellId = d.SpellId ?? objRef.SpellId;
        objRef.CooldownId = d.CooldownId ?? objRef.CooldownId;
        objRef.CooldownDuration = d.CooldownDuration ?? objRef.CooldownDuration;
        objRef.RadarBlipColor = d.RadarBlipColor ?? objRef.RadarBlipColor;
        objRef.RadarBehavior = d.RadarBehavior ?? objRef.RadarBehavior;
        objRef.ItemsCapacity = d.ItemsCapacity ?? objRef.ItemsCapacity;
        objRef.ContainersCapacity = d.ContainersCapacity ?? objRef.ContainersCapacity;
        objRef.HookItemTypes = d.HookItemTypes ?? objRef.HookItemTypes;
        objRef.HookType = d.HookType ?? objRef.HookType;
        objRef.Structure = d.Structure ?? objRef.Structure;
        objRef.MaxStructure = d.MaxStructure ?? objRef.MaxStructure;
        objRef.Workmanship = d.Workmanship ?? objRef.Workmanship;
        objRef.MaterialType = d.MaterialType ?? objRef.MaterialType;

        // These three notify restriction observers only on a real change,
        // so writing the current value back is silent.
        if (d.HouseOwnerId is { } houseHolder) objRef.HouseHolderIdent = houseHolder;
        if (d.MonarchId is { } monarch) objRef.MonarchIdent = monarch;
        if (d.Restrictions is { } restrictions) objRef.Restrictions = restrictions;
    }
}
