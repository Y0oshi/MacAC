using System.Collections.Concurrent;

namespace MacAC.Mechanics.Gear;

public sealed partial class ClientThingChart
{
    private readonly ConcurrentDictionary<uint, ClientThing> _objects = new();
    private readonly ConcurrentDictionary<uint, Vessel> _vessels = new();
    private readonly ShelfIndex _shelves;
    private readonly WornIndex _worn = new();
    private readonly PendingLedger _queued = new();
    private readonly Dictionary<uint, DeferredPlacement> _postponedStances = new();
    private readonly HashSet<ClientThing> _restrictionWatched =
        new(ReferenceEqualityComparer.Instance);
    private ulong _alterationRev;

    public ClientThingChart()
    {
        _shelves = new ShelfIndex(this);
        ObjectAdded += _ => ProgressAlterationRev();
        ObjectMoved += _ => ProgressAlterationRev();
        ObjectRemoved += _ => ProgressAlterationRev();
        ObjectUpdated += _ => ProgressAlterationRev();
        Cleared += ProgressAlterationRev;
    }

    public const uint WidgetFxListPropIdent = 18u;
    public const uint LatestWieldedLocalePropIdent = 10u;
    public const uint TapKindPropIdent = 151u;
    public const uint TapGearKindsPropIdent = 152u;
    public const uint SharedCooldownPropIdent = 280u;
    public const uint CooldownIntervalPropIdent = 167u;
    public const uint AvatarKillerConditionPropIdent = 134u;

    public event Action<ClientThing>? ObjectAdded;

    public event Action<ObjectRelocation>? ObjectMoved;

    public event Action<uint>? ContainerContentsReplaced;

    public event Action<ClientThing>? MoveRolledBack;

    public event Action<uint>? WieldConfirmed;

    public event Action<RelocationRefusal>? MoveRequestFailed;

    public event Action<ClientThing>? ObjectRemoved;

    public event Action<ObjectRemoval>? ObjectRemovalClassified;

    public event Action<ClientThing>? ObjectUpdated;

    public event Action<ClientThing>? StackSizeUpdated;

    public event Action? Cleared;

    public int ObjectCount => _objects.Count;
    public int VesselTally => _vessels.Count;
    public int VesselProjTally => _shelves.Count;
    public int EquipmentHolderTally => _worn.Count;
    public int QueuedRelocateTally => _queued.Count;

    public IEnumerable<ClientThing> Objects => _objects.Values;
    public IEnumerable<Vessel> Containers => _vessels.Values;

    internal ulong AlterationRev => _alterationRev;

    /// <summary>Look up an object by its server-assigned <c>ObjectId</c>.</summary>
    public ClientThing? Get(uint objectIdent) =>
        _objects.TryGetValue(objectIdent, out ClientThing? gear) ? gear : null;

    public Vessel? FetchVessel(uint objectIdent)
    {
        return _vessels.TryGetValue(objectIdent, out Vessel? vessel) ? vessel : null;
    }

    public bool IsPossessedByObject(uint objectIdent, uint holderIdent)
    {
        if (holderIdent is 0u || !_objects.TryGetValue(objectIdent, out ClientThing? gear))
            return false;

        if (gear.ObjectId == holderIdent || gear.VesselTag == holderIdent || gear.WielderIdent == holderIdent)
            return true;

        if (!_objects.ContainsKey(holderIdent))
            return false;

        if (gear.VesselTag is not 0u && _shelves.Lists(holderIdent, gear.VesselTag))
            return true;

        return _worn.Lists(holderIdent, gear.ObjectId);
    }

    public IReadOnlyList<uint> FetchInsides(uint vesselIdent) => _shelves.Snapshot(vesselIdent);

    public IReadOnlyList<ClientThing> FetchEquippedBy(uint wielderIdent)
    {
        if (!_worn.TryGet(wielderIdent, out List<uint>? stances))
            return [];

        List<ClientThing> worn = new List<ClientThing>(stances.Count);
        foreach (uint oid in stances)
        {
            var gear = Get(oid);
            if (gear is null || gear.CurrentlyEquippedLocale == WieldBitmask.None)
                continue;
            if (gear.WielderIdent == wielderIdent || gear.VesselTag == wielderIdent)
                worn.Add(gear);
        }
        return worn.ToArray();
    }

