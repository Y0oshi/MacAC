namespace MacAC.Client.Shell;

public sealed class CanonWindowKeeper : IDisposable
{
    private readonly WidgetTrunk _trunk;
    private readonly Dictionary<string, CanonWindowHandle> _byLabel =
        new(StringComparer.Ordinal);
    private readonly Dictionary<WidgetElem, CanonWindowHandle> _byCycle = [];
    private readonly Dictionary<CanonWindowHandle, WidgetElem> _defaultFeeds = [];
    private bool _destroyed;

    internal CanonWindowKeeper(WidgetTrunk trunk)
    {
        _trunk = trunk;
        _trunk.ElementVisibilityChanged += OnElemVisAltered;
        _trunk.KeyboardFocusChanged += OnKeyboardFocusAltered;
        _trunk.PointerCaptureChanged += OnPtrGrabAltered;
        _trunk.WindowMoved += OnPaneMoved;
        _trunk.WindowResized += OnPaneResized;
        _trunk.UiLockChanged += OnWidgetMutexAltered;
    }

    public bool IsLocked => _trunk.WidgetBolted;
    public IReadOnlyCollection<CanonWindowHandle> Windows => _byLabel.Values;
    public event Action<string, bool>? WindowVisibilityChanged;

    public event Action<CanonWindowHandle>? WindowRegistered;

    internal event Action<CanonWindowHandle>? WindowRegistering;

    public event Action<CanonWindowHandle>? WindowUnregistered;

    public CanonWindowHandle Register(
        string label,
        WidgetElem outerCycle,
        WidgetElem? substanceTrunk = null,
        IRetainedPaneDriver? driver = null,
        IRetainedWindowStateDriver? phaseDriver = null,
        int authoredGeoRev = 0)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(outerCycle);
        authoredGeoRev = Math.Max(0, authoredGeoRev);

        if (!ReferenceEquals(outerCycle.Ancestor, _trunk))
            throw new InvalidOperationException(
                $"Retained window '{label}' has to be mounted as a direct WidgetTrunk child prior to registration");

        IRetainedWindowStateDriver? settledPhaseDriver =
            phaseDriver ?? outerCycle as IRetainedWindowStateDriver;

        if (_byLabel.TryGetValue(label, out var sameLabel))
        {
            if (ReferenceEquals(sameLabel.OuterCycle, outerCycle)
                && ReferenceEquals(sameLabel.SubstanceTrunk, substanceTrunk ?? outerCycle)
                && ReferenceEquals(sameLabel.Controller, driver)
                && ReferenceEquals(sameLabel.PhaseDriver, settledPhaseDriver)
                && sameLabel.AuthoredGeoRev == authoredGeoRev)
                return sameLabel;

            Unregister(label);
        }

        if (_byCycle.TryGetValue(outerCycle, out var sameCycle))
            Unregister(sameCycle.Name);

