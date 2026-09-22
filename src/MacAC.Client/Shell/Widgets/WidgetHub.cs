using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Gpu;
using Silk.NET.Input;

namespace MacAC.Client.Shell;

public sealed class WidgetHub : System.IDisposable
{
    public WidgetTrunk Root { get; } = new();
    public CanonWindowKeeper PaneKeeper => Root.PaneManager;
    public PhrasePainter TextRenderer { get; }
    public BitmapFont? DefaultTypeface { get; set; }

    public IKeyboard? Keyboard { get; private set; }

    private long _beginBeats = System.Environment.TickCount64;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly List<IRetainedWidgetInputWiring> _feedMappings = [];
    private AssetShutdownTransaction? _feedShutdown;
    private AssetShutdownTransaction? _shutdown;
    private bool _teardownAsked;

    internal bool IsDisposalComplete { get; private set; }

    internal WidgetHub(
        IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shaderDirection,
        BitmapFont? defaultTypeface = null)
        : this(dev, cycleSrc, shaderDirection, defaultTypeface, new HostQuiescenceTurnstile())
    {
    }

    internal WidgetHub(
        IClientGpuDevice dev,
        ILatestGpuCycleOrigin cycleSrc,
        string shaderDirection,
        BitmapFont? defaultTypeface,
        HostQuiescenceTurnstile quiescence)
    {
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        TextRenderer = new PhrasePainter(dev, cycleSrc, shaderDirection);
        DefaultTypeface = defaultTypeface;
    }

    public void Tick(double diffSecs)
    {
        long instant = System.Environment.TickCount64 - _beginBeats;
        Root.Tick(diffSecs, instant);
    }

    public void Draw(Vector2 monitorDims)
    {
        // Set UiRoot bounds to full screen so HitTestTopDown works
        Root.Width = monitorDims.X;
        Root.Height = monitorDims.Y;
        WidgetRenderScope cx = new WidgetRenderScope(TextRenderer, monitorDims, DefaultTypeface);
        TextRenderer.Begin(monitorDims);
        Root.Draw(cx);
        TextRenderer.Flush(DefaultTypeface);
    }

    public void WirePointer(IMouse pointer)
    {
        System.ObjectDisposedException.ThrowIf(_teardownAsked || IsDisposalComplete, this);
        System.ArgumentNullException.ThrowIfNull(pointer);

        RetainedMouseInputWiring mapping = new RetainedMouseInputWiring(
            new SilkKeptPointerCanvas(pointer),
            Root,
            _stillness);
        _feedMappings.Add(mapping);
        try
        {
            mapping.Attach();
        }
        catch
        {
            if (mapping.IsDisposalComplete)
                _feedMappings.Remove(mapping);
            throw;
        }
    }

    public void WireKeyboard(IKeyboard keyboard)
    {
        System.ObjectDisposedException.ThrowIf(_teardownAsked || IsDisposalComplete, this);
        System.ArgumentNullException.ThrowIfNull(keyboard);
        Keyboard = keyboard;   // last wired keyboard wins (one-keyboard desktop)
        var mapping = new RetainedKeyboardInputWiring(
            new SilkKeptKeyboardCanvas(keyboard),
            Root,
            _stillness);
        _feedMappings.Add(mapping);
        try
        {
            mapping.Attach();
        }
        catch
        {
            if (mapping.IsDisposalComplete)
                _feedMappings.Remove(mapping);
            throw;
        }
    }

    public void QuiesceInput()
    {
        _teardownAsked = true;
        foreach (IRetainedWidgetInputWiring mapping in _feedMappings)
            mapping.Deactivate();
        Keyboard = null;
    }

    public void DisarmFeed() => ConcludeFeedShutdown(dossierMisses: true);

    public CanonWindowHandle EnrollPane(
        string label,
        WidgetElem pane,
        WidgetElem? substanceTrunk = null,
        IRetainedPaneDriver? driver = null,
        IRetainedWindowStateDriver? phaseDriver = null,
        int authoredGeoRev = 0)
    {
        return Root.ListPane(
                label,
                pane,
                substanceTrunk,
                driver,
                phaseDriver,
                authoredGeoRev);
    }

    public bool WithdrawPane(string label) => Root.DelistPane(label);

    public bool RevealPane(string label) => Root.DisplayPane(label);

    public bool ConcealPane(string label) => Root.MaskPane(label);

    public bool SealPane(string label) => Root.ShutWindow(label);

    public bool IsPaneShown(string label) => Root.IsPaneVisible(label);

    public bool SwitchPane(string label) => Root.FlipWindow(label);

    public void Dispose()
    {
        if (IsDisposalComplete)
            return;

        _teardownAsked = true;
        _shutdown ??= BuildShutdownTransaction(
            [() => ConcludeFeedShutdown(dossierMisses: false)],
            () => { },
            PaneKeeper.Dispose,
            TextRenderer.Dispose);
        _shutdown.CompleteOrThrow();
        IsDisposalComplete = _shutdown.IsComplete;
    }

    internal static AssetShutdownTransaction BuildShutdownTransaction(
        IReadOnlyList<Action> inputUnsubscribers,
        Action freeFeedPhase,
        Action teardownPaneKeeper,
        Action teardownPhrasePainter)
    {
        ArgumentNullException.ThrowIfNull(inputUnsubscribers);
        ArgumentNullException.ThrowIfNull(freeFeedPhase);
        ArgumentNullException.ThrowIfNull(teardownPaneKeeper);
        ArgumentNullException.ThrowIfNull(teardownPhrasePainter);

        return new AssetShutdownTransaction(
            new AssetShutdownJuncture(
                "retained UI input subscriptions",
                [.. inputUnsubscribers
                    .Select((delist, ordinal) => new AssetShutdownOp(
                        $"input subscription {ordinal}",
                        delist ?? throw new ArgumentException(
                            "Input unsubscriber entries can't be null",
                            nameof(inputUnsubscribers))))]),
            new AssetShutdownJuncture("retained UI input state",
            [
                new("input state", freeFeedPhase),
            ]),
            new AssetShutdownJuncture("retained UI windows",
            [
                new("window manager", teardownPaneKeeper),
            ]),
            new AssetShutdownJuncture("retained UI renderer",
            [
                new("text renderer", teardownPhrasePainter),
            ]));
    }

    private void ConcludeFeedShutdown(bool dossierMisses)
    {
        QuiesceInput();

        _feedShutdown ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture(
                "retained UI input subscriptions",
                [.. _feedMappings
                    .AsEnumerable()
                    .Reverse()
                    .Select((mapping, ordinal) => new AssetShutdownOp(
                        $"input binding {ordinal}",
                        mapping.Dispose,
                        ResourceShutdownOperationRule.ReportAndContinue))]));
        _feedShutdown.CompleteOrThrow();
        if (_feedShutdown.IsComplete && _feedShutdown.TidyMisses.Count is 0)
            _feedMappings.Clear();
        if (dossierMisses && _feedShutdown.TidyMisses.Count is not 0)
        {
            throw new AggregateException(
                "Retained UI input callback cleanup completed with failures",
                _feedShutdown.TidyMisses.Select(static miss =>
                    new InvalidOperationException(
                        $"Retained UI operation '{miss.Operation}' failed in stage '{miss.Stage}'.",
                        miss.Error)));
        }
    }
}
