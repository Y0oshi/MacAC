namespace MacAC.Mechanics.Gear;

[Flags]
public enum PublicWeenieBits : uint
{
    None = 0,
    Openable = 0x00000001,
    Inscribable = 0x00000002,
    Stuck = 0x00000004,
    Player = 0x00000008,
    Attackable = 0x00000010,
    PlayerKiller = 0x00000020,
    HiddenAdmin = 0x00000040,
    UiHidden = 0x00000080,
    Book = 0x00000100,
    Vendor = 0x00000200,
    PlayerKillerSwitch = 0x00000400,
    NonPlayerKillerSwitch = 0x00000800,
    Door = 0x00001000,
    Corpse = 0x00002000,
    Lifestone = 0x00004000,
    Food = 0x00008000,
    Healer = 0x00010000,
    Lockpick = 0x00020000,
    Portal = 0x00040000,
    Admin = 0x00100000,
    FreePlayerKiller = 0x00200000,
    ImmuneCellRestrictions = 0x00400000,
    RequiresPackSlot = 0x00800000,
    Retained = 0x01000000,
    PlayerKillerLite = 0x02000000,
    IncludesSecondHeader = 0x04000000,
    Bindstone = 0x08000000,
    VolatileRare = 0x10000000,
    WieldOnUse = 0x20000000,
    WieldLeft = 0x40000000,
}

public enum ItemPrimaryUseOutcome : ushort
{
    None = 0,
    ItemUse = 1,
    PlaceInBackpack = 2,
    WieldRight = 3,
    AutoSort = 4,
    OpenSecureTrade = 5,
    OpenSalvage = 6,
    BeginGame = 7,
    WieldLeft = 8,
}

/// <summary>Everything the interaction rules need to know about one object.</summary>
public readonly record struct ItemRulingSubject(
    uint Id,
    GearKind Type,
    PublicWeenieBits Flags,
    uint ContainerId,
    uint WielderId,
    WieldBitmask ValidLocations,
    WieldBitmask CurrentLocation,
    byte CombatUse,
    int ItemsCapacity,
    int ContainersCapacity,
    uint Useability,
    uint TargetType,
    bool OwnedByPlayer,
    bool IsContainer,
    bool IsComponentPack,
    int TradeState,
    int StackSize,
    int MaxSplitSize,
    bool IsIn3DView,
    string Name = "item")
{
    public bool IsPlayer => (Flags & PublicWeenieBits.Player) != 0;

    public bool Has(PublicWeenieBits bit) => (Flags & bit) != 0;

    public bool IsA(GearKind kind) => (Type & kind) != 0;

    public bool InBarter => TradeState is 1;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "item" : Name;
}

public enum ItemRulingActionKind
{
    PlaceInBackpack,
    WieldRight,
    AutoSort,
    OpenSecureTrade,
    OpenSalvage,
    BeginGame,
    WieldLeft,
    OpenContainedContainer,
    SetGroundObject,
    EnterTargetMode,
    SendUse,
    SendUseWithTarget,
    IncrementBusy,
    ConfirmPlayerKillerSwitch,
    ConfirmNonPlayerKillerSwitch,
    ConfirmVolatileRare,
    MergeStack,
    SellToVendor,
    StartSecureTrade,
    GiveToTarget,
    PlaceInContainer,
    SplitToWorld,
    DropToWorld,
    Reject,
}

public readonly record struct ItemRulingAction(
    ItemRulingActionKind Kind,
    uint ObjectId = 0,
    uint TargetId = 0,
    int Amount = 0,
    string? Message = null);

public readonly record struct ItemUseQuery(
    ItemRulingSubject Source,
    uint PlayerId,
    uint GroundObjectId,
    bool ReadyForInventoryRequest,
    uint ActiveVendorId,
    bool BypassClassification,
    bool UseCurrentSelection,
    ItemRulingSubject? SelectedTarget,
    bool ConfirmVolatileRareUses,
    bool InNonCombatMode);

public readonly record struct ItemUseRuling(bool Consumed, IReadOnlyList<ItemRulingAction> Actions);

public readonly record struct ItemPlacementQuery(
    ItemRulingSubject Item,
    uint PlayerId,
    uint GroundObjectId,
    bool ReadyForInventoryRequest,
    uint TargetId,
    ItemRulingSubject? Target,
    bool AllowGroundFallback,
    bool MergeAccepted,
    bool DragOnPlayerOpensSecureTrade,
    bool PlayerOnGround,
    int SplitSize);

