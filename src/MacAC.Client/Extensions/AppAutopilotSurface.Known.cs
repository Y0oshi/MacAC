using MacAC.Client.SimBridge;
using MacAC.Extensibility.Automation;
using MacAC.Mechanics.Arcana;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Gear;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Traits;
using MacAC.Sim;
using MacAC.Sim.Actors;

namespace MacAC.Client.Extensions;

internal sealed partial class AppAutopilotSurface
{
    public IReadOnlyList<SpellFacts> RecognizedSelfBuffs { get; private set; } = Array.Empty<SpellFacts>();

    public IReadOnlyList<SpellFacts> RecognizedAssaultArcana { get; private set; } = Array.Empty<SpellFacts>();

    public IReadOnlyList<SpellFacts> RecognizedFightingArcana { get; private set; } = Array.Empty<SpellFacts>();

    public double FetchCooldownLeftover(uint cooldownIdent)
    {
        Grimoire? grimoire;
        SimCore? core;
        lock (_latch)
        {
            grimoire = _grimoire;
            core = _runtime;
        }
        return grimoire is null || core is null || cooldownIdent is 0u
            ? 0d
            : grimoire.OnCooldown(
            cooldownIdent,
            core.Clock.SimulationMomentSecs,
            out double leftover)
                ? Math.Max(0d, leftover)
                : 0d;
    }

    public void PostSysMsg(string phrase)
    {
        if (string.IsNullOrEmpty(phrase))
            return;
        SimCommsLedger? communication;
        lock (_latch)
            communication = _communication;
        communication?.AddText(phrase, CanonLogTextType.Default);
    }

    public bool Submit(string phrase)
    {
        CurrentGameEngineBridge? directives;
        lock (_latch)
            directives = _sessDirectives;
        return directives?.SubmitCommsPhrase(phrase) == true;
    }

