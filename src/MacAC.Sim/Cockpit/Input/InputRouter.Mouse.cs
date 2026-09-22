using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public sealed partial class InputRouter
{
    private void OnPointerDown(MouseButton btn, ModifierBits mods)
    {
        if (!Live)
            return;
        _pressTravel.Remove(btn);
        if (_grab is not null)
        {
            FinishGrab(PointerChord(btn, mods));
            return;
        }
        if (_pointer.WantCaptureMouse)
            return;

        KeyStroke chord = PointerChord(btn, mods);
        TriggerAll(chord, ActivationKind.Press, ActivationKind.Press);
        CommenceGrip(chord);
        if (First(chord, ActivationKind.Click) is not null)
            _pressTravel[btn] = 0f;

        long instant = _beatMsec();
        if (_previousDownBtn == btn && instant - _previousDownAtMsec <= DoublePressThresholdMsec)
        {
            TriggerAll(chord, ActivationKind.DoubleClick, ActivationKind.DoubleClick);
            _previousDownBtn = null;
        }
        else
        {
            _previousDownBtn = btn;
            _previousDownAtMsec = instant;
        }
    }

    private void OnPointerUp(MouseButton btn, ModifierBits mods)
    {
        if (!Live)
            return;
        KeyStroke chord = PointerChord(btn, mods);
        bool loaded = _pressTravel.Remove(btn, out float travel);

        TriggerAll(chord, ActivationKind.Release, ActivationKind.Release);
        FinishHolds(PointerBtnToTag(btn), dev: 1);

        if (loaded && !_pointer.WantCaptureMouse && travel <= PressPullThresholdPx)
            TriggerAll(chord, ActivationKind.Click, ActivationKind.Click);
    }

    private void OnPointerRelocate(float dx, float dy)
    {
        if (!Live || _pressTravel.Count is 0)
            return;
        if (_pointer.WantCaptureMouse)
        {
            _pressTravel.Clear();
            return;
        }

        float gap = MathF.Sqrt(dx * dx + dy * dy);
        foreach (MouseButton btn in _pressTravel.Keys.ToArray())
        {
            if (_pointer.IsHeld(btn))
                _pressTravel[btn] += gap;
            else
                _pressTravel.Remove(btn);
        }
    }

    // Only the sign matters: subscribers step by a fixed amount per tick
    private void OnRoll(float diff)
    {
        if (!Live || _pointer.WantCaptureMouse)
            return;
        if (diff > 0f)
            Fired?.Invoke(FeedAct.ScrollUp, ActivationKind.Press);
        else if (diff < 0f)
            Fired?.Invoke(FeedAct.ScrollDown, ActivationKind.Press);
    }
}
