using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Shell;

public sealed class CanonWindowOpacityDriver : IDisposable
{
    private static readonly HashSet<string> CommsPaneLabels = new(StringComparer.Ordinal)
    {
        PaneLabels.Chat,
        PaneLabels.CommsWindow1,
        PaneLabels.CommsWindow2,
        PaneLabels.CommsWindow3,
        PaneLabels.CommsWindow4,
    };

    private readonly CanonWindowKeeper _keeper;
    private readonly HashSet<CanonWindowHandle> _focused = [];
    private bool _destroyed;

    public CanonWindowOpacityDriver(
        CanonWindowKeeper manager,
        float defaultDensity,
        float engagedDensity)
    {
        _keeper = manager ?? throw new ArgumentNullException(nameof(manager));

        (DefaultDensity, EngagedDensity) = ChatOpacityTie.ApplyEngaged(
            System.Math.Clamp(defaultDensity, 0f, 1f),
            System.Math.Clamp(engagedDensity, 0f, 1f));

        _keeper.WindowRegistered += OnPaneRegistered;
        _keeper.WindowUnregistered += OnPaneUnregistered;
        foreach (CanonWindowHandle hnd in _keeper.Windows)
            if (CommsPaneLabels.Contains(hnd.Name))
                Fasten(hnd);
    }

    public float DefaultDensity { get; private set; }

    public float EngagedDensity { get; private set; }

    public void AssignDefaultDensity(float val)
    {
        if (_destroyed) return;
        (DefaultDensity, EngagedDensity) = ChatOpacityTie.AssignDefault(EngagedDensity, val);
        ReapplyAll();
    }

    public void AssignEngagedDensity(float val)
    {
        if (_destroyed) return;
        (DefaultDensity, EngagedDensity) = ChatOpacityTie.ApplyEngaged(DefaultDensity, val);
        ReapplyAll();
    }

    public void AssignOpacity(float defaultDensity, float engagedDensity)
    {
        if (_destroyed) return;
        (DefaultDensity, EngagedDensity) = ChatOpacityTie.AssignDefault(EngagedDensity, defaultDensity);
        (DefaultDensity, EngagedDensity) = ChatOpacityTie.ApplyEngaged(DefaultDensity, engagedDensity);
        ReapplyAll();
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _keeper.WindowRegistered -= OnPaneRegistered;
        _keeper.WindowUnregistered -= OnPaneUnregistered;
        foreach (CanonWindowHandle hnd in _keeper.Windows)
            if (CommsPaneLabels.Contains(hnd.Name))
                hnd.DescendantFocusChanged -= OnDescendantFocusAltered;
        _focused.Clear();
    }

    private void OnPaneRegistered(CanonWindowHandle hnd)
    {
        if (CommsPaneLabels.Contains(hnd.Name))
            Fasten(hnd);
    }

    private void OnPaneUnregistered(CanonWindowHandle hnd)
    {
        hnd.DescendantFocusChanged -= OnDescendantFocusAltered;
        _focused.Remove(hnd);
    }

    private void Fasten(CanonWindowHandle hnd)
    {
        hnd.DescendantFocusChanged += OnDescendantFocusAltered;
        Apply(hnd, hasFocus: false);
    }

    private void OnDescendantFocusAltered(CanonWindowHandle hnd, WidgetElem? focusedDescendant)
    {
        bool hasFocus = focusedDescendant is not null;
        if (hasFocus)
            _focused.Add(hnd);
        else
            _focused.Remove(hnd);
        Apply(hnd, hasFocus);
    }

    private void Apply(CanonWindowHandle hnd, bool hasFocus)
        => hnd.AssignDensity(hasFocus ? EngagedDensity : DefaultDensity);

    private void ReapplyAll()
    {
        foreach (CanonWindowHandle hnd in _keeper.Windows)
            if (CommsPaneLabels.Contains(hnd.Name))
                Apply(hnd, _focused.Contains(hnd));
    }
}
