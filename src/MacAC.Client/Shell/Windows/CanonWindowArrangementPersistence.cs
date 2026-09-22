using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Shell;

public sealed class CanonWindowArrangementPersistence : IDisposable
{
    private readonly CanonWindowKeeper _keeper;
    private readonly SettingsVault? _store;
    private readonly Func<string> _toonTag;
    private readonly Func<(int Width, int Height)> _monitorDims;
    private readonly HashSet<string> _phaseManagedVisPanes;
    private readonly List<CanonWindowHandle> _affixed = [];
    private readonly Dictionary<string, UiWindowArrangement> _defaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UiWindowSpot> _stances = new(StringComparer.Ordinal);
    private string? _restoredToon;
    private bool _restoring;
    private bool _destroyed;
    private bool _gameplayEngaged = true;

    public void AssignGameplayEngaged(bool engaged)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        _gameplayEngaged = engaged;
    }

    public CanonWindowArrangementPersistence(
        CanonWindowKeeper manager,
        SettingsVault? vault,
        Func<string> characterKey,
        Func<(int Width, int Height)> screenSize,
        IEnumerable<string>? phaseManagedVisPanes = null)
    {
        _keeper = manager ?? throw new ArgumentNullException(nameof(manager));
        _store = vault;
        _toonTag = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
        _monitorDims = screenSize ?? throw new ArgumentNullException(nameof(screenSize));
        _phaseManagedVisPanes = phaseManagedVisPanes is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(phaseManagedVisPanes, StringComparer.Ordinal);

        _keeper.WindowRegistered += OnPaneRegistered;
        _keeper.WindowUnregistered += OnPaneUnregistered;
        foreach (CanonWindowHandle hnd in manager.Windows)
            Fasten(hnd);
    }

    public void ReinstateAll(bool persistBack = true)
    {
        ReinstateAllCore(
                persistBack,
                revertVis: true);
    }

    public void ReinstateFollowingReadoutEdit()
    {
        ReinstateAllCore(
                persistBack: false,
                revertVis: false);
    }

    public void RestartToDefaults()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var monitor = ValidMonitorDims();
        _restoring = true;
        try
        {
            foreach (var hnd in _affixed)
            {
                UiWindowArrangement arrangement = PaneStanceGeo.Default(hnd.Name, _defaults[hnd.Name],
                    monitor.Width, monitor.Height, _defaults);
                UiWindowSpot stance = new UiWindowSpot(arrangement, monitor.Width, monitor.Height);
                _stances[hnd.Name] = stance;
                ImposeStance(hnd, stance, monitor, revertVis: false);
            }
        }
        finally { _restoring = false; }
    }

    public void ReflowToMonitor()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_gameplayEngaged) return;
        var monitor = ValidMonitorDims();
        _restoring = true;
        try
        {
            foreach (var hnd in _affixed)
            {
                UiWindowSpot stance = WithLatestPhase(hnd, _stances[hnd.Name]);
                ImposeStance(hnd, stance, monitor, revertVis: false);
            }
        }
        finally { _restoring = false; }
    }

    public void LimitAllToMonitor()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        var monitor = ValidMonitorDims();
        _restoring = true;
        try
        {
            foreach (CanonWindowHandle hnd in _affixed.ToArray())
            {
                float upperX = MathF.Max(0f, monitor.Width - hnd.Width);
                float upperY = MathF.Max(0f, monitor.Height - hnd.Height);
                float x = Math.Clamp(hnd.Left, 0f, upperX);
                float y = Math.Clamp(hnd.Top, 0f, upperY);
                if (x != hnd.Left || y != hnd.Top)
                    hnd.ShiftTo(x, y);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    public void PersistAll()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_gameplayEngaged) return;
        string toon = _toonTag();
        if (!CanPersist(toon)) return;
        foreach (CanonWindowHandle hnd in _affixed.ToArray())
            PersistStance(toon, hnd);
    }

    public void PersistNamed(string profileLabel)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(profileLabel);
        if (!_gameplayEngaged) return;
        foreach (CanonWindowHandle hnd in _affixed.ToArray())
            _store?.PersistNamedPaneArrangement(profileLabel, hnd.Name, Capture(hnd));
    }

    public void ReinstateNamed(string profileLabel)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        ArgumentNullException.ThrowIfNull(profileLabel);
        if (!_gameplayEngaged) return;
        var monitor = ValidMonitorDims();

        _restoring = true;
        try
        {
            foreach (CanonWindowHandle hnd in _affixed.ToArray())
            {
                var stored = _store?.PullNamedPaneArrangement(
                    profileLabel, hnd.Name, Capture(hnd));
                if (stored is not { } arrangement) continue;
                var backup = Capture(hnd);
                arrangement = MigrateAuthoredGeo(
                    arrangement,
                    AuthoredGeo(hnd, backup));
                Apply(
                    hnd,
                    arrangement,
                    monitor,
                    revertVis: !_phaseManagedVisPanes.Contains(hnd.Name));
                _stances[hnd.Name] = new UiWindowSpot(Capture(hnd), monitor.Width, monitor.Height);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _keeper.WindowRegistered -= OnPaneRegistered;
        _keeper.WindowUnregistered -= OnPaneUnregistered;
        foreach (CanonWindowHandle hnd in _affixed.ToArray())
            Unfasten(hnd);
        _affixed.Clear();
    }

    private void OnPaneRegistered(CanonWindowHandle hnd) => Fasten(hnd);

    private void OnPaneUnregistered(CanonWindowHandle hnd) => Unfasten(hnd);

    private void ReinstateAllCore(
        bool persistBack,
        bool revertVis)
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_gameplayEngaged) return;
        string toon = _toonTag();
        if (!CanPersist(toon)) return;
        var monitor = ValidMonitorDims();
        _restoredToon = toon;

        _restoring = true;
        try
        {
            foreach (CanonWindowHandle hnd in _affixed.ToArray())
            {
                ReinstateHnd(hnd, toon, monitor, persistBack, revertVis);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    private void ReinstateHnd(CanonWindowHandle hnd, string toon,
        (int Width, int Height) monitor, bool persistBack, bool revertVis)
    {
        var backup = PaneStanceGeo.Default(hnd.Name, _defaults[hnd.Name],
            monitor.Width, monitor.Height, _defaults);
        var stance = _store?.PullPaneStance(toon, ResolutionTag(monitor), hnd.Name, backup)
            ?? new UiWindowSpot(backup, monitor.Width, monitor.Height);
        stance = stance with
        {
            Layout = MigrateAuthoredGeo(stance.Layout, AuthoredGeo(hnd, backup)),
        };
        _stances[hnd.Name] = stance;
        ImposeStance(hnd, stance, monitor,
            revertVis && !_phaseManagedVisPanes.Contains(hnd.Name));
        stance = WithLatestPhase(hnd, stance);
        _stances[hnd.Name] = stance;
        if (persistBack)
            _store?.PersistPaneStance(toon, hnd.Name, stance);
    }

    private void Fasten(CanonWindowHandle hnd)
    {
        if (_affixed.Contains(hnd))
            return;
        _affixed.Add(hnd);
        _defaults[hnd.Name] = Capture(hnd);
        var monitor = ValidMonitorDims();
        _stances[hnd.Name] = new UiWindowSpot(Capture(hnd), monitor.Width, monitor.Height);
        hnd.Moved += OnGeoAltered;
        hnd.Resized += OnGeoAltered;
        hnd.StateChanged += OnAltered;
        if (!_phaseManagedVisPanes.Contains(hnd.Name))
        {
            hnd.Shown += OnAltered;
            hnd.Hidden += OnAltered;
        }
        if (_gameplayEngaged && _restoredToon is { } toon && toon == _toonTag())
        {
            _restoring = true;
            try { ReinstateHnd(hnd, toon, monitor, persistBack: true, revertVis: true); }
            finally { _restoring = false; }
        }
    }

    private void Unfasten(CanonWindowHandle hnd)
    {
        if (!_affixed.Remove(hnd))
            return;
        _defaults.Remove(hnd.Name);
        _stances.Remove(hnd.Name);
        hnd.Moved -= OnGeoAltered;
        hnd.Resized -= OnGeoAltered;
        hnd.StateChanged -= OnAltered;
        if (!_phaseManagedVisPanes.Contains(hnd.Name))
        {
            hnd.Shown -= OnAltered;
            hnd.Hidden -= OnAltered;
        }
    }

    private void OnGeoAltered(CanonWindowHandle hnd)
    {
        if (!_gameplayEngaged || _restoring || _destroyed) return;
        var monitor = ValidMonitorDims();
        _stances[hnd.Name] = new UiWindowSpot(Capture(hnd), monitor.Width, monitor.Height);
        OnAltered(hnd);
    }

    private static UiWindowSpot WithLatestPhase(CanonWindowHandle hnd, UiWindowSpot stance)
    {
        UiWindowArrangement latest = Capture(hnd);
        return stance with
        {
            Layout = stance.Layout with
            {
                Visible = latest.Visible,
                Collapsed = latest.Collapsed,
                Maximized = latest.Maximized,
            },
        };
    }

    private void PersistStance(string toon, CanonWindowHandle hnd)
    {
        UiWindowSpot stance = WithLatestPhase(hnd, _stances[hnd.Name]);
        _stances[hnd.Name] = stance;
        _store?.PersistPaneStance(toon, hnd.Name, stance);
    }

    private void OnAltered(CanonWindowHandle hnd)
    {
        if (!_gameplayEngaged || _restoring || _destroyed) return;
        string toon = _toonTag();
        if (!CanPersist(toon)) return;

        try
        {
            PersistStance(toon, hnd);
        }
        catch (Exception exc)
        {
            Console.WriteLine($"settings: window layout save failed [{hnd.Name}]: {exc.Message}");
        }
    }

    private static UiWindowArrangement Capture(CanonWindowHandle hnd)
    {
        RetainedWindowLedger phase = hnd.PhaseDriver?.GrabPanePhase() ?? default;
        return new UiWindowArrangement(
            hnd.Left,
            phase.PersistedTop ?? hnd.Top,
            hnd.Width,
            phase.PersistedHeight ?? hnd.Height,
            phase.RequestedVisible ?? hnd.IsVisible,
            phase.Collapsed,
            phase.Maximized,
            hnd.AuthoredGeoRev);
    }

    private static UiWindowArrangement MigrateAuthoredGeo(
        UiWindowArrangement stored,
        UiWindowArrangement authored)
    {
        return stored.AuthoredGeometryRevision == authored.AuthoredGeometryRevision
            ? stored
            : (stored with
            {
                Width = authored.Width,
                Height = authored.Height,
                AuthoredGeometryRevision = authored.AuthoredGeometryRevision,
            });
    }

    private static UiWindowArrangement AuthoredGeo(
        CanonWindowHandle hnd,
        UiWindowArrangement latest)
    {
        return latest with
        {
            Width = hnd.AuthoredWidth,
            Height = hnd.AuthoredHeight,
            AuthoredGeometryRevision = hnd.AuthoredGeoRev,
        };
    }

    private static void ImposeStance(CanonWindowHandle hnd, UiWindowSpot stance,
        (int Width, int Height) monitor, bool revertVis)
    {
        WidgetElem cycle = hnd.OuterCycle;
        float width = cycle.ResizeX
            ? LimitDimension(stance.Layout.Width, cycle.Width, cycle.MinWidth, cycle.MaxWidth, monitor.Width)
            : cycle.Width;
        float height = cycle.ResizeY
            ? LimitDimension(stance.Layout.Height, cycle.Height, cycle.MinHeight, cycle.MaxHeight, monitor.Height)
            : cycle.Height;
        UiWindowArrangement arrangement = PaneStanceGeo.Project(stance, monitor.Width, monitor.Height, width, height);
        Apply(hnd, arrangement, monitor, revertVis);
    }

    private static void Apply(
        CanonWindowHandle hnd,
        UiWindowArrangement arrangement,
        (int Width, int Height) monitor,
        bool revertVis)
    {
        WidgetElem cycle = hnd.OuterCycle;
        float width = LimitDimension(arrangement.Width, cycle.Width, cycle.MinWidth, cycle.MaxWidth, monitor.Width);
        float height = LimitDimension(arrangement.Height, cycle.Height, cycle.MinHeight, cycle.MaxHeight, monitor.Height);
        bool constrainRescale = cycle.ConstrainRescaleToParent;
        cycle.ConstrainRescaleToParent = false;
        try { hnd.RescaleTo(width, height); }
        finally { cycle.ConstrainRescaleToParent = constrainRescale; }

        float upperX = MathF.Max(0f, monitor.Width - hnd.Width);
        ApplyRest(hnd, arrangement, monitor, upperX, revertVis);
    }

    private static void ApplyRest(CanonWindowHandle hnd, UiWindowArrangement arrangement, (int Width, int Height) monitor, float upperX, bool revertVis)
    {
        float upperY = MathF.Max(0f, monitor.Height - hnd.Height);
        float x = Math.Clamp(FiniteOr(arrangement.X, hnd.Left), 0f, upperX);
        float y = Math.Clamp(FiniteOr(arrangement.Y, hnd.Top), 0f, upperY);
        ApplyTail(x, hnd, arrangement, y, revertVis);
    }

    private static void ApplyTail(float x, CanonWindowHandle hnd, UiWindowArrangement arrangement, float y, bool revertVis)
    {
        hnd.ShiftTo(x, y);
        hnd.PhaseDriver?.ReinstatePanePhase(new RetainedWindowLedger(
                            Collapsed: arrangement.Collapsed,
                            Maximized: arrangement.Maximized,
                            PersistedTop: y,
                            PersistedHeight: hnd.Height,
                            RequestedVisible: arrangement.Visible));
        if (revertVis)
        {
            if (arrangement.Visible) hnd.Display();
            else hnd.Hide();
        }
    }

    private (int Width, int Height) ValidMonitorDims()
    {
        var monitor = _monitorDims();
        return (Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
    }

    private static float LimitDimension(
        float stored,
        float latest,
        float floor,
        float ceiling,
        int monitorReach)
    {
        float val = FiniteOr(stored, latest);
        float upper = MathF.Max(floor, MathF.Min(ceiling, monitorReach));
        return Math.Clamp(val, floor, upper);
    }

    private static float FiniteOr(float val, float backup)
        => float.IsFinite(val) ? val : backup;

    private static string ResolutionTag((int Width, int Height) monitor)
        => $"{monitor.Width}x{monitor.Height}";

    private static bool CanPersist(string tag)
    {
        return !string.IsNullOrWhiteSpace(tag)
               && !string.Equals(tag, "default", StringComparison.OrdinalIgnoreCase);
    }
}
