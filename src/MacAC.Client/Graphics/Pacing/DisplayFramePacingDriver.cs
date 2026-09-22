using MacAC.Client.Telemetry;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics;

internal interface IReadoutCyclePacingCanvas
{
    bool VSynchronize { get; set; }

    bool TryFetchEngagedObserveRenewHz(out int renewHz);
}

// Narrow Silk window adapter for display pacing
internal sealed class SilkReadoutCyclePacingCanvas(IWindow window) : IReadoutCyclePacingCanvas
{
    private readonly IWindow _window = window ?? throw new ArgumentNullException(nameof(window));

    public bool VSynchronize
    {
        get => _window.VSync;
        set => _window.VSync = value;
    }

    public bool TryFetchEngagedObserveRenewHz(out int renewHz)
    {
        renewHz = 0;
        try
        {
            int? engagedRenewHz = _window.Monitor?.VideoMode.RefreshRate;
            if (engagedRenewHz is not > 0)
                return false;

            renewHz = engagedRenewHz.Value;
            return true;
        }
        catch (Silk.NET.GLFW.GlfwException)
        {
            return false;
        }
    }
}

internal sealed class DisplayFramePacingDriver : IDisposable
{
    private readonly bool _uncappedRendering;
    private CycleProfiler? _profiler;
    private readonly FramePacingDriver _pacing;
    private IReadoutCyclePacingCanvas? _canvas;
    private int? _engagedObserveRenewHz;

    public DisplayFramePacingDriver(
        bool uncappedRendering,
        CycleProfiler profiler)
        : this(uncappedRendering, profiler, new FramePacingDriver())
    {
    }

    internal DisplayFramePacingDriver(
        bool uncappedRendering,
        CycleProfiler profiler,
        IFramePacingWaiterMint waiterMaker)
        : this(
            uncappedRendering,
            profiler,
            new FramePacingDriver(waiterMaker))
    {
    }

    internal DisplayFramePacingDriver(
        bool uncappedRendering,
        CycleProfiler profiler,
        FramePacingDriver pacing)
    {
        _uncappedRendering = uncappedRendering;
        _profiler = profiler ?? throw new ArgumentNullException(nameof(profiler));
        _pacing = pacing ?? throw new ArgumentNullException(nameof(pacing));
    }

    internal bool AskedVSynchronize { get; private set; } = MacAC.Cockpit.Panels.Settings.ReadoutPrefs.Default.VSync;

    internal FramePacingRule Policy => _pacing.Policy;

    internal bool IsCanvasTied => _canvas is not null;

    public FramePacingRule BootstrapStartup(bool askedVSynchronize)
    {
        AskedVSynchronize = askedVSynchronize;
        var rule = Resolve(observeRenewHz: null);
        _pacing.Apply(rule);
        return rule;
    }

    public void AttachCanvas(IReadoutCyclePacingCanvas surface) => _canvas = surface ?? throw new ArgumentNullException(nameof(surface));

    public void ImposePreference(bool askedVSynchronize)
    {
        AskedVSynchronize = askedVSynchronize;
        ImposeSettledRule();
    }

    public void RenewEngagedObserve()
    {
        _engagedObserveRenewHz =
            _canvas?.TryFetchEngagedObserveRenewHz(out int renewHz) == true
                ? renewHz
                : null;
        ImposeSettledRule();
    }

    public void OnPaneMoved(Vector2D<int> _) => RenewEngagedObserve();

    public void OnPanePhaseAltered(WindowState _) => RenewEngagedObserve();

    public void OnFrameRendered(double _)
    {
        CycleProfiler profiler = _profiler
            ?? throw new ObjectDisposedException(nameof(DisplayFramePacingDriver));
        using JunctureAmbit pacingJuncture = profiler.BeginStage(CycleJuncture.Pacing);
        _pacing.FinishCycle();
    }

    public void Dispose()
    {
        try
        {
            _pacing.Dispose();
        }
        finally
        {
            _canvas = null;
            _profiler = null;
        }
    }

    private FramePacingRule Resolve(int? observeRenewHz)
    {
        return FramePacingRule.Resolve(
            AskedVSynchronize,
            _uncappedRendering,
            observeRenewHz);
    }

    private void ImposeSettledRule()
    {
        var rule = Resolve(_engagedObserveRenewHz);
        _pacing.Apply(rule);
        if (_canvas is not null && _canvas.VSynchronize != rule.UseVSync)
            _canvas.VSynchronize = rule.UseVSync;
    }
}