public readonly record struct ItemPlacementRuling(bool ReturnValue, IReadOnlyList<ItemRulingAction> Actions);

public static class ItemInteractRules
{
    private const WieldBitmask CorpusSockets = (WieldBitmask)0x00007E00u;
    private const WieldBitmask ClothingSockets = (WieldBitmask)0x080001FFu;
    private const WieldBitmask PinnedSockets = (WieldBitmask)0x7C0F8000u;
    private const GearKind WearableKinds = GearKind.Armor | GearKind.Clothing | GearKind.Jewelry;

    private static readonly IReadOnlyList<ItemRulingAction> NoActs = [];

    public static bool IsToolbarUseEnabled(GearKind kind, byte fightingUse, uint useability)
    {
        return fightingUse is not 0 || (kind & WearableKinds) != 0 || GearUseability.IsUseable(useability);
    }

    /// <summary>What a plain double-click means for this item.</summary>
    public static ItemPrimaryUseOutcome DetermineUseOutcome(in ItemRulingSubject gear, uint avatarIdent, uint terrainObjectIdent)
    {
        bool loose = (gear.ContainerId is 0 && !gear.Has(PublicWeenieBits.Stuck))
            || (terrainObjectIdent is not 0 && gear.ContainerId == terrainObjectIdent);
        if (loose && (gear.WielderId is 0 || gear.WielderId == avatarIdent))
        {
            bool plain = !gear.Has(PublicWeenieBits.RequiresPackSlot) && gear.ItemsCapacity is 0 && gear.ContainersCapacity is 0;
            if (plain || gear.IsComponentPack)
                return ItemPrimaryUseOutcome.PlaceInBackpack;
        }

        if (gear.OwnedByPlayer)
        {
            bool wields = gear.CombatUse is not 0 || gear.IsA(GearKind.Caster) || gear.Has(PublicWeenieBits.WieldOnUse);
            if (wields && gear.WielderId != avatarIdent)
                return gear.Has(PublicWeenieBits.WieldLeft) ? ItemPrimaryUseOutcome.WieldLeft : ItemPrimaryUseOutcome.WieldRight;
            if (HasSpareSocketClan(gear.ValidLocations, gear.CurrentLocation))
                return ItemPrimaryUseOutcome.AutoSort;
            if (gear.IsA(GearKind.TinkeringTool))
                return ItemPrimaryUseOutcome.OpenSalvage;
        }
        else if (gear.IsA(GearKind.Gameboard))
        {
            return ItemPrimaryUseOutcome.BeginGame;
        }

        if (GearUseability.IsUseable(gear.Useability))
            return ItemPrimaryUseOutcome.ItemUse;
        if (gear.IsPlayer && gear.Id != avatarIdent)
            return ItemPrimaryUseOutcome.OpenSecureTrade;
        return ItemPrimaryUseOutcome.None;
    }

