using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Gpu;
using MacAC.Cockpit.Input;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace MacAC.Client.Rigging;

internal interface IPlayPaneHubFeedCameraBulletin
{
    void PublishGpuFrameFlights(GpuCycleFlightDriver? val);
    void PublishGpuDevice(IClientGpuDevice val);
    void PublishGpuFrameLifetime(GpuDeviceCycleLifespan val);
    void PublishKeyboardSource(SilkKeyboardOrigin val);
    void PublishMouseSource(SilkPointerOrigin val);
    void PublishMouseLookCursor(IPointerGazeCursor val);
    void PublishInputDispatcher(InputRouter val);
    void PublishCameraController(CameraDriver val);
    void PublishCameraPointerInput(CameraPointerInputDriver val);
}

internal sealed record HubFeedCameraOutcome(
    GpuCycleFlightDriver? GpuFrameFlights,
    IGpuAssetSunsetFifo Retirement,
    IRasterizeCycleSocketOrigin FrameSlots,
    IClientGpuDevice GpuDevice,
    GpuDeviceCycleLifespan GpuFrameLifetime,
    SilkKeyboardOrigin? KeyboardSource,
    SilkPointerOrigin? MouseSource,
    IPointerGazeCursor? MouseLookCursor,
    InputRouter? InputRouter,
    CameraDriver CameraController,
    CameraPointerInputDriver? CameraPointerInput);

internal sealed record HubFeedCameraDeps(
    FramebufferResizeDriver FramebufferResize,
    Vector2D<int> InitialFramebufferSize,
    HostQuiescenceTurnstile HostQuiescence,
    IFeedGrabOrigin InputCapture,
    KeyBindingBook KeyBindingBook,
    RouterLocomotionInputSource LocomotionInput,
    RouterCameraInputSource CameraInput,
    AvatarModeLedger LocalPlayerMode,
    FollowCameraInputLedger ChaseCameraInput,
    PointerPositionLedger PointerPosition,
    IRenderFrameTelemetryLog RenderDiagnosticLog,
    float? InitialOrbitDistanceMeters = null,
    float? InitialOrbitYawDegrees = null,
    float? InitialOrbitPitchDegrees = null);

internal interface IHostInputCameraAssemblyMint
{
    IFramebufferViewRectMark BuildViewRectMark(PlayPaneVisuals visuals);

    // The GL fence ring, or null when the backend's RHI device owns its flights
    GpuCycleFlightDriver? BuildGpuCycleFlights(PlayPaneVisuals visuals);

    IClientGpuDevice BuildGpuDev(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights);

    // Where per-frame resource release is queued
    IGpuAssetSunsetFifo BuildSunset(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights,
        IClientGpuDevice dev);

    // The ring slot renderers index their per-flight buffers by
    IRasterizeCycleSocketOrigin BuildCycleSockets(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights,
        IClientGpuDevice dev);

    SilkKeyboardOrigin BuildKeyboardSrc(
        IKeyboard keyboard,
        HostQuiescenceTurnstile stillness);
    SilkPointerOrigin BuildPointerSrc(
        IMouse pointer,
        IFeedGrabOrigin grab,
        IKeyFeed? keyboard,
        HostQuiescenceTurnstile stillness);
    IPointerGazeCursor BuildPointerGazeCur(IMouse pointer);
    InputRouter BuildFeedRouter(
        IKeyFeed keyboard,
        IMouseFeed pointer,
        KeyBindingBook mappings);
    CameraDriver BuildCamDriver(
        float? startingOrbitGapMeters,
        float? startingOrbitYawDeg,
        float? startingOrbitPitchDeg);
    IFramebufferCameraMark BuildCamMark(CameraDriver cam);
    CameraPointerInputDriver BuildCamPtrFeed(
        IReadOnlyList<IMouse> mice,
        HostQuiescenceTurnstile stillness,
        IFeedGrabOrigin grab,
        AvatarModeLedger avatarManner,
        CameraDriver cam,
        FollowCameraInputLedger pursue,
        IMouseFeed mouse,
        PointerPositionLedger ptr);
}

