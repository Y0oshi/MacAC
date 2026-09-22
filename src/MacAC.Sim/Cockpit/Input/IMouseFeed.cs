using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

/// <summary>Raw mouse edges and motion, plus whether the UI layer has claimed the mouse or keyboard.</summary>
public interface IMouseFeed
{
    /// <summary>Every up → down transition.</summary>
    event Action<MouseButton, ModifierBits>? MouseDown;

    /// <summary>Every down → up transition.</summary>
    event Action<MouseButton, ModifierBits>? MouseUp;

    event Action<float, float>? MouseMove;

    /// <summary>Every wheel tick; positive is up.</summary>
    event Action<float>? Scroll;

    bool IsHeld(MouseButton btn);

    bool WantCaptureMouse { get; }

    bool WantCaptureKeyboard { get; }
}