    public static ItemUseRuling DecideUse(in ItemUseQuery query)
    {
        var gear = query.Source;
        if (!query.ReadyForInventoryRequest)
            return Consumed(NoActs);
        if (query.ActiveVendorId is not 0 && gear.ContainerId == query.ActiveVendorId)
            return Consumed(NoActs);

        if (!query.BypassClassification)
        {
            var primary = DetermineUseOutcome(gear, query.PlayerId, query.GroundObjectId);
            if (primary is >= ItemPrimaryUseOutcome.PlaceInBackpack and <= ItemPrimaryUseOutcome.BeginGame)
                return Consumed(TrailUp(gear, query.GroundObjectId, primary));
        }

        if (gear.InBarter)
            return Refuse($"You cannot use the {gear.DisplayName} because you are trading it");
        if (gear.CurrentLocation == WieldBitmask.None && GearUseability.LeastLimitedSrcUse(gear.Useability) == GearUseability.Wielded)
            return Refuse($"You must wield the {gear.DisplayName} to use it");

        if (GearUseability.IsTargeted(gear.Useability))
        {
            if (!query.UseCurrentSelection)
                return Consumed([new ItemRulingAction(ItemRulingActionKind.EnterTargetMode, gear.Id)]);
            if (query.SelectedTarget is not { } mark)
                return Refuse($"Select your target before using the {gear.DisplayName}");
            if (WhyIncompatible(gear, mark, query.PlayerId) is { } cause)
                return Refuse(cause);

            return Consumed(WithTrailUp(
                [new(ItemRulingActionKind.SendUseWithTarget, gear.Id, mark.Id), new(ItemRulingActionKind.IncrementBusy)],
                gear, query.PlayerId, query.GroundObjectId));
        }

        if (GearUseability.IsUseable(gear.Useability))
        {
            if (gear.Has(PublicWeenieBits.PlayerKillerSwitch))
                return Consumed([new ItemRulingAction(ItemRulingActionKind.ConfirmPlayerKillerSwitch, gear.Id)]);
            if (gear.Has(PublicWeenieBits.NonPlayerKillerSwitch))
                return Consumed([new ItemRulingAction(ItemRulingActionKind.ConfirmNonPlayerKillerSwitch, gear.Id)]);
            if (gear.Has(PublicWeenieBits.VolatileRare) && query.ConfirmVolatileRareUses)
                return Consumed([new ItemRulingAction(ItemRulingActionKind.ConfirmVolatileRare, gear.Id)]);

            return Consumed(WithTrailUp(
                [new(ItemRulingActionKind.SendUse, gear.Id), new(ItemRulingActionKind.IncrementBusy)],
                gear, query.PlayerId, query.GroundObjectId));
        }

        var backup = TrailUp(gear, query.GroundObjectId, DetermineUseOutcome(gear, query.PlayerId, query.GroundObjectId));
        if (backup.Count is not 0)
            return Consumed(backup);
        if (gear.Id == query.PlayerId)
            return new ItemUseRuling(false, NoActs);
        if (gear.Has(PublicWeenieBits.Door))
            return Refuse($"You can't open or close this {gear.DisplayName} that way");
        if (gear.Has(PublicWeenieBits.Attackable) && query.InNonCombatMode)
            return Refuse($"To attack {gear.DisplayName}, click on the dove icon first");
        if (!gear.Has(PublicWeenieBits.Attackable) || query.InNonCombatMode)
            return Refuse($"The {gear.DisplayName} cannot be used");
        return new ItemUseRuling(false, NoActs);
    }

    public static bool IsObjectiveCompatible(in ItemRulingSubject src, in ItemRulingSubject mark, uint avatarIdent) =>
        WhyIncompatible(src, mark, avatarIdent) is null;

