namespace MacAC.Mechanics.Gear;

public enum PackPlacementRefusal
{
    None,
    InvalidItem,
    CannotMovePlayer,
    CannotMoveCreature,
    SourceBeingTraded,
    InvalidDestination,
    DestinationBeingTraded,
    RecursiveContainment,
    ItemCapacityFull,
    ContainerCapacityFull,
}

public static class PackPlacementPolicy
{
    public static PackPlacementRefusal Evaluate(
        ClientThingChart objects,
        uint gearIdent,
        uint destIdent,
        uint avatarIdent)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (gearIdent is 0u || objects.Get(gearIdent) is not { } gear)
            return PackPlacementRefusal.InvalidItem;
        if (gearIdent == avatarIdent)
            return PackPlacementRefusal.CannotMovePlayer;
        if ((gear.Type & GearKind.Creature) != 0)
            return PackPlacementRefusal.CannotMoveCreature;
        if (gear.BarterPhase is 1)
            return PackPlacementRefusal.SourceBeingTraded;

        var dest = objects.Get(destIdent);
        if (!IsValidDest(dest, destIdent, avatarIdent))
            return PackPlacementRefusal.InvalidDestination;
        if (dest?.BarterPhase == 1)
            return PackPlacementRefusal.DestinationBeingTraded;
        if (gearIdent == destIdent || Encloses(objects, outerIdent: gearIdent, interiorIdent: destIdent))
            return PackPlacementRefusal.RecursiveContainment;

        // An item already sitting directly in the destination never counts against its capacity.
        if (gear.VesselTag == destIdent)
            return PackPlacementRefusal.None;

        return IsVessel(gear)
            ? VerifyHall(dest?.ContainersCapacity ?? 0, Count(objects, destIdent, bundles: true), PackPlacementRefusal.ContainerCapacityFull)
            : VerifyHall(dest?.ItemsCapacity ?? 0, Count(objects, destIdent, bundles: false), PackPlacementRefusal.ItemCapacityFull);
    }

    public static string? ConstructClientOwn(
        PackPlacementRefusal rejection,
        ClientThing? gear,
        ClientThing? dest,
        uint avatarIdent)
    {
        string gearLabel = gear?.FetchAppropriateLabel() ?? "item";
        string bundleLabel = dest?.FetchAppropriateLabel() ?? "container";
        bool intoOwnBundle = dest?.ObjectId == avatarIdent;
        return rejection switch
        {
            PackPlacementRefusal.None => null,
            PackPlacementRefusal.InvalidItem => "That item is not valid!",
            PackPlacementRefusal.CannotMovePlayer => "You cannot place yourself within another object!",
            PackPlacementRefusal.CannotMoveCreature => "You cannot pick up creatures!",
            PackPlacementRefusal.SourceBeingTraded => $"The {gearLabel} is being traded",
            PackPlacementRefusal.InvalidDestination => "The destination container is not valid!",
            PackPlacementRefusal.DestinationBeingTraded => $"The {bundleLabel} is being traded",
            PackPlacementRefusal.RecursiveContainment => "You cannot place an object within itself!",
            PackPlacementRefusal.ItemCapacityFull when intoOwnBundle => "Backpack is completely full!",
            PackPlacementRefusal.ItemCapacityFull => $"The {bundleLabel} is completely full!",
            PackPlacementRefusal.ContainerCapacityFull when intoOwnBundle => $"{bundleLabel} can carry no more containers!",
            PackPlacementRefusal.ContainerCapacityFull => $"The {bundleLabel} can fit no more containers!",
            _ => null,
        };
    }

    public static bool IsVessel(ClientThing gear)
    {
        return gear.VesselKindHint is not 0u
        || (gear.Type & GearKind.Container) != 0
        || gear.ItemsCapacity is not 0
        || gear.ContainersCapacity is not 0;
    }

    private static bool IsValidDest(ClientThing? dest, uint destIdent, uint avatarIdent)
    {
        if (destIdent is 0u)
            return false;
        return destIdent == avatarIdent ? true : dest is not null && IsVessel(dest);
    }

    private static PackPlacementRefusal VerifyHall(int cap, int occupied, PackPlacementRefusal whenWhole) =>
        cap > 0 && occupied >= cap ? whenWhole : PackPlacementRefusal.None;

    // Counts the direct children of a container that are (or are not) packs
    private static int Count(ClientThingChart objects, uint vesselIdent, bool bundles)
    {
        int tally = 0;
        foreach (uint descendantIdent in objects.FetchInsides(vesselIdent))
        {
            if (objects.Get(descendantIdent) is { } descendant && IsVessel(descendant) == bundles)
                ++tally;
        }
        return tally;
    }

    // True when walking up from interiorIdent reaches outerIdent
    private static bool Encloses(ClientThingChart objects, uint outerIdent, uint interiorIdent)
    {
        HashSet<uint> walked = new HashSet<uint>();
        for (uint cur = interiorIdent; cur is not 0u && walked.Add(cur); cur = objects.Get(cur)?.VesselTag ?? 0u)
        {
            if (cur == outerIdent)
                return true;
        }
        return false;
    }
}
