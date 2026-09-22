namespace MacAC.Mechanics.Gear;

[Flags]
public enum AetheriaSlotState : uint
{
    None = 0x0,
    Blue = 0x1,
    Yellow = 0x2,
    Red = 0x4,
    All = Blue | Yellow | Red,
}

public static class AetheriaSlots
{
    public const uint PropIdent = 0x142u;

    public static AetheriaSlotState Read(ClientThing? avatar)
    {
        return avatar is not null && avatar.Properties.Ints.TryGetValue(PropIdent, out int bitset)
            ? (AetheriaSlotState)(uint)bitset
            : AetheriaSlotState.None;
    }
}
