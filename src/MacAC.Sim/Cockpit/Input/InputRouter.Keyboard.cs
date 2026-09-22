using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public sealed partial class InputRouter
{
    private void OnTagDown(Key tag, ModifierBits mods)
    {
        if (!Live)
            return;
        if (_grab is not null)
        {
            if (tag == Key.Escape)
                FinishGrab(default);
            else if (IsModifierTag(tag))
                _grabModifier = tag;
            else
                FinishGrab(new KeyStroke(tag, mods, Device: 0));
            return;
        }
        if (_pointer.WantCaptureKeyboard)
            return;

        KeyStroke chord = KeyboardChord(tag, mods);
        LatestPhysicalChord = chord;
        try
        {
            TriggerAll(chord, ActivationKind.Press, ActivationKind.Press);
            TriggerAll(chord, ActivationKind.Click, ActivationKind.Click);
            CommenceGrip(chord);
        }
        finally
        {
            LatestPhysicalChord = null;
        }
    }

    private void OnTagUp(Key tag, ModifierBits mods)
    {
        if (!Live)
            return;
        if (_grab is not null)
        {
            if (_grabModifier == tag)
                FinishGrab(KeyboardChord(tag, mods));
            return;
        }

        TriggerAll(KeyboardChord(tag, mods), ActivationKind.Release, ActivationKind.Release);
        FinishHolds(tag, dev: 0);
    }
}
