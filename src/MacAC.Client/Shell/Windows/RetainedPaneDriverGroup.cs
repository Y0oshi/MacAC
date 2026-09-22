namespace MacAC.Client.Shell;

public sealed class RetainedPaneDriverGroup : IRetainedPaneDriver
{
    private readonly IRetainedPaneDriver[] _drivers;
    private bool _destroyed;

    public RetainedPaneDriverGroup(params IRetainedPaneDriver[] drivers)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        _drivers = (IRetainedPaneDriver[])drivers.Clone();
        for (int idx = 0; idx < _drivers.Length; ++idx)
            ArgumentNullException.ThrowIfNull(_drivers[idx]);
    }

    public void OnShown()
    {
        if (_destroyed) return;
        for (int idx = 0; idx < _drivers.Length; ++idx)
            _drivers[idx].OnShown();
    }

    public void OnConcealed()
    {
        if (_destroyed) return;
        for (int idx = _drivers.Length - 1; idx >= 0; --idx)
            _drivers[idx].OnConcealed();
    }

    public void OnDescendantFocusAltered(WidgetElem? focusedDescendant)
    {
        if (_destroyed) return;
        for (int idx = 0; idx < _drivers.Length; ++idx)
            _drivers[idx].OnDescendantFocusAltered(focusedDescendant);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        for (int idx = _drivers.Length - 1; idx >= 0; --idx)
            _drivers[idx].Dispose();
    }
}