internal enum HostInputCameraAssemblyPoint
{
    ViewportBound,
    GpuFrameFlightsPublished,
    GpuDevicePublished,
    KeyboardPublished,
    KeyboardAttached,
    MousePublished,
    MouseAttached,
    MouseLookCursorPublished,
    DispatcherPublished,
    DispatcherAttached,
    MovementInputBound,
    CameraInputBound,
    CameraPublished,
    CameraTargetBound,
    InitialFramebufferApplied,
    CameraPointerPublished,
    CameraPointerAttached,
}

internal sealed class HostInputCameraAssemblyPhase(
    HubFeedCameraDeps dependencies,
    IPlayPaneHubFeedCameraBulletin publication,
    IHostInputCameraAssemblyMint? maker = null,
    Action<HostInputCameraAssemblyPoint>? flawInjection = null) :
    IHostInputCameraAssemblyPhase<
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext>,
        HubFeedCameraOutcome>
{
    private readonly HubFeedCameraDeps _deps = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));
    private readonly IPlayPaneHubFeedCameraBulletin _bulletin = publication
            ?? throw new ArgumentNullException(nameof(publication));
    private readonly IHostInputCameraAssemblyMint? _injectedMaker = maker;
    private readonly Action<HostInputCameraAssemblyPoint>? _flawInjection = flawInjection;
    private IHostInputCameraAssemblyMint _maker =
        new VkHostInputCameraAssemblyMint();

    public HubFeedCameraOutcome Compose(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        AssemblyAcquisitionScope ambit = new AssemblyAcquisitionScope();
        try
        {
            var outcome = ComposeCore(platform, ambit);
            ambit.Complete();
            return outcome;
        }
        catch (Exception miss)
        {
            ambit.RevertAndThrow(miss);
            throw new System.Diagnostics.UnreachableException();
        }
    }

    private static IHostInputCameraAssemblyMint DefaultMakerFor(
        PlayPaneVisuals visuals) =>
        new VkHostInputCameraAssemblyMint();

    private HubFeedCameraOutcome ComposeCore(
        PlayPanePlatformOutcome<PlayPaneVisuals, IInputContext> platform,
        AssemblyAcquisitionScope ambit)
    {
        var visuals = platform.Graphics;
        var feed = platform.Input;
        _maker = _injectedMaker ?? DefaultMakerFor(visuals);

        _deps.FramebufferResize.AttachViewRect(
            _maker.BuildViewRectMark(visuals));
        Flaw(HostInputCameraAssemblyPoint.ViewportBound);

        var gpuCycles = ambit.ObtainOptional(
            "GPU frame flights",
            () => _maker.BuildGpuCycleFlights(visuals),
            static val => val.Dispose()).Publish(
                _bulletin.PublishGpuFrameFlights);
        Flaw(HostInputCameraAssemblyPoint.GpuFrameFlightsPublished);

        IClientGpuDevice gpuDev = ambit.Acquire(
            "GPU device (RHI)",
            () => _maker.BuildGpuDev(visuals, gpuCycles),
            static val => val.Dispose()).Publish(
                _bulletin.PublishGpuDevice);
        Flaw(HostInputCameraAssemblyPoint.GpuDevicePublished);
        var sunset =
            _maker.BuildSunset(visuals, gpuCycles, gpuDev);
        var cycleSockets =
            _maker.BuildCycleSockets(visuals, gpuCycles, gpuDev);

        GpuDeviceCycleLifespan gpuCycleLifespan = new GpuDeviceCycleLifespan(gpuDev);
        _bulletin.PublishGpuFrameLifetime(gpuCycleLifespan);

        IKeyboard? leadKeyboard = feed.Keyboards.FirstOrDefault();
        IMouse? leadPointer = feed.Mice.FirstOrDefault();
        SilkKeyboardOrigin? keyboard = null;
        SilkPointerOrigin? mouse = null;
        IPointerGazeCursor? cur = null;
        InputRouter? router = null;
        CameraPointerInputDriver? ptr = null;

        if (leadKeyboard is not null)
        {
            keyboard = ambit.Acquire(
                "keyboard source",
                () => _maker.BuildKeyboardSrc(
                    leadKeyboard,
                    _deps.HostQuiescence),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishKeyboardSource);
            Flaw(HostInputCameraAssemblyPoint.KeyboardPublished);
            keyboard.Attach();
            Flaw(HostInputCameraAssemblyPoint.KeyboardAttached);
        }

        if (leadPointer is not null)
        {
            mouse = ambit.Acquire(
                "mouse source",
                () => _maker.BuildPointerSrc(
                    leadPointer,
                    _deps.InputCapture,
                    keyboard,
                    _deps.HostQuiescence),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishMouseSource);
            Flaw(HostInputCameraAssemblyPoint.MousePublished);
            mouse.Attach();
            Flaw(HostInputCameraAssemblyPoint.MouseAttached);

            cur = _maker.BuildPointerGazeCur(leadPointer);
            _bulletin.PublishMouseLookCursor(cur);
            Flaw(HostInputCameraAssemblyPoint.MouseLookCursorPublished);
        }

        if (keyboard is not null && mouse is not null)
        {
            router = ambit.Acquire(
                "input dispatcher",
                () => _maker.BuildFeedRouter(
                    keyboard,
                    mouse,
                    _deps.KeyBindingBook),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishInputDispatcher);
            Flaw(HostInputCameraAssemblyPoint.DispatcherPublished);
            router.Attach();
            Flaw(HostInputCameraAssemblyPoint.DispatcherAttached);

            _deps.LocomotionInput.Bind(router);
            Flaw(HostInputCameraAssemblyPoint.MovementInputBound);
            _deps.CameraInput.Bind(router);
            Flaw(HostInputCameraAssemblyPoint.CameraInputBound);
        }

        var cam = _maker.BuildCamDriver(
            _deps.InitialOrbitDistanceMeters,
            _deps.InitialOrbitYawDegrees,
            _deps.InitialOrbitPitchDegrees);
        _bulletin.PublishCameraController(cam);
        Flaw(HostInputCameraAssemblyPoint.CameraPublished);
        _deps.FramebufferResize.AttachCam(
            _maker.BuildCamMark(cam));
        Flaw(HostInputCameraAssemblyPoint.CameraTargetBound);
        _deps.FramebufferResize.Resize(
            _deps.InitialFramebufferSize);
        Flaw(HostInputCameraAssemblyPoint.InitialFramebufferApplied);

        if (mouse is not null && leadPointer is not null)
        {
            ptr = ambit.Acquire(
                "camera pointer input",
                () => _maker.BuildCamPtrFeed(
                    feed.Mice,
                    _deps.HostQuiescence,
                    _deps.InputCapture,
                    _deps.LocalPlayerMode,
                    cam,
                    _deps.ChaseCameraInput,
                    mouse,
                    _deps.PointerPosition),
                static val => val.Dispose()).Publish(
                    _bulletin.PublishCameraPointerInput);
            Flaw(HostInputCameraAssemblyPoint.CameraPointerPublished);
            ptr.FastenRaw();
            Flaw(HostInputCameraAssemblyPoint.CameraPointerAttached);
        }

        return new HubFeedCameraOutcome(
            gpuCycles,
            sunset,
            cycleSockets,
            gpuDev,
            gpuCycleLifespan,
            keyboard,
            mouse,
            cur,
            router,
            cam,
            ptr);
    }

    private void Flaw(HostInputCameraAssemblyPoint pt) =>
        _flawInjection?.Invoke(pt);
}