        CanonWindowHandle hnd = new CanonWindowHandle(
            this,
            label,
            outerCycle,
            substanceTrunk ?? outerCycle,
            driver,
            settledPhaseDriver,
            authoredGeoRev);
        _byLabel.Add(label, hnd);
        _byCycle.Add(outerCycle, hnd);
        WindowRegistering?.Invoke(hnd);
        hnd.AlertStartingPhase();
        WindowRegistered?.Invoke(hnd);
        return hnd;
    }

    public static int CalculateAuthoredGeoRev(
        float width, float height, float lowerWidth, float lowerHeight, bool resizable)
    {
        unchecked
        {
            int digest = 17;
            digest = (digest * 31) + BitConverter.SingleToInt32Bits(width);
            digest = (digest * 31) + BitConverter.SingleToInt32Bits(height);
            digest = (digest * 31) + BitConverter.SingleToInt32Bits(lowerWidth);
            digest = (digest * 31) + BitConverter.SingleToInt32Bits(lowerHeight);
            digest = (digest * 31) + (resizable ? 1 : 0);
            return digest & 0x7FFFFFFF;
        }
    }

    public bool TryGet(string label, out CanonWindowHandle hnd)
        => _byLabel.TryGetValue(label, out hnd!);

    public void FastenDriver(string label, IRetainedPaneDriver driver)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(driver);
        if (!_byLabel.TryGetValue(label, out var hnd))
            throw new KeyNotFoundException($"No retained window named '{label}' is registered");
        hnd.FastenDriver(driver);
    }

    public bool Show(string label)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        hnd.OuterCycle.Visible = true;
        BringToFront(hnd.OuterCycle);
        return true;
    }

    public bool Hide(string label)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        hnd.OuterCycle.Visible = false;
        return true;
    }

    public bool Toggle(string label)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        if (hnd.OuterCycle.Visible)
        {
            Hide(label);
            return false;
        }

        Show(label);
        return true;
    }

    public bool Close(string label)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        Hide(label);
        hnd.AlertClosed();
        return true;
    }

    public bool IsVisible(string label)
        => _byLabel.TryGetValue(label, out var hnd) && hnd.OuterCycle.Visible;

    public bool RelocateTo(string label, float left, float top)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        WidgetElem cycle = hnd.OuterCycle;
        if (cycle.ConstrainPullToParent && cycle.Ancestor is { } ancestor)
        {
            left = Math.Clamp(left, 0f, Math.Max(0f, ancestor.Width - cycle.Width));
            top = Math.Clamp(top, 0f, Math.Max(0f, ancestor.Height - cycle.Height));
        }
        if (cycle.Left == left && cycle.Top == top) return true;
        cycle.Left = left;
        cycle.Top = top;
        cycle.RestartMooringGrab();
        _trunk.AlertPaneMoved(cycle);
        return true;
    }

    public bool ReshapeTo(string label, float width, float height)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        WidgetElem cycle = hnd.OuterCycle;
        if (!cycle.ResizeX) width = cycle.Width;
        if (!cycle.ResizeY) height = cycle.Height;
        float upperWidth = cycle.MaxWidth;
        float upperHeight = cycle.MaxHeight;
        if (cycle.ConstrainRescaleToParent && cycle.Ancestor is { } ancestor)
        {
            upperWidth = MathF.Min(upperWidth, ancestor.Width - cycle.Left);
            upperHeight = MathF.Min(upperHeight, ancestor.Height - cycle.Top);
        }
        width = Math.Clamp(width, cycle.MinWidth, MathF.Max(cycle.MinWidth, upperWidth));
        height = Math.Clamp(height, cycle.MinHeight, MathF.Max(cycle.MinHeight, upperHeight));
        if (cycle.Width == width && cycle.Height == height) return true;
        cycle.Width = width;
        cycle.Height = height;
        cycle.RestartMooringGrab();
        _trunk.AlertPaneResized(cycle);
        return true;
    }

    public bool ApplyDensity(string label, float density)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;
        hnd.OuterCycle.Opacity = Math.Clamp(density, 0f, 1f);
        return true;
    }

    public void AssignBolted(bool bolted) => _trunk.WidgetBolted = bolted;

    public bool Unregister(string label)
    {
        if (!_byLabel.TryGetValue(label, out var hnd)) return false;

        if (hnd.OuterCycle.Visible)
            hnd.OuterCycle.Visible = false;
        else
            _trunk.WipeSubtreeOwnership(hnd.OuterCycle);

        _byLabel.Remove(label);
        _byCycle.Remove(hnd.OuterCycle);
        _defaultFeeds.Remove(hnd);
        hnd.AlertClosed();
        hnd.TeardownDriver();
        WindowUnregistered?.Invoke(hnd);
        return true;
    }

    public void BringToFront(WidgetElem pane)
    {
        int top = pane.ZOrder;
        foreach (var descendant in _trunk.Children)
            if (!ReferenceEquals(descendant, pane))
                top = Math.Max(top, descendant.ZOrder + 1);
        pane.ZOrder = top;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;

        foreach (string label in _byLabel.Keys.ToArray())
            Unregister(label);

        _trunk.ElementVisibilityChanged -= OnElemVisAltered;
        _trunk.KeyboardFocusChanged -= OnKeyboardFocusAltered;
        _trunk.PointerCaptureChanged -= OnPtrGrabAltered;
        _trunk.WindowMoved -= OnPaneMoved;
        _trunk.WindowResized -= OnPaneResized;
        _trunk.UiLockChanged -= OnWidgetMutexAltered;
    }

    internal bool TryGet(WidgetElem outerCycle, out CanonWindowHandle hnd)
        => _byCycle.TryGetValue(outerCycle, out hnd!);

    internal void ReadyToConceal(WidgetElem subtree)
    {
        if (!_byCycle.TryGetValue(subtree, out var hnd)) return;
        if (_trunk.DefaultPhraseFeed is { } feed && IsWithin(feed, subtree))
            _defaultFeeds[hnd] = feed;
    }

    internal void OnSubtreeRemoving(WidgetElem subtree)
    {
        foreach (var hnd in _byLabel.Values
            .Where(handle => IsWithin(handle.OuterCycle, subtree))
            .ToArray())
        {
            hnd.AlertVis(false);
            _byLabel.Remove(hnd.Name);
            _byCycle.Remove(hnd.OuterCycle);
            _defaultFeeds.Remove(hnd);
            hnd.AlertClosed();
            hnd.TeardownDriver();
            WindowUnregistered?.Invoke(hnd);
        }
    }

    private void OnElemVisAltered(WidgetElem elem, bool shown)
    {
        if (!_byCycle.TryGetValue(elem, out var hnd)) return;

        if (shown
            && _trunk.DefaultPhraseFeed is null
            && _defaultFeeds.TryGetValue(hnd, out var feed)
            && IsWithin(feed, hnd.OuterCycle))

            _trunk.DefaultPhraseFeed = feed;

        hnd.AlertVis(shown);
        WindowVisibilityChanged?.Invoke(hnd.Name, shown);
    }

    private void OnKeyboardFocusAltered(WidgetElem? formerFocus, WidgetElem? newFocus)
    {
        foreach (var hnd in _byLabel.Values.ToArray())
        {
            bool hadFocus = IsWithin(formerFocus, hnd.OuterCycle);
            bool hasFocus = IsWithin(newFocus, hnd.OuterCycle);
            if (hadFocus || hasFocus)
                hnd.AlertDescendantFocusAltered(hasFocus ? newFocus : null);
        }
    }

    private void OnPtrGrabAltered(WidgetElem? formerGrab, WidgetElem? newGrab)
    {
        foreach (var hnd in _byLabel.Values.ToArray())
        {
            bool hadGrab = IsWithin(formerGrab, hnd.OuterCycle);
            bool hasGrab = IsWithin(newGrab, hnd.OuterCycle);
            if (hadGrab || hasGrab)
                hnd.AlertDescendantGrabAltered(hasGrab ? newGrab : null);
        }
    }

    private void OnPaneMoved(string label, WidgetElem pane)
    {
        if (_byLabel.TryGetValue(label, out var hnd)
            && ReferenceEquals(hnd.OuterCycle, pane))
            hnd.AlertMoved();
    }

    private void OnPaneResized(string label, WidgetElem pane)
    {
        if (_byLabel.TryGetValue(label, out var hnd)
            && ReferenceEquals(hnd.OuterCycle, pane))
            hnd.AlertResized();
    }

    private void OnWidgetMutexAltered(bool bolted)
    {
        foreach (var hnd in _byLabel.Values.ToArray())
            hnd.AlertMutexAltered(bolted);
    }

    private static bool IsWithin(WidgetElem? elem, WidgetElem subtree)
    {
        while (elem is not null)
        {
            if (ReferenceEquals(elem, subtree)) return true;
            elem = elem.Ancestor;
        }
        return false;
    }
}
