using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public static class CanonScanCodeMap
{
    private const uint ShiftBit = 0x80000000u, CtrlBit = 0x40000000u, AltBit = 0x20000000u;
    private const uint LeadPointerScan = 0x0C;
    private const string PointerTicketStem = "DIMOFS_BUTTON";
    private const string TagTicketStem = "DIK_";

    // One scan code: its key, the name written to files, and any extra names accepted when reading
    private readonly record struct CockpitRow(uint Scan, Key Key, string Name, params string[] Aliases);

    private static readonly CockpitRow[] Ranks =
    [
        new(0x01, Key.Escape, "ESCAPE"),
        new(0x02, Key.Number1, "1"), new(0x03, Key.Number2, "2"), new(0x04, Key.Number3, "3"), new(0x05, Key.Number4, "4"), new(0x06, Key.Number5, "5"),
        new(0x07, Key.Number6, "6"), new(0x08, Key.Number7, "7"), new(0x09, Key.Number8, "8"), new(0x0A, Key.Number9, "9"), new(0x0B, Key.Number0, "0"),
        new(0x0C, Key.Minus, "MINUS"), new(0x0D, Key.Equal, "EQUALS"), new(0x0E, Key.Backspace, "BACK"), new(0x0F, Key.Tab, "TAB"),
        new(0x10, Key.Q, "Q"), new(0x11, Key.W, "W"), new(0x12, Key.E, "E"), new(0x13, Key.R, "R"), new(0x14, Key.T, "T"),
        new(0x15, Key.Y, "Y"), new(0x16, Key.U, "U"), new(0x17, Key.I, "I"), new(0x18, Key.O, "O"), new(0x19, Key.P, "P"),
        new(0x1A, Key.LeftBracket, "LBRACKET"), new(0x1B, Key.RightBracket, "RBRACKET"), new(0x1C, Key.Enter, "RETURN"), new(0x1D, Key.ControlLeft, "LCONTROL"),
        new(0x1E, Key.A, "A"), new(0x1F, Key.S, "S"), new(0x20, Key.D, "D"), new(0x21, Key.F, "F"), new(0x22, Key.G, "G"),
        new(0x23, Key.H, "H"), new(0x24, Key.J, "J"), new(0x25, Key.K, "K"), new(0x26, Key.L, "L"),
        new(0x27, Key.Semicolon, "SEMICOLON"), new(0x28, Key.Apostrophe, "APOSTROPHE"), new(0x29, Key.GraveAccent, "GRAVE"),
        new(0x2A, Key.ShiftLeft, "LSHIFT"), new(0x2B, Key.BackSlash, "BACKSLASH"),
        new(0x2C, Key.Z, "Z"), new(0x2D, Key.X, "X"), new(0x2E, Key.C, "C"), new(0x2F, Key.V, "V"), new(0x30, Key.B, "B"), new(0x31, Key.N, "N"), new(0x32, Key.M, "M"),
        new(0x33, Key.Comma, "COMMA"), new(0x34, Key.Period, "PERIOD"), new(0x35, Key.Slash, "SLASH"), new(0x36, Key.ShiftRight, "RSHIFT"),
        new(0x37, Key.KeypadMultiply, "NUMPADSTAR", "MULTIPLY"), new(0x38, Key.AltLeft, "LMENU", "LALT"), new(0x39, Key.Space, "SPACE"), new(0x3A, Key.CapsLock, "CAPITAL"),
        new(0x3B, Key.F1, "F1"), new(0x3C, Key.F2, "F2"), new(0x3D, Key.F3, "F3"), new(0x3E, Key.F4, "F4"), new(0x3F, Key.F5, "F5"),
        new(0x40, Key.F6, "F6"), new(0x41, Key.F7, "F7"), new(0x42, Key.F8, "F8"), new(0x43, Key.F9, "F9"), new(0x44, Key.F10, "F10"),
        new(0x45, Key.NumLock, "NUMLOCK"), new(0x46, Key.ScrollLock, "SCROLL"),
        new(0x47, Key.Keypad7, "NUMPAD7"), new(0x48, Key.Keypad8, "NUMPAD8"), new(0x49, Key.Keypad9, "NUMPAD9"), new(0x4A, Key.KeypadSubtract, "NUMPADMINUS", "SUBTRACT"),
        new(0x4B, Key.Keypad4, "NUMPAD4"), new(0x4C, Key.Keypad5, "NUMPAD5"), new(0x4D, Key.Keypad6, "NUMPAD6"), new(0x4E, Key.KeypadAdd, "NUMPADPLUS", "ADD"),
        new(0x4F, Key.Keypad1, "NUMPAD1"), new(0x50, Key.Keypad2, "NUMPAD2"), new(0x51, Key.Keypad3, "NUMPAD3"), new(0x52, Key.Keypad0, "NUMPAD0"),
        new(0x53, Key.KeypadDecimal, "DECIMAL", "NUMPADPERIOD"),
        new(0x57, Key.F11, "F11"), new(0x58, Key.F12, "F12"), new(0x64, Key.F13, "F13"), new(0x65, Key.F14, "F14"), new(0x66, Key.F15, "F15"),
        new(0x9C, Key.KeypadEnter, "NUMPADENTER"), new(0x9D, Key.ControlRight, "RCONTROL"), new(0xB5, Key.KeypadDivide, "NUMPADSLASH", "DIVIDE"),
        new(0xB7, Key.PrintScreen, "SYSRQ"), new(0xB8, Key.AltRight, "RALT", "RMENU"), new(0xC5, Key.Pause, "PAUSE"), new(0xC7, Key.Home, "HOME"),
        new(0xC8, Key.Up, "UPARROW", "UP"), new(0xC9, Key.PageUp, "PGUP", "PRIOR"), new(0xCB, Key.Left, "LEFT"), new(0xCD, Key.Right, "RIGHTARROW", "RIGHT"),
        new(0xCF, Key.End, "END"), new(0xD0, Key.Down, "DOWNARROW", "DOWN"), new(0xD1, Key.PageDown, "PGDN", "NEXT"),
        new(0xD2, Key.Insert, "INSERT"), new(0xD3, Key.Delete, "DELETE"), new(0xDB, Key.SuperLeft, "LWIN"), new(0xDC, Key.SuperRight, "RWIN"), new(0xDD, Key.Menu, "APPS"),
    ];

    private static readonly Dictionary<uint, CockpitRow> ByScan = Ranks.ToDictionary(r => r.Scan);
    private static readonly Dictionary<Key, CockpitRow> ByTag = LeadPerTag();
    private static readonly Dictionary<string, uint> ByTicket = TicketsToScans();

    /// <summary>Retail keeps its modifier bits in the top three bits of the control word.</summary>
    public static ModifierBits ToModifierBitmask(uint canonModifier)
    {
        return ((canonModifier & ShiftBit) is not 0 ? ModifierBits.Shift : 0)
        | ((canonModifier & CtrlBit) is not 0 ? ModifierBits.Ctrl : 0)
        | ((canonModifier & AltBit) is not 0 ? ModifierBits.Alt : 0);
    }

    public static Key? ToSilkTag(uint scan, uint dev)
    {
        return dev switch
        {
            0 => ByScan.TryGetValue(scan, out CockpitRow rank) ? rank.Key : null,
            1 => scan switch
            {
                0x0C => InputRouter.PointerBtnToTag(MouseButton.Left),
                0x0D => InputRouter.PointerBtnToTag(MouseButton.Right),
                0x0E => InputRouter.PointerBtnToTag(MouseButton.Middle),
                0x0F => InputRouter.PointerBtnToTag(MouseButton.Button4),
                _ => null,
            },
            _ => null,
        };
    }

    public static bool TryFromFileControl(string control, out uint scan, out uint dev)
    {
        scan = 0u;
        dev = 0u;
        if (string.IsNullOrWhiteSpace(control))
            return false;

        string ticket = control.Trim().ToUpperInvariant();
        if (ticket.StartsWith(PointerTicketStem, StringComparison.Ordinal)
            && int.TryParse(ticket[PointerTicketStem.Length..], out int btn)
            && btn is >= 0 and <= 4)
        {
            scan = LeadPointerScan + (uint)btn;
            dev = 1u;
            return true;
        }

        return ticket.StartsWith(TagTicketStem, StringComparison.Ordinal) && ByTicket.TryGetValue(ticket[TagTicketStem.Length..], out scan);
    }

    public static bool TryToFileControl(KeyStroke chord, out string control)
    {
        control = string.Empty;
        if (chord.Device is 1)
        {
            int btn = -1001 - (int)chord.Key; // mouse keys are encoded as -1001 - ordinal
            if (btn is < 0 or > 4)
                return false;
            control = $"{PointerTicketStem}{btn}";
            return true;
        }
        if (chord.Device is not 0 || !ByTag.TryGetValue(chord.Key, out CockpitRow rank))
            return false;

        control = TagTicketStem + rank.Name;
        return true;
    }

    private static Dictionary<Key, CockpitRow> LeadPerTag()
    {
        Dictionary<Key, CockpitRow> lookup = new Dictionary<Key, CockpitRow>();
        foreach (CockpitRow rank in Ranks)
            lookup.TryAdd(rank.Key, rank);
        return lookup;
    }

    private static Dictionary<string, uint> TicketsToScans()
    {
        var lookup = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (CockpitRow rank in Ranks)
        {
            lookup[rank.Name] = rank.Scan;
            foreach (string alias in rank.Aliases)
                lookup[alias] = rank.Scan;
        }
        return lookup;
    }
}
