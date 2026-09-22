using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

/// <summary>Raw keyboard edges plus the current held state, as the router consumes them.</summary>
public interface IKeyFeed
{
    /// <summary>Every up → down transition.</summary>
    event Action<Key, ModifierBits>? KeyDown;

    /// <summary>Every down → up transition.</summary>
    event Action<Key, ModifierBits>? KeyUp;

    bool IsPinned(Key tag);

    ModifierBits LatestModifiers { get; }
}
