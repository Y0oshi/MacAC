using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics;

internal sealed class NativePaneHookTidyPostponedFault()
    : InvalidOperationException(
        "Native window callback cleanup was requested reentrantly and remains pending.");

internal sealed class PaneHookMarks(
    Action load,
    Action<double> update,
    Action<double> render,
    Action closing,
    Action<bool> focusChanged,
    Action<Vector2D<int>> framebufferResize)
{
    public Action Load { get; } = load ?? throw new ArgumentNullException(nameof(load));
    public Action<double> Update { get; } = update ?? throw new ArgumentNullException(nameof(update));
    public Action<double> Render { get; } = render ?? throw new ArgumentNullException(nameof(render));
    public Action Closing { get; } = closing ?? throw new ArgumentNullException(nameof(closing));
    public Action<bool> FocusAltered { get; } = focusChanged ?? throw new ArgumentNullException(nameof(focusChanged));
    public Action<Vector2D<int>> FramebufferRescale { get; } = framebufferResize
            ?? throw new ArgumentNullException(nameof(framebufferResize));
}

internal interface IPaneHookCanvas
{
    void AppendPull(Action hook);
    void DropPull(Action hook);
    void AppendRefresh(Action<double> hook);
    void DropRefresh(Action<double> hook);
    void AppendRasterize(Action<double> hook);
    void DropRasterize(Action<double> hook);
    void AppendClosing(Action hook);
    void DropClosing(Action hook);
    void AppendFocusAltered(Action<bool> hook);
    void DropFocusAltered(Action<bool> hook);
    void AppendRelocate(Action<Vector2D<int>> hook);
    void DropRelocate(Action<Vector2D<int>> hook);
    void AppendPhaseAltered(Action<WindowState> hook);
    void DropPhaseAltered(Action<WindowState> hook);
    void AppendFramebufferRescale(Action<Vector2D<int>> hook);
    void DropFramebufferRescale(Action<Vector2D<int>> hook);
}

// Production adapter over Silk's native event surface
internal sealed class SilkPaneHookCanvas(IWindow window) : IPaneHookCanvas
{
    private readonly IWindow _window = window ?? throw new ArgumentNullException(nameof(window));

    public void AppendPull(Action hook) => _window.Load += hook;
    public void DropPull(Action hook) => _window.Load -= hook;
    public void AppendRefresh(Action<double> hook) => _window.Update += hook;
    public void DropRefresh(Action<double> hook) => _window.Update -= hook;
    public void AppendRasterize(Action<double> hook) => _window.Render += hook;
    public void DropRasterize(Action<double> hook) => _window.Render -= hook;
    public void AppendClosing(Action hook) => _window.Closing += hook;
    public void DropClosing(Action hook) => _window.Closing -= hook;
    public void AppendFocusAltered(Action<bool> hook) => _window.FocusChanged += hook;
    public void DropFocusAltered(Action<bool> hook) => _window.FocusChanged -= hook;
    public void AppendRelocate(Action<Vector2D<int>> hook) => _window.Move += hook;
    public void DropRelocate(Action<Vector2D<int>> hook) => _window.Move -= hook;
    public void AppendPhaseAltered(Action<WindowState> hook) => _window.StateChanged += hook;
    public void DropPhaseAltered(Action<WindowState> hook) => _window.StateChanged -= hook;
    public void AppendFramebufferRescale(Action<Vector2D<int>> hook) =>
        _window.FramebufferResize += hook;
    public void DropFramebufferRescale(Action<Vector2D<int>> hook) =>
        _window.FramebufferResize -= hook;
}

internal sealed class SilkWindowCallbackWiring : IDisposable
{
    private enum LifespanPhase
    {
        Created,
        Attaching,
        Attached,
        AttachFailed,
        Detaching,
        DetachPending,
        Detached,
    }

    private const int PullOrdinal = 0;
    private const int RefreshOrdinal = 1;
    private const int PrimaryRasterizeOrdinal = 2;
    private const int PacingRasterizeOrdinal = 3;
    private const int ClosingOrdinal = 4;
    private const int FocusAlteredOrdinal = 5;
    private const int RelocateOrdinal = 6;
    private const int PhaseAlteredOrdinal = 7;
    private const int FramebufferRescaleOrdinal = 8;
    private const int AffixTally = 9;

    private readonly IPaneHookCanvas _canvas;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action _pull;
    private readonly Action<double> _refresh;
    private readonly Action<double> _primaryRasterize;
    private readonly Action<double> _pacingRasterize;
    private readonly Action _closing;
    private readonly Action<bool> _focusAltered;
    private readonly Action<Vector2D<int>> _moved;
    private readonly Action<WindowState> _phaseAltered;
    private readonly Action<Vector2D<int>> _framebufferRescale;
    private readonly AssetShutdownTransaction _unfasten;
    private readonly bool[] _affixed = new bool[AffixTally];
    private readonly object _lifecycleSynchronize = new();
    private int _affixedTally;
    private int _unfastenAsked;
    private int _lifecycleHolderThreadIdent;
    private LifespanPhase _lifecyclePhase = LifespanPhase.Created;

