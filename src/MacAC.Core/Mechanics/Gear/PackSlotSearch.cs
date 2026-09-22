namespace MacAC.Mechanics.Gear;

public static class PackSlotSearch
{

    public static bool WillGearFitInVessel(
        ClientThingChart objects,
        uint gearIdent,
        uint vesselIdent,
        uint avatarIdent)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (vesselIdent is 0u || gearIdent == vesselIdent)
            return false;

        var vessel = objects.Get(vesselIdent);
        if (vessel is null)
        {
            return vesselIdent == avatarIdent;
        }

        bool theirs = vesselIdent == avatarIdent || (avatarIdent is not 0u && objects.IsPossessedByObject(vesselIdent, avatarIdent));
        if (!theirs || vessel.BarterPhase is 1)
            return false;

        bool bundleGoingIn = IsBundle(objects.Get(gearIdent));

        // Item slots: -1 means no limit and 0 means the server sent no capacity (only the player and packs
        // carry one), so both are unbounded here.
        int cap = bundleGoingIn ? vessel.ContainersCapacity : vessel.ItemsCapacity;
        bool unbounded = bundleGoingIn ? cap < 0 : cap <= 0;
        if (unbounded)
            return true;

        int alike = 0;
        foreach (uint pinnedIdent in objects.FetchInsides(vesselIdent))
        {
            if (pinnedIdent == gearIdent)
                return true;
            if (IsBundle(objects.Get(pinnedIdent)) == bundleGoingIn)
                ++alike;
        }
        return alike < cap;
    }

    public static uint SelectVessel(
        ClientThingChart objects,
        uint gearIdent,
        uint trunkIdent,
        uint markIdent,
        uint avatarIdent,
        out PackPlacementRefusal refusal)
    {
        ArgumentNullException.ThrowIfNull(objects);
        refusal = PackPlacementRefusal.None;

        if (markIdent is not 0u && WillGearFitInVessel(objects, gearIdent, markIdent, avatarIdent))
            return markIdent;
        if (trunkIdent is not 0u && WillGearFitInVessel(objects, gearIdent, trunkIdent, avatarIdent))
            return trunkIdent;

        foreach (ClientThing flankBundle in FlankBundlesInOrdering(objects, trunkIdent))
        {
            if (WillGearFitInVessel(objects, gearIdent, flankBundle.ObjectId, avatarIdent))
                return flankBundle.ObjectId;
        }

        refusal = IsBundle(objects.Get(gearIdent))
            ? PackPlacementRefusal.ContainerCapacityFull
            : PackPlacementRefusal.ItemCapacityFull;
        return 0u;
    }
    private static bool IsBundle(ClientThing? gear) => gear is not null && PackPlacementPolicy.IsVessel(gear);

    private static List<ClientThing> FlankBundlesInOrdering(ClientThingChart objects, uint trunkIdent)
    {
        List<ClientThing> bundles = [];
        if (trunkIdent is 0u)
            return bundles;

        foreach (uint pinnedIdent in objects.FetchInsides(trunkIdent))
        {
            if (objects.Get(pinnedIdent) is { } pinned && IsBundle(pinned))
                bundles.Add(pinned);
        }
        bundles.Sort(static (a, b) => a.VesselSlot.CompareTo(b.VesselSlot));
        return bundles;
    }
}
