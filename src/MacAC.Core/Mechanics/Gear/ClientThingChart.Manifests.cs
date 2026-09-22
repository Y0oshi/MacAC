namespace MacAC.Mechanics.Gear;

public sealed partial class ClientThingChart
{
    // An object touched by a manifest, remembered until the manifest is fully applied
    private readonly record struct Touched(ClientThing Item, bool Existed);

    public void BootstrapEquipmentManifest(uint wielderIdent, IReadOnlyList<WornItemRow> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        if (wielderIdent is 0u)
            return;

        List<uint> sequenced = new List<uint>(listings.Count);
        List<Touched> touched = new List<Touched>(listings.Count);
        List<uint> alteredShelves = new List<uint>();
        var incoming = OidsOf(listings, static rank => rank.Guid);

        // Anything this wielder wore that the manifest no longer lists is stripped.
        if (_worn.TryGet(wielderIdent, out List<uint>? precedingWorn))
        {
            foreach (uint precedingIdent in precedingWorn.ToArray())
            {
                if (incoming.Contains(precedingIdent) || !_objects.TryGetValue(precedingIdent, out ClientThing? preceding))
                    continue;
                bool wornHere = preceding.WielderIdent == wielderIdent
                    || (preceding.VesselTag == wielderIdent && preceding.CurrentlyEquippedLocale != WieldBitmask.None);
                if (!wornHere)
                    continue;

                Strip(preceding);
                if (_shelves.Evict(precedingIdent, exceptVesselIdent: 0u) is { } left)
                    alteredShelves.AddRange(left);
                touched.Add(new Touched(preceding, true));
            }
        }

        foreach (WornItemRow row in listings)
        {
            sequenced.Add(row.Guid);
            var objRef = Admit(row.Guid, out bool existed);
            var before = ObjectPlacement.From(objRef);
            objRef.VesselTag = 0u;
            objRef.VesselSlot = -1;
            objRef.WielderIdent = wielderIdent;
            objRef.CurrentlyEquippedLocale = row.EquipLocation;
            objRef.Priority = row.Priority;
            if (_shelves.Evict(row.Guid, exceptVesselIdent: 0u) is { } left)
                alteredShelves.AddRange(left);
            _shelves.Refile(objRef, before.ContainerId);
            _worn.Forget(row.Guid);
            touched.Add(new Touched(objRef, existed));
        }

        _worn.Set(wielderIdent, sequenced);
        ProclaimAll(touched);
        BroadcastShelfEdits(alteredShelves);
    }

    public void ReplaceContents(uint vesselIdent, IReadOnlyList<uint> oids)
    {
        ArgumentNullException.ThrowIfNull(oids);
        if (vesselIdent is 0u)
            return;

        ContainerSlotRow[] ranks = new ContainerSlotRow[oids.Count];
        for (int idx = 0; idx < ranks.Length; ++idx)
            ranks[idx] = new ContainerSlotRow(oids[idx], 0u);
        ReplaceContents(vesselIdent, ranks);
    }

    public void ReplaceContents(uint vesselIdent, IReadOnlyList<ContainerSlotRow> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        if (vesselIdent is 0u)
            return;

        List<uint> sequenced = new List<uint>(listings.Count);
        List<ClientThing> newcomers = new List<ClientThing>();
        foreach (ContainerSlotRow rank in listings)
        {
            sequenced.Add(rank.Guid);
            var objRef = Admit(rank.Guid, out bool existed);
            objRef.VesselKindHint = rank.ContainerType;
            if (!existed)
                newcomers.Add(objRef);
        }

        _shelves.Set(vesselIdent, sequenced);
        foreach (ClientThing gear in newcomers)
            ObjectAdded?.Invoke(gear);
        ContainerContentsReplaced?.Invoke(vesselIdent);
    }

    public bool HaltViewingInsides(uint vesselIdent)
    {
        if (vesselIdent is 0u || !_shelves.Shed(vesselIdent))
            return false;
        ContainerContentsReplaced?.Invoke(vesselIdent);
        return true;
    }

