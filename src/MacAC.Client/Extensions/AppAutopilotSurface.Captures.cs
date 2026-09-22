using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Traits;
using MacAC.Sim;
using MacAC.Sim.Actors;
using MacAC.Sim.Presence;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{

    public IReadOnlyList<ChatLine> GrabMsgs(ulong followingSeries)
    {
        lock (_latch)
        {
            if (_commsMsgs.Count is 0)
                return Array.Empty<ChatLine>();
            List<ChatLine> outcome = new List<ChatLine>();
            foreach (ChatLine msg in _commsMsgs)
            {
                if (msg.Sequence > followingSeries)
                    outcome.Add(msg);
            }
            return outcome.Count is 0
                ? []
                : outcome.ToArray();
        }
    }

    public IReadOnlyList<NavigationEntry> CaptureObjects()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<NavigationEntry>();

        var outcome = new List<NavigationEntry>();
        foreach (SimActorRecord capture in core.EntityObjects.Entities.ActiveRecords)
        {
            Locus? src = capture.KineticBody?.CellPosition
                ?? TranslateLocus(capture.Snapshot.Position);
            if (src is not { } locus)
                continue;
            var gear = core.SatchelHolder.Objects.Get(capture.ServerGuid);
            string label = gear?.Name
                ?? capture.Snapshot.Name
                ?? $"0x{capture.ServerGuid:X8}";
            outcome.Add(EnrichNavigationObject(
                new NavigationEntry(
                    capture.ServerGuid,
                    label,
                    ProjectNavigationLocus(locus)),
                gear));
        }
        outcome.Sort(static (left, right) => left.ObjectId.CompareTo(right.ObjectId));
        return outcome;
    }

    public IReadOnlyList<EquipmentEntry> GrabPossessedEquipment()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<EquipmentEntry>();

        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (avatarIdent is 0u)
            return Array.Empty<EquipmentEntry>();
        var objects = core.SatchelHolder.Objects;
        List<EquipmentEntry> built = new List<EquipmentEntry>();
        foreach (ClientThing gear in objects.Objects)
        {
            if (gear.ValidLocations == WieldBitmask.None
                || !IsAvatarPossessed(gear, avatarIdent, objects))

                continue;
            built.Add(new EquipmentEntry(
                gear.ObjectId,
                gear.Name,
                (uint)gear.Type,
                (uint)gear.ValidLocations,
                (uint)gear.CurrentlyEquippedLocale,
                gear.VesselTag,
                gear.WielderIdent,
                gear.CombatUse ?? 0,
                gear.Properties.FetchInt((uint)TraitInt.DamageType),
                gear.Properties.FetchInt((uint)TraitInt.WeaponSkill),
                gear.Properties.FetchInt((uint)TraitInt.Damage),
                gear.Properties.FetchFloat((uint)TraitFloat.DamageVariance))
            {
                AmmoType = gear.AmmoType ?? (uint)Math.Max(
                    0,
                    gear.Properties.FetchInt((uint)TraitInt.AmmoType)),
                StackSize = Math.Max(1, gear.StackSize),
                WeaponType = gear.Properties.FetchInt(
                    (uint)TraitInt.WeaponType),
            });
        }
        built.Sort(static (left, right) =>
        {
            int equipped = right.IsEquipped.CompareTo(left.IsEquipped);
            if (equipped is not 0)
                return equipped;
            int label = string.CompareOrdinal(left.Name, right.Name);
            return label is not 0
                ? label
                : left.ObjectId.CompareTo(right.ObjectId);
        });
        return built;
    }

    public IReadOnlyList<PackEntry> GrabPossessedGearList()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<PackEntry>();

        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (avatarIdent is 0u)
            return Array.Empty<PackEntry>();
        var objects = core.SatchelHolder.Objects;
        List<PackEntry> built = new List<PackEntry>();
        foreach (ClientThing gear in objects.Objects)
        {
            if (!IsAvatarPossessed(gear, avatarIdent, objects))
                continue;
            built.Add(ProjectSatchelGear(core, gear));
        }
        built.Sort(static (left, right) =>
        {
            int label = string.CompareOrdinal(left.Name, right.Name);
            return label is not 0 ? label : left.ObjectId.CompareTo(right.ObjectId);
        });
        return built;
    }

    public IReadOnlyList<CorpseEntry> GrabCorpses(
        float ceilingGap)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable
            || float.IsNaN(ceilingGap)
            || ceilingGap <= 0f)

            return Array.Empty<CorpseEntry>();

        var external =
            core.SatchelHolder.ExternalVessels;
        List<CorpseEntry> outcome = new List<CorpseEntry>();
        foreach (ClientThing contender in core.SatchelHolder.Objects.Objects)
        {
            if (((PublicWeenieBits)(contender.PublicWeenieBitfield ?? 0u)
                    & PublicWeenieBits.Corpse) == 0
                || contender.VesselTag is not 0u
                || !SimAllyTargetProbe.TryFetchGap(
                    core,
                    contender.ObjectId,
                    out float gap)
                || gap > ceilingGap)

                continue;

            outcome.Add(new CorpseEntry(
                contender.ObjectId,
                contender.WeenieClassIdent,
                contender.Name,
                gap,
                external.HasCorpseBeenOpened(contender.ObjectId),
                external.AskedVesselIdent == contender.ObjectId,
                external.LatestVesselIdent == contender.ObjectId)
            {
                LongBlurb = contender.Properties.ObtainString(
                    (uint)PropString.LongDesc),
                IsGeneratedRare = contender.Properties.FetchBool(
                    (uint)PropBool.CorpseGeneratedRare),
                IsIdentified = contender.Properties.Texts.ContainsKey(
                    (uint)PropString.LongDesc),
            });
        }
        outcome.Sort(static (left, right) =>
        {
            int gap = left.Distance.CompareTo(right.Distance);
            return gap is not 0
                ? gap
                : left.ObjectId.CompareTo(right.ObjectId);
        });
        return outcome.Count is 0
            ? []
            : outcome.ToArray();
    }

    public IReadOnlyList<PackEntry> GrabLatestInsides()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<PackEntry>();

        uint trunk = core.SatchelHolder.ExternalVessels.LatestVesselIdent;
        if (trunk is 0u)
            return Array.Empty<PackEntry>();

        var objects = core.SatchelHolder.Objects;
        List<PackEntry> outcome = new List<PackEntry>();
        HashSet<uint> visited = new HashSet<uint> { trunk };
        GrabVesselTree(core, objects, trunk, visited, outcome);
        return outcome.Count is 0
            ? []
            : outcome.ToArray();
    }

    public IReadOnlyList<FellowEntry> GrabParticipants()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable
            || !core.Fellowship.Snapshot.IsInFellowship)

            return Array.Empty<FellowEntry>();

        uint self = core.AvatarIdentity.ServerGuid;
        List<FellowEntry> outcome = new List<FellowEntry>();
        foreach (SimFellowMemberCapture participant
            in core.Fellowship.FetchParticipants())
        {
            if (participant.Guid == self
                || !SimAllyTargetProbe.TryFetchGap(
                    core,
                    participant.Guid,
                    out float gap))

                continue;
            outcome.Add(new FellowEntry(
                participant.Guid,
                participant.Name,
                participant.CurrentHealth,
                participant.MaxHealth,
                participant.CurrentStamina,
                participant.MaxStamina,
                participant.CurrentMana,
                participant.MaxMana,
                gap)
            {
                PortionLoot = participant.ShareLoot,
            });
        }
        return outcome.Count is 0
            ? []
            : outcome.ToArray();
    }

    public IReadOnlyList<FellowEntry> CaptureRoster() =>
        GrabFellowshipParticipants(includeSelf: true);

    public IReadOnlyList<TrackedEnchantmentFacts> Capture(uint markObjectIdent)
    {
        if (markObjectIdent is 0u)
            return Array.Empty<TrackedEnchantmentFacts>();
        WatchSuccessfulOwnCasting();
        var instant = DateTimeOffset.UtcNow;
        lock (_latch)
        {
            PruneFollowedEnchantments(instant);
            TrackedEnchantmentFacts[] outcome = [.. _followedEnchantments
                .Where(duo => duo.Key.Target == markObjectIdent)
                .Select(duo => new TrackedEnchantmentFacts(
                    duo.Key.Target,
                    duo.Key.Spell,
                    duo.Value.Family,
                    duo.Value.Quality,
                    duo.Value.IsUntargeted,
                    Math.Max(0d, (duo.Value.ExpiresAt - instant).TotalSeconds)))
                .OrderBy(static listing => listing.Family)
                .ThenBy(static listing => listing.SpellId)];
            return outcome.Length is 0
                ? []
                : outcome;
        }
    }

    public IReadOnlyList<HostileEntry> GrabHostileMarks(
        float ceilingGap)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<HostileEntry>();

        var grabbed =
            SimFoeTargetProbe.Capture(core, ceilingGap);
        if (grabbed.Count is 0)
            return Array.Empty<HostileEntry>();

        HostileEntry[] projected = new HostileEntry[grabbed.Count];
        Func<int, string> speciesLabel;
        lock (_latch)
            speciesLabel = _speciesLabel;
        for (int idx = 0; idx < grabbed.Count; ++idx)
        {
            var mark = grabbed[idx];
            projected[idx] = new HostileEntry(
                mark.ObjectId,
                mark.Name,
                mark.WeenieClassId,
                mark.Distance,
                mark.RelativeAngleDegrees,
                mark.IsHealthKnown,
                mark.HealthFraction)
            {
                SpeciesIdent = mark.SpeciesTag,
                SpeciesLabel = speciesLabel(mark.SpeciesTag),
                CeilingHealth = mark.MaximumHealth,
                HasShield = mark.HasShield,
                Incarnation = mark.Incarnation,
                HealthRev = mark.HealthRevision,
                SecsSinceHealthRefresh = mark.SecsSinceHealthUpdate,
            };
        }
        return projected;
    }

    internal IReadOnlyList<ProjectileTraceSample>
        GrabMissileDiagSpecimens()
    {
        lock (_latch)
        {
            return _destroyed
                || Environment.TickCount64 > _missileDiagSpecimensExpireAt
                ? Array.Empty<ProjectileTraceSample>()
                : _missileDiagSpecimens;
        }
    }
    IReadOnlyList<PeerEntry> INetworkControls.CaptureClients()
    {
        if (_signals is null)
            BroadcastCounterpartCapture();
        return _peers.GrabDistantClients();
    }

    IReadOnlyList<RosterEntry> ILoginControls.CaptureRoster()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || _destroyed)
            return Array.Empty<RosterEntry>();

        var lens = core.Session.ToonSelection;
        var capture = lens.Snapshot;
        RosterEntry[] outcome = new RosterEntry[capture.RosterCount];
        for (int ordinal = 0; ordinal < outcome.Length; ++ordinal)
        {
            if (!lens.TryFetchAt(ordinal, out SimToonPickEntry listing))
                return Array.Empty<RosterEntry>();
            outcome[ordinal] = new RosterEntry(
                listing.CharacterId,
                listing.Name,
                listing.ActiveIndex,
                listing.IsQueuedErase);
        }
        return outcome;
    }

    IReadOnlyList<WorldObjectEntry> IWorldObjectControls.CaptureObjects()
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
            return Array.Empty<WorldObjectEntry>();

        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        var outcome = new List<WorldObjectEntry>();
        HashSet<uint> grabbed = new HashSet<uint>();
        foreach (SimActorRecord capture in
            core.EntityObjects.Entities.ActiveRecords.ToArray())
        {
            var gear = objects.Get(capture.ServerGuid);
            outcome.Add(ProjectWorldObject(core, capture, gear, avatarIdent));
            grabbed.Add(capture.ServerGuid);
        }
        foreach (ClientThing gear in objects.Objects)
        {
            if (!grabbed.Add(gear.ObjectId))
                continue;
            outcome.Add(ProjectWorldObject(core, null, gear, avatarIdent));
        }
        outcome.Sort(static (left, right) => left.ObjectId.CompareTo(right.ObjectId));
        return outcome;
    }

    private static HashSet<uint> GrabVesselIdents(
        ClientThingChart objects,
        uint trunk)
    {
        HashSet<uint> outcome = new HashSet<uint>();
        Stack<uint> queued = new Stack<uint>();
        queued.Push(trunk);
        while (queued.Count is not 0)
        {
            uint vesselIdent = queued.Pop();
            foreach (uint descendantIdent in objects.FetchInsides(vesselIdent))
            {
                if (!outcome.Add(descendantIdent))
                    continue;
                if (objects.Get(descendantIdent) is { } descendant
                    && (descendant.ItemsCapacity is not 0
                        || descendant.ContainersCapacity is not 0
                        || (descendant.Type & GearKind.Container) != 0))

                    queued.Push(descendantIdent);
            }
        }
        return outcome;
    }

    private void GrabVesselTree(
        SimCore core,
        ClientThingChart objects,
        uint vesselIdent,
        HashSet<uint> visited,
        List<PackEntry> outcome)
    {
        foreach (uint descendantIdent in objects.FetchInsides(vesselIdent))
        {
            if (!visited.Add(descendantIdent)
                || objects.Get(descendantIdent) is not { } descendant)

                continue;
            outcome.Add(ProjectSatchelGear(core, descendant));
            if (descendant.ItemsCapacity is not 0
                || descendant.ContainersCapacity is not 0
                || (descendant.Type & GearKind.Container) != 0)
            {
                GrabVesselTree(core, objects, descendantIdent, visited, outcome);
            }
        }
    }

    private static ItemPropertySheet GrabProps(TraitBundle src)
    {
        return new(
            new Dictionary<uint, int>(src.Ints),
            new Dictionary<uint, long>(src.Int64s),
            new Dictionary<uint, bool>(src.Bools),
            new Dictionary<uint, double>(src.Floats),
            new Dictionary<uint, string>(src.Texts),
            new Dictionary<uint, uint>(src.BlobIdents),
            new Dictionary<uint, uint>(src.InstIdents));
    }

    private IReadOnlyList<FellowEntry> GrabFellowshipParticipants(bool includeSelf)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable
            || !core.Fellowship.Snapshot.IsInFellowship)

            return Array.Empty<FellowEntry>();

        uint self = core.AvatarIdentity.ServerGuid;
        List<FellowEntry> outcome = new List<FellowEntry>();
        foreach (SimFellowMemberCapture participant in core.Fellowship.FetchParticipants())
        {
            if (!includeSelf && participant.Guid == self)
                continue;
            float gap = 0f;
            if (participant.Guid != self
                && !SimAllyTargetProbe.TryFetchGap(
                    core,
                    participant.Guid,
                    out gap))

                continue;
            outcome.Add(new FellowEntry(
                participant.Guid,
                participant.Name,
                participant.CurrentHealth,
                participant.MaxHealth,
                participant.CurrentStamina,
                participant.MaxStamina,
                participant.CurrentMana,
                participant.MaxMana,
                gap)
            {
                PortionLoot = participant.ShareLoot,
            });
        }
        return outcome.Count is 0
            ? []
            : outcome.ToArray();
    }
}
