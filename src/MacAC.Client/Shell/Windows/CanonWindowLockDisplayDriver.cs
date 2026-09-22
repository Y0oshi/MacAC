namespace MacAC.Client.Shell;

public sealed class CanonWindowLockDisplayDriver : IDisposable
{
    private static readonly (uint LockedStart, uint LiveStart)[] AuthoredChromeChunks =
    [
        (0x10000633u, 0x1000063Bu),
        (0x10000643u, 0x1000064Bu),
        (0x10000653u, 0x1000065Bu),
        (0x10000663u, 0x1000066Bu),
        (0x10000673u, 0x1000067Bu),
        (0x10000683u, 0x1000068Bu),
        (0x10000693u, 0x1000069Bu),
        (0x100006A5u, 0x100006ADu),
    ];

    private const uint SmartBboxOnlineChromeBegin = 0x100006CAu;

    private readonly CanonWindowKeeper _keeper;
    private readonly Dictionary<CanonWindowHandle, WindowDisplay> _panes = [];
    private bool _destroyed;

    public CanonWindowLockDisplayDriver(CanonWindowKeeper manager)
    {
        _keeper = manager ?? throw new ArgumentNullException(nameof(manager));
        _keeper.WindowRegistering += OnPaneRegistered;
        _keeper.WindowUnregistered += OnPaneUnregistered;

        foreach (CanonWindowHandle hnd in _keeper.Windows)
            Fasten(hnd);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _keeper.WindowRegistering -= OnPaneRegistered;
        _keeper.WindowUnregistered -= OnPaneUnregistered;
        foreach (CanonWindowHandle hnd in new List<CanonWindowHandle>(_panes.Keys))
            Unfasten(hnd);
    }

    private void OnPaneRegistered(CanonWindowHandle hnd) => Fasten(hnd);

    private void OnPaneUnregistered(CanonWindowHandle hnd) => Unfasten(hnd);

    private void Fasten(CanonWindowHandle hnd)
    {
        if (_panes.ContainsKey(hnd))
            return;

        WindowDisplay exhibit = WindowDisplay.Capture(hnd.OuterCycle);
        _panes.Add(hnd, exhibit);
        hnd.LockChanged += OnMutexAltered;
        exhibit.Apply(_keeper.IsLocked);
    }

    private void Unfasten(CanonWindowHandle hnd)
    {
        hnd.LockChanged -= OnMutexAltered;
        _panes.Remove(hnd);
    }

    private void OnMutexAltered(CanonWindowHandle hnd, bool bolted)
    {
        if (_panes.TryGetValue(hnd, out WindowDisplay? exhibit))
            exhibit.Apply(bolted);
    }

    private static bool IsAuthoredBoltedChrome(uint ident)
    {
        foreach ((uint begin, _) in AuthoredChromeChunks)
            if (ident >= begin && ident < begin + 8u)
                return true;
        return false;
    }

    private sealed class WindowDisplay
    {
        private readonly List<WidgetElem> _authoredBoltedChrome = [];
        private readonly List<(WidgetElem Element, bool VisibleWhenUnlocked)> _onlineChrome = [];
        private readonly List<(WidgetNineSlicePane Panel, bool VisibleWhenUnlocked)> _nineSlices = [];

        public static WindowDisplay Capture(WidgetElem outerCycle)
        {
            WindowDisplay exhibit = new WindowDisplay();
            exhibit.GrabElem(outerCycle);
            return exhibit;
        }

        public void Apply(bool bolted)
        {
            foreach (WidgetElem elem in _authoredBoltedChrome)
                elem.Visible = bolted;

            foreach ((WidgetElem elem, bool shownWhenUnlocked) in _onlineChrome)
                elem.Visible = !bolted && shownWhenUnlocked;

            foreach ((WidgetNineSlicePane board, bool shownWhenUnlocked) in _nineSlices)
                board.PaintRescaleAffordances = !bolted && shownWhenUnlocked;
        }

        private void GrabElem(WidgetElem elem)
        {
            if (elem is WidgetNineSlicePane nineSlice)
                _nineSlices.Add((nineSlice, nineSlice.PaintRescaleAffordances));

            if (IsAuthoredBoltedChrome(elem.DatElemIdent))
            {
                _authoredBoltedChrome.Add(elem);
            }
            else if (IsAuthoredOnlineChrome(elem.DatElemIdent) || elem is WidgetResizeGrip)
            {
                _onlineChrome.Add((elem, elem.Visible));
            }

            foreach (WidgetElem descendant in elem.Children)
                GrabElem(descendant);
        }
    }

    private static bool IsAuthoredOnlineChrome(uint ident)
    {
        foreach ((_, uint begin) in AuthoredChromeChunks)
            if (ident >= begin && ident < begin + 8u)
                return true;
        return ident is >= SmartBboxOnlineChromeBegin and < (SmartBboxOnlineChromeBegin + 8u);
    }
}