    public static ItemPlacementRuling DecideStance(in ItemPlacementQuery query)
    {
        var gear = query.Item;
        if (!query.ReadyForInventoryRequest)
            return Placed(false);

        if (query.TargetId == query.PlayerId)
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.PlaceInBackpack, gear.Id));
        if (!gear.OwnedByPlayer)
            return Placed(false, Rejection($"You must first pick up the {gear.DisplayName}"));
        if (gear.TradeState is not 0)
            return Placed(false, Rejection($"You are trading the {gear.DisplayName}, it cannot be dropped"));

        if (query.TargetId is 0)
            return query.AllowGroundFallback ? Ground(query) : Placed(false);
        if (query.MergeAccepted)
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.MergeStack, gear.Id, query.TargetId, query.SplitSize));
        if (query.Target is not { } mark)
            return query.AllowGroundFallback ? Ground(query) : Placed(false);

        if (mark.Has(PublicWeenieBits.Vendor))
        {
            return query.SplitSize >= gear.MaxSplitSize
                ? Placed(false, new ItemRulingAction(ItemRulingActionKind.SellToVendor, gear.Id, mark.Id, query.SplitSize))
                : Placed(false, Rejection("You must split the stack before selling it."));
        }
        if (query.DragOnPlayerOpensSecureTrade && mark.IsPlayer)
            return Placed(false, new ItemRulingAction(ItemRulingActionKind.StartSecureTrade, gear.Id, mark.Id, query.SplitSize));
        if (mark.Type == GearKind.Creature)
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.GiveToTarget, gear.Id, mark.Id, query.SplitSize));

        if (mark.IsContainer)
        {
            if (!mark.Has(PublicWeenieBits.Openable))
                return Placed(false, Rejection($"The {mark.DisplayName} is locked"));
            if (mark.Id != query.GroundObjectId)
                return Placed(false, Rejection($"You must open the {mark.DisplayName} first"));
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.PlaceInContainer, gear.Id, mark.Id, query.SplitSize));
        }

        return query.AllowGroundFallback ? Ground(query) : Placed(false, Rejection($"Cannot give {gear.DisplayName} to {mark.DisplayName}"));
    }

    private static string? WhyIncompatible(in ItemRulingSubject src, in ItemRulingSubject mark, uint avatarIdent)
    {
        if (src.InBarter)
            return $"You cannot use the {src.DisplayName} because you are trading it";
        if (mark.InBarter)
            return $"You can't use the {src.DisplayName} on an item you are trading";

        uint markBitset = GearUseability.MarkFlagSet(src.Useability);
        if (!mark.OwnedByPlayer)
        {
            uint loosest = GearUseability.LeastLimitedMarkUse(src.Useability);
            if ((loosest & GearUseability.Contained) is not 0)
            {
                bool selfAllowed = mark.Id == avatarIdent && (markBitset & GearUseability.Self) is not 0;
                if (!selfAllowed)
                    return $"You can't use the {src.DisplayName} on what you don't own";
            }
            else if ((loosest & GearUseability.Wielded) is not 0)
            {
                return $"You can't use the {src.DisplayName} on what you aren't wielding";
            }
        }

        if (mark.Id == avatarIdent && (markBitset & GearUseability.Self) is 0)
            return $"Cannot use the {src.DisplayName} on yourself";
        return (src.TargetType & (uint)mark.Type) is not 0 ? null : $"Cannot use the {src.DisplayName} with the {mark.DisplayName}";
    }

    // The primary action plus the container side effects the client always tacked on
    private static IReadOnlyList<ItemRulingAction> TrailUp(in ItemRulingSubject gear, uint terrainObjectIdent, ItemPrimaryUseOutcome primary)
    {
        var acts = new List<ItemRulingAction>(3);
        ItemRulingActionKind? lead = primary switch
        {
            ItemPrimaryUseOutcome.PlaceInBackpack => ItemRulingActionKind.PlaceInBackpack,
            ItemPrimaryUseOutcome.WieldRight => ItemRulingActionKind.WieldRight,
            ItemPrimaryUseOutcome.AutoSort => ItemRulingActionKind.AutoSort,
            ItemPrimaryUseOutcome.OpenSecureTrade => ItemRulingActionKind.OpenSecureTrade,
            ItemPrimaryUseOutcome.OpenSalvage => ItemRulingActionKind.OpenSalvage,
            ItemPrimaryUseOutcome.BeginGame => ItemRulingActionKind.BeginGame,
            ItemPrimaryUseOutcome.WieldLeft => ItemRulingActionKind.WieldLeft,
            _ => null,
        };
        if (lead is { } sort)
            acts.Add(new ItemRulingAction(sort, gear.Id));

        if (gear.IsContainer)
        {
            if (gear.OwnedByPlayer)
                acts.Add(new ItemRulingAction(ItemRulingActionKind.OpenContainedContainer, gear.Id));
            else if (GearUseability.IsUseable(gear.Useability) && !GearUseability.IsTargeted(gear.Useability))
                acts.Add(new ItemRulingAction(ItemRulingActionKind.SetGroundObject, gear.Id, terrainObjectIdent));
        }
        return acts;
    }

    private static List<ItemRulingAction> WithTrailUp(List<ItemRulingAction> acts, in ItemRulingSubject gear, uint avatarIdent, uint terrainObjectIdent)
    {
        acts.AddRange(TrailUp(gear, terrainObjectIdent, DetermineUseOutcome(gear, avatarIdent, terrainObjectIdent)));
        return acts;
    }

    // A slot family the item could occupy but currently does not
    private static bool HasSpareSocketClan(WieldBitmask valid, WieldBitmask latest)
    {
        return Release(valid, latest, CorpusSockets) || Release(valid, latest, ClothingSockets) || Release(valid, latest, PinnedSockets);
    }

    private static bool Release(WieldBitmask valid, WieldBitmask latest, WieldBitmask clan) =>
        (valid & clan) != 0 && (latest & clan) == 0;

    private static ItemPlacementRuling Ground(in ItemPlacementQuery query)
    {
        if (!query.PlayerOnGround)
            return Placed(false, Rejection("You cannot do that in mid air"));
        if (query.SplitSize < query.Item.MaxSplitSize)
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.SplitToWorld, query.Item.Id, Amount: query.SplitSize));
        if (!query.Item.IsIn3DView)
            return Placed(true, new ItemRulingAction(ItemRulingActionKind.DropToWorld, query.Item.Id));
        return Placed(false, Rejection("Move cancelled"));
    }

    private static ItemUseRuling Consumed(IReadOnlyList<ItemRulingAction> acts) => new(true, acts);

    private static ItemUseRuling Refuse(string msg) => Consumed([Rejection(msg)]);

    private static ItemRulingAction Rejection(string msg) => new(ItemRulingActionKind.Reject, Message: msg);

    private static ItemPlacementRuling Placed(bool returnVal, params ItemRulingAction[] acts) => new(returnVal, acts);
}
