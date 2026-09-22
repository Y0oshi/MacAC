using MacAC.Client.Machine;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace MacAC.Client.Graphics.Gpu.Vulkan;

internal sealed unsafe class VkGraphicsScope : IDisposable
{
    private const ulong ObtainTimeoutNanoseconds = 1_000_000_000ul;

    private readonly IWindow _window;
    private readonly EngineKnobs _knobs;
    private readonly GraphicalHubPlatformServices _platform;
    private readonly FramePacingRule _pacing;
    private readonly Action<string> _trace;

    private Silk.NET.Vulkan.Vk? _vk;
    private Instance _inst;
    private KhrSurface? _canvasApi;
    private SurfaceKHR _canvas;
    private PhysicalDevice _physicalDev;
    private Device _device;
    private KhrSwapchain? _swapchainApi;
    private Queue _graphicsQueue;
    private Queue _presentFifo;
    private VkQueueFamilyChoice? _clans;
    private VkSwapchain? _swapchain;
    private ClientVulkanGpuDevice? _gpuDev;
    private VkDebugNames _diagLabels = VkDebugNames.Disabled;
    private VkDeviceFeatureSupport? _features;
    private VkDeviceLimitSupport? _thresholds;
    private VkFormatSupport? _formats;
    private IReadOnlyList<string> _instExtensions = [];
    private bool _recreateAtCycleBoundary;
    private bool _destroyed;

    private VkGraphicsScope(
        IWindow window,
        EngineKnobs options,
        GraphicalHubPlatformServices platform,
        FramePacingRule pacing,
        Action<string> log)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _knobs = options ?? throw new ArgumentNullException(nameof(options));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _pacing = pacing;
        _trace = log ?? throw new ArgumentNullException(nameof(log));
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        var vk = _vk;
        if (vk is not null && _device.Handle != 0)
            vk.DeviceWaitIdle(_device);

        _gpuDev?.Dispose();
        _gpuDev = null;
        _swapchain?.Dispose();
        _swapchain = null;

        if (vk is not null && _device.Handle != 0)
        {
            vk.DestroyDevice(_device, null);
            _device = default;
        }

        _diagLabels.Dispose();
        _diagLabels = VkDebugNames.Disabled;

        if (vk is not null && _canvasApi is not null && _canvas.Handle is not 0)
        {
            _canvasApi.DestroySurface(_inst, _canvas, null);
            _canvas = default;
        }

        _swapchainApi?.Dispose();
        _swapchainApi = null;
        _canvasApi?.Dispose();
        _canvasApi = null;

        if (vk is not null && _inst.Handle != 0)
        {
            vk.DestroyInstance(_inst, null);
            _inst = default;
        }