    private SilkWindowCallbackWiring(
        IPaneHookCanvas surface,
        PaneHookMarks marks,
        DisplayFramePacingDriver pacing,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(pacing);
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));

        _pull = () => _stillness.Invoke(marks.Load);
        _refresh = val => _stillness.Invoke(marks.Update, val);
        _primaryRasterize = val => _stillness.Invoke(marks.Render, val);
        _pacingRasterize = val => _stillness.Invoke(pacing.OnFrameRendered, val);
        _closing = () => _stillness.Invoke(marks.Closing);
        _focusAltered = val => _stillness.Invoke(marks.FocusAltered, val);
        _moved = val => _stillness.Invoke(pacing.OnPaneMoved, val);
        _phaseAltered = val => _stillness.Invoke(pacing.OnPanePhaseAltered, val);
        _framebufferRescale = val => _stillness.Invoke(marks.FramebufferRescale, val);

        _unfasten = new AssetShutdownTransaction(
            new AssetShutdownJuncture("native window callbacks",
            [
                new("framebuffer resize", () => Drop(FramebufferRescaleOrdinal)),
                new("window state", () => Drop(PhaseAlteredOrdinal)),
                new("window move", () => Drop(RelocateOrdinal)),
                new("focus changed", () => Drop(FocusAlteredOrdinal)),
                new("closing", () => Drop(ClosingOrdinal)),
                new("pacing render", () => Drop(PacingRasterizeOrdinal)),
                new("main render", () => Drop(PrimaryRasterizeOrdinal)),
                new("update", () => Drop(RefreshOrdinal)),
                new("load", () => Drop(PullOrdinal)),
            ]));
    }

    public bool IsDetached
    {
        get
        {
            lock (_lifecycleSynchronize)
                return _affixed.All(static affixed => !affixed);
        }
    }

    public bool IsDisposalComplete
    {
        get
        {
            lock (_lifecycleSynchronize)
                return _lifecyclePhase == LifespanPhase.Detached;
        }
    }

    public static SilkWindowCallbackWiring Create(
        IWindow pane,
        PaneHookMarks marks,
        DisplayFramePacingDriver pacing,
        HostQuiescenceTurnstile stillness) =>
        Create(new SilkPaneHookCanvas(pane), marks, pacing, stillness);

    public void Attach()
    {
        int threadIdent = Environment.CurrentManagedThreadId;
        lock (_lifecycleSynchronize)
        {
            if (_lifecyclePhase != LifespanPhase.Created)
            {
                throw _lifecyclePhase == LifespanPhase.Detached
                    ? new ObjectDisposedException(nameof(SilkWindowCallbackWiring))
                    : new InvalidOperationException(
                        "Native window callback attachment has by now started");
            }
            if (Volatile.Read(ref _unfastenAsked) is not 0 || _unfasten.IsComplete)
                throw new ObjectDisposedException(nameof(SilkWindowCallbackWiring));

            _lifecyclePhase = LifespanPhase.Attaching;
            _lifecycleHolderThreadIdent = threadIdent;
        }

        try
        {
            Fasten(PullOrdinal);
            Fasten(RefreshOrdinal);
            Fasten(PrimaryRasterizeOrdinal);
            Fasten(PacingRasterizeOrdinal);
            Fasten(ClosingOrdinal);
            Fasten(FocusAlteredOrdinal);
            Fasten(RelocateOrdinal);
            Fasten(PhaseAlteredOrdinal);
            Fasten(FramebufferRescaleOrdinal);

            lock (_lifecycleSynchronize)
            {
                if (Volatile.Read(ref _unfastenAsked) is not 0)
                {
                    throw new ObjectDisposedException(
                        nameof(SilkWindowCallbackWiring),
                        "Shutdown was requested while native callbacks were attaching");
                }

                _lifecyclePhase = LifespanPhase.Attached;
                _lifecycleHolderThreadIdent = 0;
                System.Threading.Monitor.PulseAll(_lifecycleSynchronize);
            }
        }
        catch (Exception fastenProblem)
        {
            Interlocked.Exchange(ref _unfastenAsked, 1);
            _stillness.HaltAccepting();
            List<Exception>? undoProblems = null;
            for (int ordinal = _affixedTally - 1; ordinal >= 0; --ordinal)
            {
                try
                {
                    Drop(ordinal);
                }
                catch (Exception undoProblem)
                {
                    (undoProblems ??= []).Add(new InvalidOperationException(
                        $"Could not roll back native window callback index {ordinal}.",
                        undoProblem));
                }
            }

            lock (_lifecycleSynchronize)
            {
                _lifecyclePhase = LifespanPhase.AttachFailed;
                _lifecycleHolderThreadIdent = 0;
                System.Threading.Monitor.PulseAll(_lifecycleSynchronize);
            }

            if (undoProblems is not null)
            {
                undoProblems.Insert(0, new InvalidOperationException(
                    "Native window callback registration failed", fastenProblem));
                throw new AggregateException(
                    "Native window callback registration and rollback both failed",
                    undoProblems);
            }

            throw new InvalidOperationException(
                "Native window callback registration failed and was rolled back",
                fastenProblem);
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _unfastenAsked, 1);
        _stillness.HaltAccepting();
        int threadIdent = Environment.CurrentManagedThreadId;
        bool ownsLatch = _stillness.IsEnteredByLatestThread;
        while (true)
        {
            lock (_lifecycleSynchronize)
            {
                if (_lifecyclePhase == LifespanPhase.Detached)
                    return;
                if (_lifecyclePhase == LifespanPhase.Attaching)
                {
                    if (_lifecycleHolderThreadIdent == threadIdent || ownsLatch)
                        throw new NativePaneHookTidyPostponedFault();

                    System.Threading.Monitor.Wait(_lifecycleSynchronize);
                    continue;
                }
                if (_lifecyclePhase == LifespanPhase.Detaching)
                {
                    if (_lifecycleHolderThreadIdent == threadIdent)
                        return;
                    if (ownsLatch)
                        throw new NativePaneHookTidyPostponedFault();

                    System.Threading.Monitor.Wait(_lifecycleSynchronize);
                    continue;
                }

                _lifecyclePhase = LifespanPhase.Detaching;
                _lifecycleHolderThreadIdent = threadIdent;
                break;
            }
        }

        try
        {
            _unfasten.CompleteOrThrow();
            lock (_lifecycleSynchronize)
            {
                _lifecyclePhase = LifespanPhase.Detached;
                _lifecycleHolderThreadIdent = 0;
                System.Threading.Monitor.PulseAll(_lifecycleSynchronize);
            }
        }
        catch
        {
            lock (_lifecycleSynchronize)
            {
                _lifecyclePhase = LifespanPhase.DetachPending;
                _lifecycleHolderThreadIdent = 0;
                System.Threading.Monitor.PulseAll(_lifecycleSynchronize);
            }

            throw;
        }
    }

    internal static SilkWindowCallbackWiring Create(
        IPaneHookCanvas canvas,
        PaneHookMarks marks,
        DisplayFramePacingDriver pacing,
        HostQuiescenceTurnstile stillness) =>
        new(canvas, marks, pacing, stillness);

    private void Fasten(int index)
    {
        lock (_lifecycleSynchronize)
        {
            _affixed[index] = true;
            _affixedTally = Math.Max(_affixedTally, index + 1);
        }
        switch (index)
        {
            case PullOrdinal: _canvas.AppendPull(_pull); break;
            case RefreshOrdinal: _canvas.AppendRefresh(_refresh); break;
            case PrimaryRasterizeOrdinal: _canvas.AppendRasterize(_primaryRasterize); break;
            case PacingRasterizeOrdinal: _canvas.AppendRasterize(_pacingRasterize); break;
            case ClosingOrdinal: _canvas.AppendClosing(_closing); break;
            case FocusAlteredOrdinal: _canvas.AppendFocusAltered(_focusAltered); break;
            case RelocateOrdinal: _canvas.AppendRelocate(_moved); break;
            case PhaseAlteredOrdinal: _canvas.AppendPhaseAltered(_phaseAltered); break;
            case FramebufferRescaleOrdinal:
                _canvas.AppendFramebufferRescale(_framebufferRescale);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (Volatile.Read(ref _unfastenAsked) is not 0)
        {
            throw new ObjectDisposedException(
                nameof(SilkWindowCallbackWiring),
                "Shutdown was requested while native callbacks were attaching");
        }
    }

    private void Drop(int index)
    {
        lock (_lifecycleSynchronize)
        {
            if (index >= _affixedTally || !_affixed[index])
                return;
        }

        switch (index)
        {
            case PullOrdinal: _canvas.DropPull(_pull); break;
            case RefreshOrdinal: _canvas.DropRefresh(_refresh); break;
            case PrimaryRasterizeOrdinal: _canvas.DropRasterize(_primaryRasterize); break;
            case PacingRasterizeOrdinal: _canvas.DropRasterize(_pacingRasterize); break;
            case ClosingOrdinal: _canvas.DropClosing(_closing); break;
            case FocusAlteredOrdinal: _canvas.DropFocusAltered(_focusAltered); break;
            case RelocateOrdinal: _canvas.DropRelocate(_moved); break;
            case PhaseAlteredOrdinal: _canvas.DropPhaseAltered(_phaseAltered); break;
            case FramebufferRescaleOrdinal:
                _canvas.DropFramebufferRescale(_framebufferRescale);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }

        lock (_lifecycleSynchronize)
            _affixed[index] = false;
    }
}
