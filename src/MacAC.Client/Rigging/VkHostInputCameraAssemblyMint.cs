using MacAC.Client.Controls;
using MacAC.Client.Graphics;
using MacAC.Client.Graphics.Gpu;
using MacAC.Client.Graphics.Gpu.Vulkan;
using MacAC.Cockpit.Input;
using Silk.NET.Input;

namespace MacAC.Client.Rigging;

internal sealed class VkHostInputCameraAssemblyMint
    : IHostInputCameraAssemblyMint
{
    public IFramebufferViewRectMark BuildViewRectMark(
        PlayPaneVisuals visuals) =>
        new SwapchainRecreateViewRectMark(DemandCtx(visuals).ReqRecreate);

    public GpuCycleFlightDriver? BuildGpuCycleFlights(
        PlayPaneVisuals visuals) => null;

    public IClientGpuDevice BuildGpuDev(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights) =>
        DemandCtx(visuals).Device;

    public IGpuAssetSunsetFifo BuildSunset(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights,
        IClientGpuDevice dev) => dev.Retirement;

    public IRasterizeCycleSocketOrigin BuildCycleSockets(
        PlayPaneVisuals visuals,
        GpuCycleFlightDriver? cycleFlights,
        IClientGpuDevice dev) =>
        new VkRenderFrameSlotSource(DemandCtx(visuals).Device);

    public SilkKeyboardOrigin BuildKeyboardSrc(
        IKeyboard keyboard,
        HostQuiescenceTurnstile stillness) =>
        SilkKeyboardOrigin.BuildDetached(keyboard, stillness);

    public SilkPointerOrigin BuildPointerSrc(
        IMouse pointer,
        IFeedGrabOrigin grab,
        IKeyFeed? keyboard,
        HostQuiescenceTurnstile stillness) =>
        SilkPointerOrigin.BuildDetached(pointer, grab, keyboard, stillness);

    public IPointerGazeCursor BuildPointerGazeCur(IMouse pointer) =>
        new SilkPointerGazeCursor(pointer);

    public InputRouter BuildFeedRouter(
        IKeyFeed keyboard,
        IMouseFeed pointer,
        KeyBindingBook mappings) =>
        InputRouter.MakeDetached(keyboard, pointer, mappings);

    public CameraDriver BuildCamDriver(
        float? startingOrbitGapMeters,
        float? startingOrbitYawDeg,
        float? startingOrbitPitchDeg)
    {
        ClientOrbitCamera orbit = new ClientOrbitCamera();
        if (startingOrbitGapMeters is { } gap)
            orbit.Distance = gap;
        if (startingOrbitYawDeg is { } yaw)
            orbit.Yaw = DegToRadians(yaw);
        if (startingOrbitPitchDeg is { } pitch)
            orbit.Pitch = DegToRadians(pitch);
        return new CameraDriver(orbit, new ClientFlyCamera());
    }

    public IFramebufferCameraMark BuildCamMark(CameraDriver cam) =>
        new CameraFramebufferMark(cam);

    public CameraPointerInputDriver BuildCamPtrFeed(
        IReadOnlyList<IMouse> mice,
        HostQuiescenceTurnstile stillness,
        IFeedGrabOrigin grab,
        AvatarModeLedger avatarManner,
        CameraDriver cam,
        FollowCameraInputLedger pursue,
        IMouseFeed mouse,
        PointerPositionLedger ptr)
    {
        return CameraPointerInputDriver.Create(
            mice,
            stillness,
            grab,
            avatarManner,
            cam,
            pursue,
            mouse,
            ptr,
            new SurroundingsFeedMonotonicTimer());
    }

    private static float DegToRadians(float deg) =>
        deg * (MathF.PI / 180f);

    private static VkGraphicsScope DemandCtx(
        PlayPaneVisuals visuals)
    {
        return visuals.Vulkan
        ?? throw new InvalidOperationException(
            "The Vulkan host factory was composed against a backend with no " +
            "Vulkan context");
    }

    internal sealed class SwapchainRecreateViewRectMark(Action requestRecreate)
        : IFramebufferViewRectMark
    {
        private readonly Action _reqRecreate = requestRecreate
            ?? throw new ArgumentNullException(nameof(requestRecreate));

        public void RescaleViewRect(int width, int height) => _reqRecreate();
    }

    private sealed class VkRenderFrameSlotSource(ClientVulkanGpuDevice dev)
        : IRasterizeCycleSocketOrigin
    {
        public int LatestSocket => dev.Flights.LatestSocket;
    }
}
