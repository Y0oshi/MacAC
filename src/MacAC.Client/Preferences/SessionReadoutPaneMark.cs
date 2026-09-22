using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Preferences;

internal sealed class SessionReadoutPaneMark(
    IEngineDisplayWindowTarget window,
    bool fixedAutomationViewRect = false,
    bool straightToonLaunch = false) : IEngineDisplayWindowTarget
{
    private readonly IEngineDisplayWindowTarget _window = window
        ?? throw new ArgumentNullException(nameof(window));
    private ReadoutPrefs? _preferred;
    private bool _straightLaunchQueued = straightToonLaunch;

    public bool IsGameplay { get; private set; }

    public EngineDisplayApplyResult Apply(ReadoutPrefs readout)
    {
        ArgumentNullException.ThrowIfNull(readout);
        _preferred = readout;
        if (IsGameplay || fixedAutomationViewRect || _straightLaunchQueued)
            return _window.Apply(readout);

        _window.Apply(readout with { Resolution = "800x600", Fullscreen = false });
        return new EngineDisplayApplyResult(readout.Fullscreen);
    }

    public void AssignGameplay(bool engaged)
    {
        if (IsGameplay == engaged)
            return;
        IsGameplay = engaged;
        if (engaged)
            _straightLaunchQueued = false;
        if (_preferred is { } readout)
            Apply(readout);
    }

    public void FinishStraightLaunch()
    {
        if (!_straightLaunchQueued) return;
        _straightLaunchQueued = false;
        if (_preferred is { } readout)
            Apply(readout);
    }
}