    public int TotalCarriedBurden(uint holderOid)
    {
        int sum = 0;
        foreach (ClientThing contender in _objects.Values)
        {
            if (IsCarriedBy(contender, holderOid))
                sum += contender.Burden;
        }
        return sum;
    }

    public void AppendOrRefresh(ClientThing gear)
    {
        ArgumentNullException.ThrowIfNull(gear);
        bool existed = _objects.TryGetValue(gear.ObjectId, out ClientThing? preceding);
        ObjectPlacement before = preceding is null ? ObjectPlacement.Nowhere : ObjectPlacement.From(preceding);
        Keep(gear);
        _worn.Shift(gear.ObjectId, before, ObjectPlacement.From(gear));
        Proclaim(gear, existed);
    }

    public void AppendVessel(Vessel vessel)
    {
        ArgumentNullException.ThrowIfNull(vessel);
        _vessels[vessel.ObjectId] = vessel;
    }

    public void Clear()
    {
        foreach (ClientThing gear in _restrictionWatched.ToArray())
            HaltWatchingRestrictions(gear);
        _objects.Clear();
        _vessels.Clear();
        _shelves.Clear();
        _worn.Clear();
        _queued.Clear();              // in-flight optimistic snapshots must not mis-rollback a recycled guid
        _postponedStances.Clear();   // stashed placements belong to the session that is ending
        Cleared?.Invoke();
    }

    private void ProgressAlterationRev() =>
        _alterationRev = checked(_alterationRev + 1UL);

    private bool IsCarriedBy(ClientThing contender, uint holderOid)
    {
        if (contender.WielderIdent == holderOid)
            return true;

        uint cur = contender.VesselTag;
        for (int hops = 0; cur is not 0u && hops < 8; ++hops)
        {
            if (cur == holderOid)
                return true;
            cur = _objects.TryGetValue(cur, out ClientThing? ancestor) ? ancestor.VesselTag : 0u;
        }
        return false;
    }

    // Returns the retained object for oid, minting and retaining a bare one when the session has not
    // met it yet
    private ClientThing Admit(uint oid, out bool existed)
    {
        existed = _objects.TryGetValue(oid, out ClientThing? objRef);
        if (existed && objRef is not null)
            return objRef;

        existed = false;
        objRef = new ClientThing { ObjectId = oid };
        Keep(objRef);
        return objRef;
    }

    private void Proclaim(ClientThing gear, bool existed)
    {
        if (existed) ObjectUpdated?.Invoke(gear);
        else ObjectAdded?.Invoke(gear);
    }

    private void Keep(ClientThing gear)
    {
        if (_objects.TryGetValue(gear.ObjectId, out ClientThing? preceding) && !ReferenceEquals(preceding, gear))
            HaltWatchingRestrictions(preceding);

        _objects[gear.ObjectId] = gear;
        MonitorRestrictions(gear);
    }

    private void MonitorRestrictions(ClientThing gear)
    {
        if (_restrictionWatched.Add(gear))
            gear.RestrictionAuthorityChanged += OnRestrictionArbiterAltered;
    }

    private void HaltWatchingRestrictions(ClientThing gear)
    {
        if (_restrictionWatched.Remove(gear))
            gear.RestrictionAuthorityChanged -= OnRestrictionArbiterAltered;
    }

    private void OnRestrictionArbiterAltered(ClientThing gear)
    {
        if (_objects.TryGetValue(gear.ObjectId, out ClientThing? kept) && ReferenceEquals(kept, gear))
            ProgressAlterationRev();
    }

    private void BroadcastShelfEdits(List<uint>? altered)
    {
        if (altered is null || altered.Count is 0)
            return;

        HashSet<uint> announced = new HashSet<uint>();
        foreach (uint vesselIdent in altered)
        {
            if (announced.Add(vesselIdent))
                ContainerContentsReplaced?.Invoke(vesselIdent);
        }
    }

    // A server move that arrived before the object it names
    private readonly record struct DeferredPlacement(ObjectPlacement Placement, uint? ContainerTypeHint);
}
