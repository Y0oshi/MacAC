namespace MacAC.Cockpit.Input;

[Flags]
public enum ModifierBits : uint
{
    /// <summary>A bare key.</summary>
    None = 0,
    Shift = 0x01,
    Ctrl = 0x02,
    Alt = 0x04,
    Win = 0x08,
}
