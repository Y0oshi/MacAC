using MacAC.Client.Machine;
using MacAC.Client.Telemetry;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed class VkBringUpHost : IDisposable
{
    internal const int FlightTally = 2;

    internal static readonly float[] WipeTint = [0.043f, 0.075f, 0.153f, 1f];

    private const string ScreenshotLabel = "vulkan-bringup";

    private readonly EngineKnobs _knobs;
    private readonly GraphicalHubPlatformServices _platform;
    private readonly FramePacingRule _pacing;
    private readonly Action<string> _trace;

    private IWindow? _window;
    private VkGraphicsScope? _visuals;
    private VkRhiStage? _tableau;
    private VkRetainedWidgetStage? _widget;
    private ulong _cycleSerialNo;
    private bool _destroyed;

    internal VkBringUpHost(
        EngineKnobs options,
        GraphicalHubPlatformServices platform,
        bool askedVSynchronize,
        Action<string>? trace = null)
    {
        _knobs = options ?? throw new ArgumentNullException(nameof(options));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _trace = trace ?? Console.WriteLine;
        _pacing = FramePacingRule.Resolve(
            askedVSynchronize,
            _knobs.UncappedRendering,
            observeRenewHz: null);
    }

    internal VkCapabilityRecord? Capabilities => _visuals?.Capabilities;

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _widget?.Dispose();
        _widget = null;
        _tableau?.Dispose();
        _tableau = null;
        _visuals?.Dispose();
        _visuals = null;
        _window?.Dispose();
        _window = null;
    }

    internal void Run()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);

        BuildPane();
        _visuals = VkGraphicsScope.Obtain(
            _window!,
            _knobs,
            _platform,
            _pacing,
            // Four samples where the device allows it, so the backbuffer pass really resolves rather than
            // rendering straight into the swapchain image.
            askedSpecimenTally: 4,
            _trace);
        BuildScenes();
        Present();
    }

    internal static bool ShouldRetire(
        int cycleAllowance,
        ulong presentedCycles,
        bool capturesScreenshot,
        bool screenshotAsked)
    {
        if (cycleAllowance <= 0)
            return false;
        return presentedCycles < (ulong)cycleAllowance ? false : !capturesScreenshot || screenshotAsked;
    }

    private void BuildPane()
    {
        WindowOptions knobs = WindowOptions.DefaultVulkan with
        {
            Size = new Vector2D<int>(1280, 720),
            Title = "macac — Vulkan capability probe",
            VSync = _pacing.UseVSync,
        };
        _window = Window.Create(knobs);
        _window.Initialize();
    }

    private void BuildScenes()
    {
        var visuals = _visuals!;
        _tableau = new VkRhiStage(visuals.Device, visuals.SampleCount);
        _widget = new VkRetainedWidgetStage(
            visuals.Device,
            VkGraphicsScope.ShaderSpirvFolder());
        _trace(
            "vulkan: retained UI up — PhrasePainter and DiagStrokePainter on the Vulkan device" +
            (_widget.HasTypeface ? string.Empty : " (no system font found; glyph draws are skipped)"));
    }

    private void Present()
    {
        IWindow pane = _window!;
        var visuals = _visuals!;
        var dev = visuals.Device;
        VkRhiStage tableau = _tableau!;
        var screenshots = BuildScreenshotDriver();
        bool screenshotAsked = false;
        var begun = DateTimeOffset.UtcNow;

        while (!pane.IsClosing)
        {
            pane.DoEvents();
            if (pane.IsClosing)
                break;

            if (!visuals.ReadyCycle())
            {
                Thread.Sleep(16);
                continue;
            }

            if (!dev.TryBeginFrame(out IGpuCycle? cycle) || cycle is null)
            {
                visuals.ReqRecreate();
                continue;
            }

            double passed = (DateTimeOffset.UtcNow - begun).TotalSeconds;
            using (cycle)
            {
                tableau.Render(cycle, visuals.Width, visuals.Height, passed);
                _widget?.Render(cycle, visuals.Width, visuals.Height, passed);
            }

            _cycleSerialNo = (ulong)cycle.SerialNo;
            visuals.NoteCycleClosed();

            if (screenshots is not null && !screenshotAsked && _cycleSerialNo >= 4)
            {
                screenshotAsked = true;
                if (screenshots.TryReq(ScreenshotLabel, out string problem))
                {
                    screenshots.SnapQueued((int)visuals.Width, (int)visuals.Height);
                    AnnounceTimings(dev);
                }
                else
                {
                    _trace($"vulkan: screenshot request rejected: {problem}");
                }
            }

            if (ShouldRetire(screenshots, screenshotAsked))
            {
                _trace(
                    $"vulkan: frame budget of {_knobs.VulkanCapabilityProbeFrames} " +
                    "reached; closing the probe.");
                break;
            }
        }

        dev.PauseIdle();
        _trace($"vulkan: presented {_cycleSerialNo} RHI frame(s); shutting down.");
    }

    private bool ShouldRetire(
        FrameScreenshotDriver? screenshots,
        bool screenshotAsked)
    {
        return ShouldRetire(
            _knobs.VulkanCapabilityProbeFrames,
            _cycleSerialNo,
            screenshots is not null,
            screenshotAsked);
    }

    private void AnnounceTimings(ClientVulkanGpuDevice dev)
    {
        if (!dev.Tickers.IsSupported)
        {
            _trace("vulkan: GPU timestamps are unsupported on this device");
            return;
        }

        string offscreen = dev.Tickers.TryLocate("offscreen", out double offscreenMsec)
            ? $"{offscreenMsec:F3} ms"
            : "pending";
        string primary = dev.Tickers.TryLocate("main", out double primaryMsec)
            ? $"{primaryMsec:F3} ms"
            : "pending";
        _trace($"vulkan: GPU timer scopes — offscreen {offscreen}, main {primary}");
    }

    private FrameScreenshotDriver? BuildScreenshotDriver()
    {
        return string.IsNullOrWhiteSpace(_knobs.AutomationArtifactDirectory)
            ? null
            : new FrameScreenshotDriver(
            (width, height) => FrameScreenshotDriver.FlipRanks(
                _visuals!.Device.CaptureBackbuffer(width, height),
                width,
                height),
            _knobs.AutomationArtifactDirectory,
            _trace);
    }
}
