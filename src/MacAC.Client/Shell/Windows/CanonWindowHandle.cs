namespace MacAC.Client.Shell;

public sealed class CanonWindowHandle
{
    private readonly CanonWindowKeeper _holder;
    private bool _notifiedShown;
    private bool _closedSinceShown;
    private bool _destroyed;

    internal CanonWindowHandle(
        CanonWindowKeeper holder,
        string label,
        WidgetElem outerCycle,
        WidgetElem substanceTrunk,
        IRetainedPaneDriver? driver,
        IRetainedWindowStateDriver? phaseDriver,
        int authoredGeoRev)
    {
        _holder = holder;
        Name = label;
        OuterCycle = outerCycle;
        SubstanceTrunk = substanceTrunk;
        Controller = driver;
        PhaseDriver = phaseDriver;
        AuthoredGeoRev = Math.Max(0, authoredGeoRev);
        AuthoredWidth = outerCycle.Width;
        AuthoredHeight = outerCycle.Height;
        _notifiedShown = outerCycle.Visible;
    }

    public string Name { get; }
    public WidgetElem OuterCycle { get; }
    public WidgetElem SubstanceTrunk { get; }
    public IRetainedPaneDriver? Controller { get; private set; }
    public IRetainedWindowStateDriver? PhaseDriver { get; }
    public int AuthoredGeoRev { get; }
    public float AuthoredWidth { get; }
    public float AuthoredHeight { get; }
    public bool IsRegistered => !_destroyed;
    public bool IsVisible => OuterCycle.Visible;
    public bool IsLocked => _holder.IsLocked;
    public float Left => OuterCycle.Left;
    public float Top => OuterCycle.Top;
    public float Width => OuterCycle.Width;
    public float Height => OuterCycle.Height;
    public float Opacity => OuterCycle.Opacity;

    public event Action<CanonWindowHandle>? Shown;
    public event Action<CanonWindowHandle>? Hidden;
    public event Action<CanonWindowHandle>? Moved;
    public event Action<CanonWindowHandle>? Resized;
    public event Action<CanonWindowHandle>? Closed;

    internal event Action<CanonWindowHandle>? StateChanged;
    public event Action<CanonWindowHandle, bool>? LockChanged;
    public event Action<CanonWindowHandle, WidgetElem?>? DescendantFocusChanged;
    public event Action<CanonWindowHandle, WidgetElem?>? DescendantCaptureChanged;

    public bool Display() => IsRegistered && _holder.Show(Name);
    public bool Hide() => IsRegistered && _holder.Hide(Name);
    public bool Close() => IsRegistered && _holder.Close(Name);
    public bool ShiftTo(float left, float top)
        => IsRegistered && _holder.RelocateTo(Name, left, top);
    public bool RescaleTo(float width, float height)
        => IsRegistered && _holder.ReshapeTo(Name, width, height);
    public bool AssignDensity(float density)
        => IsRegistered && _holder.ApplyDensity(Name, density);

    internal void AlertStartingPhase()
    {
        if (_notifiedShown)
            Controller?.OnShown();
    }

    internal void FastenDriver(IRetainedPaneDriver driver)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(driver);
        if (ReferenceEquals(Controller, driver)) return;
        if (Controller is not null)
            throw new InvalidOperationException($"Retained window '{Name}' by now owns a controller");

        Controller = driver;
        if (_notifiedShown)
            Controller.OnShown();
    }

    internal void AlertVis(bool shown)
    {
        if (_notifiedShown == shown) return;
        _notifiedShown = shown;

        if (shown)
        {
            _closedSinceShown = false;
            Controller?.OnShown();
            Shown?.Invoke(this);
        }
        else
        {
            Controller?.OnConcealed();
            Hidden?.Invoke(this);
        }
    }

    internal void AlertMoved() => Moved?.Invoke(this);
    internal void AlertResized() => Resized?.Invoke(this);

    internal void AlertPhaseAltered() => StateChanged?.Invoke(this);

    internal void AlertClosed()
    {
        if (_closedSinceShown) return;
        _closedSinceShown = true;
        Closed?.Invoke(this);
    }

    internal void AlertMutexAltered(bool bolted) => LockChanged?.Invoke(this, bolted);

    internal void AlertDescendantFocusAltered(WidgetElem? focusedDescendant)
    {
        Controller?.OnDescendantFocusAltered(focusedDescendant);
        DescendantFocusChanged?.Invoke(this, focusedDescendant);
    }

    internal void AlertDescendantGrabAltered(WidgetElem? grabbedDescendant)
        => DescendantCaptureChanged?.Invoke(this, grabbedDescendant);

    internal void TeardownDriver()
    {
        if (_destroyed) return;
        _destroyed = true;
        Controller?.Dispose();
    }
}
