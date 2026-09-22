using MacAC.Mechanics.Gear;

namespace MacAC.Client.Shell;

public enum GearDragSource { Inventory, ShortcutBar, Equipment, Ground }

public sealed record GearDragPayload(
    uint ObjId,
    GearDragSource SourceKind,  // what kind of slot it left
    int SourceSlot,
    WidgetGearSlot SourceCell,
    HotbarSlot? Shortcut = null); // lossless raw entry for shortcut-alias mutation
