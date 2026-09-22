namespace MacAC.Mechanics.Gear;

public enum VendorSellRefusal
{
    /// <summary>Acceptable - stage the drop.</summary>
    None = 0,

    NotOwnedByPlayer,

    WrongType,

    CannotBeSoldHere,

    NoValue,

    TooValuable,

    TooCheap,
}

/// <summary>The retail client's pre-flight check on dropping an item onto a vendor's sell tab.</summary>
public static class VendorSellVerdict
{
    public const uint NoThreshold = uint.MaxValue;

    public static VendorSellRefusal Evaluate(
        bool possessedByAvatar,
        int containedGearTally,
        uint gearKindBitmask,
        int perUnitVal,
        uint merchandiseGearKinds,
        uint merchandiseLowerVal,
        uint merchandiseUpperVal,
        uint publicWeenieBitfield = 0u)
    {
        if (!possessedByAvatar)
            return VendorSellRefusal.NotOwnedByPlayer;

        // A pack with anything in it is always accepted for staging; the
        // server decides about its contents.
        if (containedGearTally > 0)
            return VendorSellRefusal.None;

        bool kept = (publicWeenieBitfield & (uint)PublicWeenieBits.Retained) is not 0u;
        bool wantedKind = (gearKindBitmask & merchandiseGearKinds) is not 0u;
        if (!wantedKind || kept)
            return VendorSellRefusal.WrongType;

        if (perUnitVal is 0)
            return VendorSellRefusal.NoValue;

        if (merchandiseUpperVal != NoThreshold && perUnitVal > merchandiseUpperVal)
        {
            bool note = (gearKindBitmask & (uint)GearKind.PromissoryNote) is not 0u;
            return note ? VendorSellRefusal.None : VendorSellRefusal.TooValuable;
        }

        if (merchandiseLowerVal != NoThreshold && perUnitVal < merchandiseLowerVal)
            return VendorSellRefusal.TooCheap;

        return VendorSellRefusal.None;
    }

    public static string? MsgFor(VendorSellRefusal rejection)
    {
        return rejection switch
        {
            VendorSellRefusal.None => null,
            VendorSellRefusal.NotOwnedByPlayer => "You can only sell items you are carrying",
            VendorSellRefusal.CannotBeSoldHere => "That item cannot be sold here",
            VendorSellRefusal.NoValue => "That item has no value and cannot be sold",
            VendorSellRefusal.TooCheap => "That item is too cheap to sell here",
            VendorSellRefusal.TooValuable => "That item is too valuable to sell here",
            _ => "You cannot sell that here",
        };
    }
}
