using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public readonly record struct KeyStroke(Key Key, ModifierBits Modifiers, byte Device = 0);