        vk?.Dispose();
        _vk = null;
    }

    internal VkCapabilityRecord? Capabilities { get; private set; }

    internal ClientVulkanGpuDevice Device
    {
        get
        {
            return _gpuDev ?? throw new InvalidOperationException(
            "The Vulkan RHI device hasn't been created");
        }
    }

    // Samples the backbuffer pass renders with. 1 when MSAA is off or unsupported.
    internal int SampleCount { get; private set; } = 1;

    internal uint Width => _swapchain?.Configuration?.Width ?? 0u;

    internal uint Height => _swapchain?.Configuration?.Height ?? 0u;

    internal static VkGraphicsScope Obtain(
        IWindow pane,
        EngineKnobs knobs,
        GraphicalHubPlatformServices platform,
        FramePacingRule pacing,
        int askedSpecimenTally,
        Action<string>? trace = null)
    {
        VkGraphicsScope ctx = new VkGraphicsScope(
            pane,
            knobs,
            platform,
            pacing,
            trace ?? Console.WriteLine);
        try
        {
            ctx.CreateInstanceAndSurface();
            ctx.SelectDeviceAndGate();
            ctx.CreateDevice(askedSpecimenTally);
            return ctx;
        }
        catch
        {
            ctx.Dispose();
            throw;
        }
    }

    internal static string ShaderSpirvFolder() =>
        Path.Combine(AppContext.BaseDirectory, "Graphics", "Shaders", "spv");

    internal bool ReadyCycle()
    {
        ObjectDisposedException.ThrowIf(_destroyed, this);
        if (!_recreateAtCycleBoundary && _swapchain!.IsBuilt)
            return true;

        if (!RecreateSwapchain())
            return false;

        var resized = _swapchain!.Configuration!;
        Device.ConfigureBackbufferAffixes(
            resized.Width,
            resized.Height,
            resized.ImageFormat,
            SampleCount);
        return true;
    }

    internal void NoteCycleClosed()
    {
        if (_gpuDev is not null && !_gpuDev.PresentSucceeded)
            _recreateAtCycleBoundary = true;
    }

    // Arms recreation, used when the acquire itself reported out-of-date
    internal void ReqRecreate() => _recreateAtCycleBoundary = true;

    private void CreateInstanceAndSurface()
    {
        _vk = GraphicalVkFetcher.BuildApi();
        if (_window.VkSurface is null)
        {
            throw new NotSupportedException(
                "The windowing backend didn't expose a Vulkan surface. " +
                "macac needs GLFW 3.4 built with Vulkan support");
        }

        byte** neededLabels = _window.VkSurface.GetRequiredExtensions(out uint neededTally);
        List<string> needed = new List<string>((int)neededTally);
        for (uint idx = 0; idx < neededTally; ++idx)
            needed.Add(VkInterop.ScanString(neededLabels[idx]));

        var inst = VkInstanceMint.Create(
            _vk,
            needed,
            activateOptionalExtensions: _knobs.DevTools);
        _inst = inst.Instance;
        _instExtensions = inst.EnabledExtensions;

        if (!_vk.TryGetInstanceExtension(_inst, out KhrSurface canvasApi))
        {
            throw new NotSupportedException(
                "VK_KHR_surface is needed but its entry points could not be loaded");
        }

        _canvasApi = canvasApi;
        _canvas = _window.VkSurface.Create<AllocationCallbacks>(
            _inst.ToHandle(),
            null).ToSurface();
    }

    private void SelectDeviceAndGate()
    {
        var vk = _vk!;
        var contenders =
            VkPhysicalDeviceInspector.Iterate(vk, _inst, out PhysicalDevice[] hnds);
        VkPhysicalDeviceChoice? choice = VkPhysicalDevicePicking.Choose(
            contenders,
            _knobs.VulkanDeviceOverride) ?? throw new NotSupportedException(
                "No Vulkan physical device was enumerated. Install or update a " +
                "Vulkan 1.3 driver for this GPU");
        _physicalDev = hnds[choice.Device.Index];

        _features = VkPhysicalDeviceInspector.ScanFeatures(vk, _physicalDev);

        var fifoClans =
            VkPhysicalDeviceInspector.ScanFifoClans(
                vk,
                _physicalDev,
                _canvasApi,
                _canvas);
        VkQueueFamilyChoice? clans = VkQueueFamilyPicking.Choose(fifoClans) ?? throw new NotSupportedException(
                $"'{choice.Device.DeviceName}' exposes no queue family that can both " +
                "render and present to the window surface");
        _clans = clans;
        var built = VkLogicalDeviceMint.Create(
            vk,
            _physicalDev,
            clans,
            demandSwapchain: true,
            onHandFeatures: _features);
        _device = built.Device;
        _graphicsQueue = built.GraphicsQueue;
        _presentFifo = built.PresentQueue;

        if (!vk.TryGetDeviceExtension(_inst, _device, out KhrSwapchain swapchainApi))
        {
            throw new NotSupportedException(
                "VK_KHR_swapchain is needed but its entry points could not be loaded");
        }

        _swapchainApi = swapchainApi;
        _swapchain = new VkSwapchain(
            vk,
            _canvasApi!,
            swapchainApi,
            _physicalDev,
            _device,
            _canvas,
            clans);

        (SurfaceCapabilitiesKHR canvasCapabilities,
            IReadOnlyList<SurfaceFormatKHR> formats,
            IReadOnlyList<PresentModeKHR> presentManners) = _swapchain.AskCanvas();

        var framebuffer = _window.FramebufferSize;
        var planned = VkSwapchainSetupMint.Create(
            canvasCapabilities,
            formats,
            presentManners,
            _pacing,
            (uint)Math.Max(0, framebuffer.X),
            (uint)Math.Max(0, framebuffer.Y));

        VkSurfaceSupport canvasSupport = new VkSurfaceSupport(
            PresentSupported: true,
            SelectedFormat: planned.ImageFormat,
            SelectedColorSpace: planned.ColorSpace,
            SelectedPresentMode: planned.PresentMode,
            SelectedImageCount: planned.ImageCount,
            SelectedWidth: planned.Width,
            SelectedHeight: planned.Height,
            SupportsTransferSource:
                VkSwapchainSetupMint.SupportsTransferSrc(canvasCapabilities),
            AvailableFormats: [.. formats.Select(fmt => fmt.Format).Distinct()],
            AvailablePresentModes: [.. presentManners]);

        var sensor = VkActiveDeviceProbe.Run(
            vk,
            _physicalDev,
            _device,
            _graphicsQueue,
            clans.GraphicsFamily);

        _thresholds = VkPhysicalDeviceInspector.ScanThresholds(vk, _physicalDev);
        _formats = VkPhysicalDeviceInspector.ScanFormats(
            vk,
            _physicalDev,
            VkSwapchainSetupMint.OffersUnormFmt(formats));

        var capture = new VkCapabilityRecord(
            DateTimeOffset.UtcNow,
            _platform.RuntimeIdentifier,
            _platform.OperatingSystem,
            _platform.WindowBackend.RequestedProtocol,
            GlfwNativePlatformSensor.FetchEngagedProtocol(_platform.OperatingSystem),
            VulkanApiVer.Depict(
                VulkanApiVer.Make(
                    VkCapabilityRequirements.NeededApiMajor,
                    VkCapabilityRequirements.NeededApiMinor,
                    0)),
            VulkanApiVer.Depict(choice.Device.ApiVersion),
            choice.Device.ApiVersion,
            choice.Device.DeviceName,
            VkPhysicalDeviceInspector.DepictDriver(choice.Device),
            choice.Device.DeviceType,
            choice.Device.Index,
            choice.Reason,
            _knobs.VulkanDeviceOverride,
            ForcedUnsupportedFeature: null,
            contenders,
            _instExtensions,
            built.EnabledExtensions,
            clans.GraphicsFamily,
            clans.PresentFamily,
            _features,
            _thresholds,
            _formats,
            canvasSupport,
            sensor,
            SupportFailures: []);

        capture = VkCapabilityRequirements.Reevaluate(capture);
        capture = VkCapabilityRequirements.ImposeForcedUnsupported(
            capture,
            _knobs.VulkanForcedUnsupportedFeature);
        Capabilities = capture;

        string dossierTrail = Path.Combine(
            _platform.Paths.Diagnostics,
            VkCapabilityWarden.DossierFileLabel);
        VkCapabilityDigestWriter.Write(dossierTrail, capture);
        VkCapabilityWarden.HurlIfUnsupported(capture, dossierTrail);

        _trace(
            "vulkan: capability gate passed " +
            $"({capture.ActiveDisplayProtocol}, {capture.DeviceName}, " +
            $"{capture.DeviceApiVersion}, {capture.DriverInfo}); " +
            $"swapchain {planned.ImageFormat}/{planned.PresentMode} " +
            $"{planned.Width}x{planned.Height} x{planned.ImageCount}; " +
            $"report={dossierTrail}");
        _trace($"vulkan: device selection — {choice.Reason}");
    }

    private void CreateDevice(int askedSpecimenTally)
    {
        var vk = _vk!;
        _diagLabels = VkDebugNames.Create(vk, _inst, _device, [.. _instExtensions]);

        if (!RecreateSwapchain())
        {
            throw new InvalidOperationException(
                "The swapchain could not be created for the initial framebuffer size");
        }

        var configuration = _swapchain!.Configuration!;
        _gpuDev = new ClientVulkanGpuDevice(
            vk,
            _physicalDev,
            _device,
            _graphicsQueue,
            _presentFifo,
            _clans!.GraphicsFamily,
            _features!,
            _thresholds!,
            _formats!,
            Capabilities!.DeviceName,
            Capabilities.DriverInfo,
            Capabilities.DeviceApiVersion,
            _diagLabels,
            new ClientSwapchainBackbuffer(_swapchain!, _presentFifo),
            ShaderSpirvFolder(),
            _platform.Paths.Cache,
            retainBackbufferGrab:
                !string.IsNullOrWhiteSpace(_knobs.AutomationArtifactDirectory));

        SampleCount = (int)Math.Min(
            (uint)Math.Max(1, askedSpecimenTally),
            Math.Max(1u, _gpuDev.Capabilities.UpperSpecimenTally));
        _gpuDev.ConfigureBackbufferAffixes(
            configuration.Width,
            configuration.Height,
            configuration.ImageFormat,
            SampleCount);

        _trace(
            $"vulkan: RHI backend up — {_gpuDev.Allocator.Depict()}, " +
            $"{SampleCount}x MSAA, pipeline cache " +
            (_gpuDev.PipeStashFetchedFromDisk ? "reused" : "cold") +
            $", debug names {(_diagLabels.IsTurnedOn ? "on" : "off")}");
    }

    // Adapts the swapchain to the narrow surface the RHI device needs
    private sealed class ClientSwapchainBackbuffer(VkSwapchain swapchain, Queue presentFifo)
        : IVkBackbuffer
    {
        public Format ImageFmt => swapchain.Configuration!.ImageFormat;

        public uint Width => swapchain.Configuration!.Width;

        public uint Height => swapchain.Configuration!.Height;

        public bool TryAcquire(Semaphore acquired, out uint imageOrdinal)
        {
            var act = swapchain.TryAcquire(
                acquired,
                ObtainTimeoutNanoseconds,
                out imageOrdinal);
            return act is VkSwapchainAction.Continue
                or VkSwapchainAction.RecreateAtFrameBoundary;
        }

        public Image ImageAt(uint imageOrdinal) => swapchain.ImageAt(imageOrdinal);

        public ImageView LensAt(uint imageOrdinal) => swapchain.LensAt(imageOrdinal);

        public Semaphore PaintDoneAt(uint imageOrdinal) =>
            swapchain.PaintDoneAt(imageOrdinal);

        public bool Present(uint imageOrdinal)
        {
            return swapchain.Present(presentFifo, imageOrdinal) is VkSwapchainAction.Continue;
        }
    }

    private bool RecreateSwapchain()
    {
        var framebuffer = _window.FramebufferSize;
        uint width = (uint)Math.Max(0, framebuffer.X);
        uint height = (uint)Math.Max(0, framebuffer.Y);
        if (VkSwapchainRecreationRule.OnFramebufferDims(width, height)
            == VkSwapchainAction.Idle)
        {
            // No log here: while minimised this runs at frame rate
            return false;
        }

        VkInterop.Check(_vk!.DeviceWaitIdle(_device), "vkDeviceWaitIdle (recreate)");
        _recreateAtCycleBoundary = false;
        bool recreated = _swapchain!.Recreate(_pacing, width, height);
        _trace($"vulkan: swapchain recreated {width}x{height} ok={recreated}");
        return recreated;
    }
}
