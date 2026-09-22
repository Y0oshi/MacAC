using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

/// <summary>Binding lookup against the scope stack, and "is this action physically held" queries.</summary>
public sealed partial class InputRouter
{

    public bool IsActPinned(FeedAct act)
    {
        if (!Live || act == FeedAct.None || _pointer.WantCaptureKeyboard)
            return false;
        if (_automationPinned.Contains(act))
            return true;
        foreach (CockpitBinding binding in _bindings.ForAct(act))
        {
            if (ChordDown(binding.Chord) && PinnedResolution(binding.Chord, binding.Activation)?.Action == act)
                return true;
        }
        return false;
    }

    /// <summary>Mouse buttons are folded into the key space as negative values below -1000.</summary>
    public static Key PointerBtnToTag(MouseButton btn)
    {
        return btn switch
        {
            MouseButton.Left => (Key)(-1001),
            MouseButton.Right => (Key)(-1002),
            MouseButton.Middle => (Key)(-1003),
            MouseButton.Button4 => (Key)(-1004),
            MouseButton.Button5 => (Key)(-1005),
            _ => (Key)(-1000 - (int)btn),
        };
    }

    private CockpitBinding? First(KeyStroke chord, ActivationKind activation)
    {
        var strikes = Resolve(chord, activation);
        return strikes.Count is 0 ? null : strikes[0];
    }

    private IReadOnlyList<CockpitBinding> Resolve(KeyStroke chord, ActivationKind activation)
    {
        if (_alternateCam && InAmbit(InputLayer.Camera, chord, activation) is { Length: > 0 } cam)
            return cam;

        foreach (InputLayer ambit in _scopes)
        {
            if (ambit == InputLayer.Game && _fightingAmbit is { } fighting && InAmbit(fighting, chord, activation) is { Length: > 0 } topLayer)
                return topLayer;
            if (InAmbit(ambit, chord, activation) is { Length: > 0 } strikes)
                return strikes;
        }
        return [];
    }

    // One binding per distinct action, in book order
    private CockpitBinding[] InAmbit(InputLayer ambit, KeyStroke chord, ActivationKind activation)
    {
        return _bindings.All
            .Where(binding => binding.Scope == ambit && binding.Chord == chord && binding.Activation == activation)
            .DistinctBy(static binding => binding.Action)
            .ToArray();
    }

    private CockpitBinding? PinnedResolution(KeyStroke tied, ActivationKind activation)
    {
        var instant = _keyboard.LatestModifiers;
        if (First(tied with { Modifiers = instant }, activation) is { } precise)
            return precise;
        return tied.Modifiers == ModifierBits.None && instant == ModifierBits.Shift ? First(tied, activation) : null;
    }

    // The chord's key (or mouse button) is down and the held modifiers match, ignoring a stray Shift
    // on bare chords
    private bool ChordDown(KeyStroke chord)
    {
        bool tagDown = chord.Device switch
        {
            0 => _keyboard.IsPinned(chord.Key),
            1 => TagToPointerBtn(chord.Key) is { } btn && _pointer.IsHeld(btn),
            _ => false,
        };
        if (!tagDown)
            return false;

        var instant = _keyboard.LatestModifiers;
        if (chord.Modifiers == ModifierBits.None)
            instant &= ~ModifierBits.Shift;
        return instant == chord.Modifiers;
    }

    private static MouseButton? TagToPointerBtn(Key tag)
    {
        return (int)tag switch
        {
            -1001 => MouseButton.Left,
            -1002 => MouseButton.Right,
            -1003 => MouseButton.Middle,
            -1004 => MouseButton.Button4,
            -1005 => MouseButton.Button5,
            _ => null,
        };
    }

    private static bool IsModifierTag(Key tag)
    {
        return tag is
        Key.ShiftLeft or Key.ShiftRight or Key.ControlLeft or Key.ControlRight
        or Key.AltLeft or Key.AltRight or Key.SuperLeft or Key.SuperRight;
    }

    // A modifier key's own bit is dropped from its chord so "Shift" binds as a bare key
    private static KeyStroke KeyboardChord(Key tag, ModifierBits modifiers)
    {
        modifiers &= tag switch
        {
            Key.ShiftLeft or Key.ShiftRight => ~ModifierBits.Shift,
            Key.ControlLeft or Key.ControlRight => ~ModifierBits.Ctrl,
            Key.AltLeft or Key.AltRight => ~ModifierBits.Alt,
            Key.SuperLeft or Key.SuperRight => ~ModifierBits.Win,
            _ => ~ModifierBits.None,
        };
        return new KeyStroke(tag, modifiers, Device: 0);
    }

    private static KeyStroke PointerChord(MouseButton btn, ModifierBits modifiers) => new(PointerBtnToTag(btn), modifiers, Device: 1);

    // Fires every binding of one activation for the chord
    private void TriggerAll(KeyStroke chord, ActivationKind activation, ActivationKind triggerAs)
    {
        foreach (CockpitBinding mapping in Resolve(chord, activation))
            Fired?.Invoke(mapping.Action, triggerAs);
    }

    // Starts a hold: presses its actions now and remembers the chord for ticks and release
    private void CommenceGrip(KeyStroke chord)
    {
        var holds = Resolve(chord, ActivationKind.Hold);
        if (holds.Count is 0)
            return;
        foreach (CockpitBinding grip in holds)
            Fired?.Invoke(grip.Action, ActivationKind.Press);
        _pinnedChords.Add(chord);
    }

    // Ends every held chord on dev whose key matches, releasing its hold actions
    private void FinishHolds(Key tag, byte dev)
    {
        List<KeyStroke> ended = new List<KeyStroke>();
        foreach (KeyStroke pinned in _pinnedChords)
        {
            if (pinned.Key == tag && pinned.Device == dev)
                ended.Add(pinned);
        }
        foreach (KeyStroke pinned in ended)
        {
            _pinnedChords.Remove(pinned);
            TriggerAll(pinned, ActivationKind.Hold, ActivationKind.Release);
        }
    }
}
