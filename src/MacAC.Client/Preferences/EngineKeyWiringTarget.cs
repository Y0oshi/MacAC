using MacAC.Cockpit.Input;

namespace MacAC.Client.Preferences;

internal interface IEngineKeyWiringTarget
{
    void Apply(KeyBindingBook mappings);
}

internal sealed class EngineKeyWiringTarget : IEngineKeyWiringTarget
{
    private readonly InputRouter _router;
    private readonly string _trail;
    private readonly Action<string> _trace;

    public EngineKeyWiringTarget(
        InputRouter dispatcher,
        string trail,
        Action<string>? trace = null)
    {
        _router = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ArgumentException.ThrowIfNullOrWhiteSpace(trail);
        _trail = trail;
        _trace = trace ?? Console.WriteLine;
    }

    public void Apply(KeyBindingBook mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        _router.AssignMappings(mappings);
        try
        {
            mappings.StoreToFile(_trail);
            _trace($"keybinds: saved to {_trail}");
        }
        catch (Exception miss)
        {
            _trace($"keybinds: save failed: {miss.Message}");
        }
    }
}
