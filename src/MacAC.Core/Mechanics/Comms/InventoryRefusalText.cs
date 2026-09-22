using MacAC.Mechanics.Gear;

namespace MacAC.Mechanics.Comms;

/// <summary>"The X can't be moved - you're too busy" style refusals.</summary>
public static class InventoryRefusalText
{
    public static string? Compose(PackRequestKind sort, string gearLabel, uint weenieProblem)
    {
        string? verb = sort switch
        {
            PackRequestKind.Merge => "merged",
            PackRequestKind.SplitToContainer or PackRequestKind.SplitToWorld => "split",
            PackRequestKind.Move => "moved",
            PackRequestKind.Pickup => "picked up",
            PackRequestKind.PutInContainer => "put in the container",
            PackRequestKind.DropToWorld => "dropped",
            PackRequestKind.Wield => "wielded",
            PackRequestKind.Give => "given",
            _ => null,
        };
        return verb is null ? null : $"The {gearLabel} can't be {verb}{Reason(weenieProblem)}";
    }

    /// <summary>Errors whose own text already explains the failure.</summary>
    public static bool SuppressesGenericMissPhrase(uint weenieProblem)
    {
        return weenieProblem is 0x1Eu or 0x2Bu or 0x3EFu or 0x43Eu or 0x4CEu or 0x4CFu or 0x46Au;
    }

    private static string Reason(uint weenieProblem)
    {
        return weenieProblem switch
        {
            0x1Du => " - you're too busy",
            0x20u => " - you must control both objects",
            0x28u => " - the item is under someone else's control",
            0x2Au => " - you are too encumbered",
            0x36u => " - action cancelled",
            0x37u or 0x38u or 0x39u => " - unable to move to object",
            0x3EEu => " - the container is closed",
            _ => "",
        };
    }
}