    public bool TrySeekObject(
        string label,
        in NavigationFix nearby,
        double ceilingGapMeters,
        out NavigationEntry val)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null
            || !IsAvailable
            || string.IsNullOrWhiteSpace(label)
            || !double.IsFinite(ceilingGapMeters)
            || ceilingGapMeters < 0d)
        {
            val = default;
            return false;
        }

        double closestGap = ceilingGapMeters;
        NavigationEntry closest = default;
        bool located = false;
        foreach (SimActorRecord capture in core.EntityObjects.Entities.ActiveRecords)
        {
            uint objectIdent = capture.ServerGuid;
            string contenderLabel = core.SatchelHolder.Objects.Get(objectIdent)?.Name
                ?? capture.Snapshot.Name
                ?? string.Empty;
            if (!contenderLabel.Equals(label, StringComparison.OrdinalIgnoreCase))
                continue;

            Locus? src = capture.KineticBody?.CellPosition
                ?? TranslateLocus(capture.Snapshot.Position);
            if (src is not { } locus)
                continue;
            var contender = ProjectNavigationLocus(locus);
            double gap = nearby.HorizontalGapMeters(contender);
            if (gap > closestGap)
                continue;

            closestGap = gap;
            closest = EnrichNavigationObject(
                new NavigationEntry(objectIdent, contenderLabel, contender),
                core.SatchelHolder.Objects.Get(objectIdent));
            located = true;
        }

        val = closest;
        return located;
    }

    public NavigationOutcome FaceHeading(float bearingDeg)
    {
        CurrentGameEngineBridge? directives;
        lock (_latch)
            directives = _sessDirectives;
        if (directives is null || !IsAvailable)
            return NavigationOutcome.Unavailable;
        var outcome =
            directives.MovementCommands.PivotToBearing(
                directives.Generation,
                bearingDeg);
        return outcome.Status == SimDirectiveStatus.Accepted
            ? NavigationOutcome.Accepted
            : NavigationOutcome.Rejected;
    }

    public ItemVerdict Open(uint vesselObjectIdent)
    {
        SimCore? core;
        Func<uint, bool>? use;
        lock (_latch)
        {
            core = _runtime;
            use = _useGear;
        }
        if (core is null || use is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        if (vesselObjectIdent is 0u
            || core.SatchelHolder.Objects.Get(vesselObjectIdent)
                is not { } vessel
            || ((PublicWeenieBits)(vessel.PublicWeenieBitfield ?? 0u)
                & (PublicWeenieBits.Corpse | PublicWeenieBits.Openable)) == 0)

            return new(ItemOutcome.InvalidTarget);
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : use(vesselObjectIdent)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public bool TryCaptureProperties(
        uint objectIdent,
        out ItemPropertySheet props)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
        {
            props = default;
            return false;
        }
        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (!TryFetchPossessed(objects, avatarIdent, objectIdent, out ClientThing? gear))
        {
            props = default;
            return false;
        }
        TraitBundle src = gear!.Properties;
        props = new ItemPropertySheet(
            new Dictionary<uint, int>(src.Ints),
            new Dictionary<uint, long>(src.Int64s),
            new Dictionary<uint, bool>(src.Bools),
            new Dictionary<uint, double>(src.Floats),
            new Dictionary<uint, string>(src.Texts),
            new Dictionary<uint, uint>(src.BlobIdents),
            new Dictionary<uint, uint>(src.InstIdents));
        return true;
    }

    public ItemVerdict Identify(uint objectIdent)
    {
        SimCore? core;
        Func<uint, bool>? recognize;
        lock (_latch)
        {
            core = _runtime;
            recognize = _recognizeGear;
        }
        if (core is null || recognize is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        uint trunk = core.SatchelHolder.ExternalVessels.LatestVesselIdent;
        var objects = core.SatchelHolder.Objects;
        ClientThing? gear = objectIdent is 0u ? null : objects.Get(objectIdent);
        bool corpse = gear is not null
            && ((PublicWeenieBits)(gear.PublicWeenieBitfield ?? 0u)
                & PublicWeenieBits.Corpse) != 0;
        bool latestSubstance = trunk is not 0u
            && GrabVesselIdents(objects, trunk).Contains(objectIdent);
        if (gear is null || (!corpse && !latestSubstance))

            return new(ItemOutcome.InvalidItem);
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : recognize(objectIdent)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    uint IWorldObjectControls.OpenContainerObjectId
    {
        get
        {
            lock (_latch)
                return _runtime?.SatchelHolder.ExternalVessels
                    .LatestVesselIdent ?? 0u;
        }
    }

    public bool HasModules(uint arcanumIdent)
    {
        SimArcanaCastLedger? casting;
        lock (_latch)
            casting = _casting;
        return casting is null || casting.HasRequiredModules(arcanumIdent);
    }

    public bool Cast(uint arcanumIdent) =>
        RequestCast(arcanumIdent) == CastRequestOutcome.Sent;

    public bool Cast(uint arcanumIdent, uint markObjectIdent) =>
        PickExplicitMark(markObjectIdent) && Cast(arcanumIdent);

    public CastRequestOutcome RequestCast(uint arcanumIdent)
    {
        SimArcanaCastLedger? casting;
        lock (_latch)
            casting = _casting;
        return casting is null
            ? CastRequestOutcome.Unavailable
            : casting.Cast(arcanumIdent) switch
            {
                CastingRequestOutcome.Sent => CastRequestOutcome.Sent,
                CastingRequestOutcome.UnknownSpell => CastRequestOutcome.UnknownSpell,
                CastingRequestOutcome.NoTarget => CastRequestOutcome.NoTarget,
                CastingRequestOutcome.IncompatibleTarget =>
                    CastRequestOutcome.IncompatibleTarget,
                CastingRequestOutcome.MissingComponents =>
                    CastRequestOutcome.MissingComponents,
                _ => CastRequestOutcome.Unavailable,
            };
    }

    public CastRequestOutcome RequestCast(
        uint arcanumIdent, uint markObjectIdent)
    {
        return PickExplicitMark(markObjectIdent)
            ? RequestCast(arcanumIdent)
            : CastRequestOutcome.IncompatibleTarget;
    }

    public EquipVerdict Wield(
        uint objectIdent,
        uint askedLocale = 0u)
    {
        Func<uint, uint, bool>? wield;
        Func<bool>? occupied;
        SimCore? core;
        lock (_latch)
        {
            wield = _wield;
            occupied = _equipmentOccupied;
            core = _runtime;
        }
        if (wield is null || core is null || !IsAvailable)
            return new(EquipOutcome.Unavailable);
        if (objectIdent is 0u
            || core.SatchelHolder.Objects.Get(objectIdent) is not { } gear
            || gear.ValidLocations == WieldBitmask.None)

            return new(EquipOutcome.InvalidItem);
        if (gear.CurrentlyEquippedLocale != WieldBitmask.None
            && (askedLocale is 0u
                || ((uint)gear.CurrentlyEquippedLocale & askedLocale)
                    == askedLocale))

            return new(EquipOutcome.AlreadyEquipped);
        return occupied?.Invoke() == true
            ? new(EquipOutcome.Busy)
            : wield(objectIdent, askedLocale)
            ? new(EquipOutcome.Started)
            : new(EquipOutcome.Refused);
    }

    public ItemVerdict Use(uint objectIdent)
        => RelayGear(objectIdent, 0u);

    public ItemVerdict Apply(uint objectIdent, uint markObjectIdent)
        => RelayGear(objectIdent, markObjectIdent);

    public ItemVerdict ShiftToVessel(
        uint objectIdent,
        uint vesselObjectIdent,
        uint quantity = 0u,
        int stance = 0)
    {
        Func<uint, uint, uint, int, bool>? relocate;
        SimCore? core;
        lock (_latch)
        {
            relocate = _relocateGear;
            core = _runtime;
        }
        if (core is null || relocate is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (!TryFetchPossessed(objects, avatarIdent, objectIdent, out ClientThing? gear))
            return new(ItemOutcome.InvalidItem);
        if (vesselObjectIdent is 0u
            || objects.Get(vesselObjectIdent) is not { } vessel
            || (vesselObjectIdent != avatarIdent
                && !IsAvatarPossessed(vessel, avatarIdent, objects)))

            return new(ItemOutcome.InvalidTarget);
        if (!ValidQuantity(gear!, quantity))
            return new(ItemOutcome.Refused, "Invalid stack quantity.");
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : relocate(objectIdent, vesselObjectIdent, quantity, stance)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public ItemVerdict Merge(
        uint srcObjectIdent,
        uint markObjectIdent,
        uint quantity = 0u)
    {
        Func<uint, uint, uint, bool>? combine;
        SimCore? core;
        lock (_latch)
        {
            combine = _combineGearList;
            core = _runtime;
        }
        if (core is null || combine is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (!TryFetchPossessed(objects, avatarIdent, srcObjectIdent, out ClientThing? src))
            return new(ItemOutcome.InvalidItem);
        if (!TryFetchPossessed(objects, avatarIdent, markObjectIdent, out _))
            return new(ItemOutcome.InvalidTarget);
        if (!ValidQuantity(src!, quantity))
            return new(ItemOutcome.Refused, "Invalid stack quantity.");
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : combine(srcObjectIdent, markObjectIdent, quantity)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public ItemVerdict Discard(uint objectIdent, uint quantity = 0u)
    {
        Func<uint, uint, bool>? discard;
        SimCore? core;
        lock (_latch)
        {
            discard = _discardGear;
            core = _runtime;
        }
        if (core is null || discard is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        var objects = core.SatchelHolder.Objects;
        if (!TryFetchPossessed(
                objects,
                core.AvatarIdentity.ServerGuid,
                objectIdent,
                out ClientThing? gear))

            return new(ItemOutcome.InvalidItem);
        if (!ValidQuantity(gear!, quantity))
            return new(ItemOutcome.Refused, "Invalid stack quantity.");
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : discard(objectIdent, quantity)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public ItemVerdict Hand(
        uint objectIdent,
        uint markObjectIdent,
        uint quantity = 0u)
    {
        Func<uint, uint, uint, bool>? hand;
        SimCore? core;
        lock (_latch)
        {
            hand = _handGear;
            core = _runtime;
        }
        if (core is null || hand is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        var objects = core.SatchelHolder.Objects;
        if (!TryFetchPossessed(
                objects,
                core.AvatarIdentity.ServerGuid,
                objectIdent,
                out ClientThing? gear))

            return new(ItemOutcome.InvalidItem);
        if (markObjectIdent is 0u || objects.Get(markObjectIdent) is null)
            return new(ItemOutcome.InvalidTarget);
        if (!ValidQuantity(gear!, quantity))
            return new(ItemOutcome.Refused, "Invalid stack quantity.");
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : hand(objectIdent, markObjectIdent, quantity)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public ItemVerdict Salvage(
        uint toolObjectIdent,
        IReadOnlyList<uint> gearObjectIdents)
    {
        Func<uint, IReadOnlyList<uint>, bool>? salvage;
        SimCore? core;
        lock (_latch)
        {
            salvage = _salvageGearList;
            core = _runtime;
        }
        if (core is null || salvage is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        if (gearObjectIdents is null || gearObjectIdents.Count is 0)
            return new(ItemOutcome.InvalidItem);

        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (!TryFetchPossessed(objects, avatarIdent, toolObjectIdent, out ClientThing? tool)
            || (tool!.Type & GearKind.TinkeringTool) == 0)

            return new(ItemOutcome.InvalidTarget);
        foreach (uint gearObjectIdent in gearObjectIdents)
        {
            if (!TryFetchPossessed(objects, avatarIdent, gearObjectIdent, out _))
                return new(ItemOutcome.InvalidItem);
        }
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : salvage(toolObjectIdent, gearObjectIdents)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    public ItemVerdict Vend(uint objectIdent, uint amount = 0u)
    {
        Func<uint, uint, int, bool>? vend;
        SimCore? core;
        lock (_latch)
        {
            vend = _vendGear;
            core = _runtime;
        }
        if (core is null || vend is null || !IsAvailable)
            return new(ItemOutcome.Unavailable);
        uint merchantIdent = core.SatchelHolder.Vendor.MerchantIdent;
        if (merchantIdent is 0u)
            return new(ItemOutcome.InvalidTarget, "No vendor is open.");

        var objects = core.SatchelHolder.Objects;
        uint avatarIdent = core.AvatarIdentity.ServerGuid;
        if (!TryFetchPossessed(objects, avatarIdent, objectIdent, out ClientThing? gear))
            return new(ItemOutcome.InvalidItem);
        if (!ValidQuantity(gear!, amount))
            return new(ItemOutcome.Refused, "Invalid stack quantity.");
        int qty = checked((int)(amount is 0u
            ? (uint)Math.Max(1, gear!.StackSize)
            : amount));
        int perUnitVal = VendorPriceRules.PerUnitVal(gear!.Value, gear.StackSize);
        var profile = core.SatchelHolder.Vendor.Profile;
        var rejection = VendorSellVerdict.Evaluate(
            possessedByAvatar: true,
            containedGearTally: objects.FetchInsides(objectIdent).Count,
            gearKindBitmask: (uint)gear.Type,
            perUnitVal,
            profile.MerchandiseItemTypes,
            profile.MerchandiseMinValue,
            profile.MerchandiseMaxValue,
            gear.PublicWeenieBitfield ?? 0u);
        if (rejection != VendorSellRefusal.None)
        {
            return new ItemVerdict(
                ItemOutcome.Refused,
                VendorSellVerdict.MsgFor(rejection));
        }
        return !core.SatchelHolder.Transactions.CanCommenceReq
            ? new(ItemOutcome.Busy)
            : vend(merchantIdent, objectIdent, qty)
            ? new(ItemOutcome.Started)
            : new(ItemOutcome.Refused);
    }

    internal static EntityClass ClassifyObject(ClientThing? gear)
    {
        if (gear is null)
            return EntityClass.Unknown;
        uint kind = (uint)gear.Type;
        uint flagSet = gear.PublicWeenieBitfield ?? 0u;
        EntityClass outcome = kind switch
        {
            _ when (kind & 0x00000001u) is not 0u => EntityClass.MeleeWeapon,
            _ when (kind & 0x00000002u) is not 0u => EntityClass.Armor,
            _ when (kind & 0x00000004u) is not 0u => EntityClass.Clothing,
            _ when (kind & 0x00000008u) is not 0u => EntityClass.Jewelry,
            _ when (kind & 0x00000010u) is not 0u => EntityClass.Monster,
            _ when (kind & 0x00000020u) is not 0u => EntityClass.Food,
            _ when (kind & 0x00000040u) is not 0u => EntityClass.Money,
            _ when (kind & 0x00000080u) is not 0u => EntityClass.Misc,
            _ when (kind & 0x00000100u) is not 0u => EntityClass.MissileWeapon,
            _ when (kind & 0x00000200u) is not 0u => EntityClass.Container,
            _ when (kind & 0x00000400u) is not 0u => EntityClass.Bundle,
            _ when (kind & 0x00000800u) is not 0u => EntityClass.Gem,
            _ when (kind & 0x00001000u) is not 0u => EntityClass.SpellComponent,
            _ when (kind & 0x00004000u) is not 0u => EntityClass.Key,
            _ when (kind & 0x00008000u) is not 0u => EntityClass.WandStaffOrb,
            _ when (kind & 0x00010000u) is not 0u => EntityClass.Portal,
            _ when (kind & 0x00040000u) is not 0u => EntityClass.TradeNote,
            _ when (kind & 0x00080000u) is not 0u => EntityClass.ManaStone,
            _ when (kind & 0x00100000u) is not 0u => EntityClass.Services,
            _ when (kind & 0x00200000u) is not 0u => EntityClass.Plant,
            _ when (kind & 0x00400000u) is not 0u => EntityClass.BaseCooking,
            _ when (kind & 0x00800000u) is not 0u => EntityClass.BaseAlchemy,
            _ when (kind & 0x01000000u) is not 0u => EntityClass.BaseFletching,
            _ when (kind & 0x02000000u) is not 0u => EntityClass.CraftedCooking,
            _ when (kind & 0x04000000u) is not 0u => EntityClass.CraftedAlchemy,
            _ when (kind & 0x08000000u) is not 0u => EntityClass.CraftedFletching,
            _ when (kind & 0x20000000u) is not 0u => EntityClass.Ust,
            _ when (kind & 0x40000000u) is not 0u => EntityClass.Salvage,
            _ => EntityClass.Unknown,
        };

        outcome = flagSet switch
        {
            _ when (flagSet & 0x00000008u) is not 0u => EntityClass.Player,
            _ when (flagSet & 0x00000200u) is not 0u => EntityClass.Vendor,
            _ when (flagSet & 0x00001000u) is not 0u => EntityClass.Door,
            _ when (flagSet & 0x00002000u) is not 0u => EntityClass.Corpse,
            _ when (flagSet & 0x00004000u) is not 0u => EntityClass.Lifestone,
            _ when (flagSet & 0x00008000u) is not 0u => EntityClass.Food,
            _ when (flagSet & 0x00010000u) is not 0u => EntityClass.HealingKit,
            _ when (flagSet & 0x00020000u) is not 0u => EntityClass.Lockpick,
            _ when (flagSet & 0x00040000u) is not 0u => EntityClass.Portal,
            _ when (flagSet & 0x00800000u) is not 0u => EntityClass.Foci,
            _ when (flagSet & 0x00000001u) is not 0u => EntityClass.Container,
            _ => outcome,
        };

        if ((kind & 0x00002000u) is not 0u && outcome == EntityClass.Unknown)
        {
            outcome = (flagSet & 0x00000002u) is not 0u
                ? EntityClass.Journal
                : (flagSet & 0x00000004u) is not 0u
                    ? EntityClass.Sign
                    : (flagSet & 0x0000000Fu) is not 0u
                        ? EntityClass.Book
                        : outcome;
        }
        if ((kind & 0x00002000u) is not 0u && gear.SpellId is > 0u)
            outcome = EntityClass.Scroll;
        if (outcome == EntityClass.Monster && (flagSet & 0x10u) is 0u)
            outcome = EntityClass.Npc;
        if (outcome == EntityClass.Monster && (flagSet & 0x04000000u) is not 0u)
            outcome = EntityClass.CombatPet;
        return outcome;
    }

    public CastReceipt LastCompletion
    {
        get
        {
            WatchSuccessfulOwnCasting();
            SimArcanaCastLedger? casting;
            lock (_latch)
                casting = _casting;
            SimArcanaCastFinish wrapUp =
                casting?.PreviousWrapUp ?? default;
            return new CastReceipt(
                wrapUp.Revision,
                wrapUp.SpellId,
                wrapUp.TargetObjectId,
                wrapUp.WeenieError);
        }
    }

    ItemUseReceipt IItemControls.LastCompletion
    {
        get
        {
            SimCore? core;
            lock (_latch)
                core = _runtime;
            SimItemUseFinish wrapUp =
                core?.ActHolder.Transactions.PreviousGearUseWrapUp ?? default;
            return new ItemUseReceipt(
                wrapUp.Revision,
                wrapUp.SourceObjectId,
                wrapUp.TargetObjectId,
                wrapUp.WeenieError);
        }
    }

    InventoryReceipt IItemControls.LastInventoryCompletion
    {
        get
        {
            lock (_latch)
                return _previousSatchelWrapUp;
        }
    }

    ItemUseReceipt ILootControls.LastItemUseCompletion =>
        ((IItemControls)this).LastCompletion;

    InventoryReceipt ILootControls.LastInventoryCompletion
    {
        get
        {
            lock (_latch)
                return _previousSatchelWrapUp;
        }
    }

    bool ISelectionControls.Execute(SelectionVerb act)
    {
        Func<SelectionVerb, bool>? perform;
        lock (_latch)
            perform = _destroyed ? null : _pickAct;
        return perform?.Invoke(act) == true;
    }

    void IProjectileControls.ShowDebugSamples(
        IReadOnlyList<ProjectileTraceSample> specimens)
    {
        ArgumentNullException.ThrowIfNull(specimens);
        const int ceilingMarkers = 4096;
        var detached = new List<ProjectileTraceSample>(
            Math.Min(specimens.Count, ceilingMarkers));
        for (int ordinal = 0; ordinal < specimens.Count && ordinal < ceilingMarkers; ++ordinal)
        {
            var specimen = specimens[ordinal];
            if (!float.IsFinite(specimen.WorldPosition.X)
                || !float.IsFinite(specimen.WorldPosition.Y)
                || !float.IsFinite(specimen.WorldPosition.Z)
                || !float.IsFinite(specimen.Radius)
                || specimen.Radius <= 0f)

                continue;
            detached.Add(specimen);
        }
        lock (_latch)
        {
            if (_destroyed)
                return;
            _missileDiagSpecimens = detached.Count is 0
                ? []
                : detached.ToArray();
            _missileDiagSpecimensExpireAt = Environment.TickCount64 + 350;
        }
    }

    private static ProjectilePathVerdict WithMissileDiagSpecimens(
        ProjectilePathVerdict outcome,
        List<ProjectileTraceSample>? specimens)
    {
        return specimens is null
            ? outcome
            : outcome with { DiagSpecimens = specimens.ToArray() };
    }

    bool IWorldObjectControls.TryCaptureProperties(
        uint objectIdent,
        out ItemPropertySheet props)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        var gear = core?.SatchelHolder.Objects.Get(objectIdent);
        if (core is null || !IsAvailable || gear is null)
        {
            props = default;
            return false;
        }
        props = GrabProps(gear.Properties);
        return true;
    }

    bool ILootControls.TryCaptureProperties(
        uint objectIdent,
        out ItemPropertySheet props)
    {
        SimCore? core;
        lock (_latch)
            core = _runtime;
        if (core is null || !IsAvailable)
        {
            props = default;
            return false;
        }

        uint trunk = core.SatchelHolder.ExternalVessels.LatestVesselIdent;
        var objects = core.SatchelHolder.Objects;
        if (trunk is 0u
            || !GrabVesselIdents(objects, trunk).Contains(objectIdent)
            || objects.Get(objectIdent) is not { } gear)
        {
            props = default;
            return false;
        }
        props = GrabProps(gear.Properties);
        return true;
    }

    ItemVerdict IWorldObjectControls.Identify(uint objectIdent) =>
        ((ILootControls)this).Identify(objectIdent);

    private static bool HasPropBlob(TraitBundle props)
    {
        return props.Ints.Count is not 0
        || props.Int64s.Count is not 0
        || props.Bools.Count is not 0
        || props.Floats.Count is not 0
        || props.Texts.Count is not 0
        || props.BlobIdents.Count is not 0
        || props.InstIdents.Count is not 0;
    }

    private static NavigationEntry EnrichNavigationObject(
        in NavigationEntry val,
        ClientThing? gear)
    {
        if (gear is null)
            return val;
        bool hasOpen = gear.Properties.Bools.TryGetValue(
            (uint)PropBool.Open,
            out bool isOpen);
        bool hasBolted = gear.Properties.Bools.TryGetValue(
            (uint)PropBool.Locked,
            out bool isBolted);
        return val with
        {
            IsDoor = ((PublicWeenieBits)(gear.PublicWeenieBitfield ?? 0u)
                & PublicWeenieBits.Door) != 0,
            IsOpen = hasOpen && isOpen,
            IsLocked = hasBolted && isBolted,
            HasLockPhase = hasOpen || hasBolted,
            LockDifficulty = gear.Properties.FetchInt(
                (uint)TraitInt.ResistLockpick),
        };
    }

    private static Locus? TranslateLocus(
        MacAC.Wire.Messages.ObjectCreation.RemotePosition? locus)
    {
        return locus is not { } val
            ? null
            : new Locus(
                val.LandblockId,
                new System.Numerics.Vector3(
                    val.PositionX,
                    val.PositionY,
                    val.PositionZ),
                new System.Numerics.Quaternion(
                    val.RotationX,
                    val.RotationY,
                    val.RotationZ,
                    val.RotationW));
    }
}
