namespace MacAC.Mechanics.Gear;

/// <summary>One shortcut on the hotbar: an item, a spell, or both.</summary>
public readonly record struct HotbarSlot(int Index, uint ObjectId, uint SpellId)
{
    public HotbarSlot WithOrdinal(int ordinal) => this with { Index = ordinal };
}