    public int HaltViewingInsidesTree(uint trunkVesselIdent)
    {
        if (trunkVesselIdent is 0u)
            return 0;

        Stack<uint> frontier = new Stack<uint>();
        HashSet<uint> observed = new HashSet<uint>();
        List<uint> dropped = new List<uint>();
        frontier.Push(trunkVesselIdent);
        while (frontier.TryPop(out uint vesselIdent))
        {
            if (!observed.Add(vesselIdent) || !_shelves.TryGet(vesselIdent, out List<uint>? insides))
                continue;

            foreach (uint descendantIdent in insides)
            {
                if (_shelves.Holds(descendantIdent))
                    frontier.Push(descendantIdent);
            }
            _shelves.Shed(vesselIdent);
            dropped.Add(vesselIdent);
        }

        foreach (uint vesselIdent in dropped)
            ContainerContentsReplaced?.Invoke(vesselIdent);
        return dropped.Count;
    }

    public void BootstrapSatchelManifest(uint holderIdent, IReadOnlyList<ContainerSlotRow> listings)
    {
        ArgumentNullException.ThrowIfNull(listings);
        if (holderIdent is 0u)
            return;

        List<uint> sequenced = new List<uint>(listings.Count);
        List<uint> alteredShelves = new List<uint>();
        List<Touched> touched = new List<Touched>(listings.Count);
        var incoming = OidsOf(listings, static rank => rank.Guid);

        // Anything the owner held that the manifest no longer lists is stripped.
        if (_shelves.TryGet(holderIdent, out List<uint>? precedingPinned))
        {
            foreach (uint precedingIdent in precedingPinned.ToArray())
            {
                if (incoming.Contains(precedingIdent)
                    || !_objects.TryGetValue(precedingIdent, out ClientThing? preceding)
                    || preceding.VesselTag != holderIdent)

                    continue;

                var before = ObjectPlacement.From(preceding);
                Strip(preceding);
                _worn.Shift(preceding.ObjectId, before, ObjectPlacement.From(preceding));
                touched.Add(new Touched(preceding, true));
            }
        }

        for (int idx = 0; idx < listings.Count; ++idx)
        {
            var row = listings[idx];
            sequenced.Add(row.Guid);
            var objRef = Admit(row.Guid, out bool existed);
            var before = ObjectPlacement.From(objRef);
            objRef.VesselTag = holderIdent;
            objRef.VesselSlot = idx;
            objRef.WielderIdent = 0u;
            objRef.CurrentlyEquippedLocale = WieldBitmask.None;
            objRef.VesselKindHint = row.ContainerType;
            if (_shelves.Evict(row.Guid, holderIdent) is { } left)
                alteredShelves.AddRange(left);
            _worn.Shift(row.Guid, before, ObjectPlacement.From(objRef));
            touched.Add(new Touched(objRef, existed));
        }

        _shelves.Set(holderIdent, sequenced);
        ProclaimAll(touched);
        ContainerContentsReplaced?.Invoke(holderIdent);
        BroadcastShelfEdits(alteredShelves);
    }

    private void ProclaimAll(List<Touched> touched)
    {
        foreach ((ClientThing gear, bool existed) in touched)
            Proclaim(gear, existed);
    }

    private static HashSet<uint> OidsOf<T>(IReadOnlyList<T> ranks, Func<T, uint> oid)
    {
        HashSet<uint> set = new HashSet<uint>();
        for (int idx = 0; idx < ranks.Count; ++idx)
            set.Add(oid(ranks[idx]));
        return set;
    }

    // Detaches an object from any pack, slot, wielder and worn location
    private static void Strip(ClientThing objRef)
    {
        objRef.VesselTag = 0u;
        objRef.VesselSlot = -1;
        objRef.WielderIdent = 0u;
        objRef.CurrentlyEquippedLocale = WieldBitmask.None;
        objRef.Priority = 0u;
    }
}
