using MacAC.Client.Controls;
using Silk.NET.Maths;

namespace MacAC.Client.Graphics;

internal interface IFramebufferViewRectMark
{
    void RescaleViewRect(int width, int height);
}

internal interface IFramebufferCameraMark
{
    void SetAspect(float aspect);
}

internal sealed class CameraFramebufferMark(CameraDriver camera)
    : IFramebufferCameraMark
{
    private readonly CameraDriver _cam = camera
        ?? throw new ArgumentNullException(nameof(camera));

    public void SetAspect(float aspect) => _cam.SetAspect(aspect);
}

internal interface IFramebufferDeviceToolsMark
{
    void ResetLayout(int width, int height);
}

// Expected-owner lease for the optional renderer resize edge
internal sealed class FramebufferDevToolsWiring : IDisposable
{
    private readonly FramebufferResizeDriver _holder;
    private readonly IFramebufferDeviceToolsMark _mark;
    private bool _destroyed;

    public FramebufferDevToolsWiring(
        FramebufferResizeDriver owner,
        IFramebufferDeviceToolsMark target)
    {
        _holder = owner ?? throw new ArgumentNullException(nameof(owner));
        _mark = target ?? throw new ArgumentNullException(nameof(target));
        _holder.AttachDevTools(_mark);
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _holder.LoosenDevTools(_mark);
        _destroyed = true;
    }
}

internal sealed class FramebufferResizeDriver(ViewportAspectLedger viewportAspect)
{
    private readonly ViewportAspectLedger _viewRectAspect = viewportAspect
            ?? throw new ArgumentNullException(nameof(viewportAspect));
    private IFramebufferViewRectMark? _viewRect;
    private IFramebufferCameraMark? _cam;
    private IFramebufferDeviceToolsMark? _devTools;

    public void AttachViewRect(IFramebufferViewRectMark viewRect)
    {
        ArgumentNullException.ThrowIfNull(viewRect);
        AttachOnce(ref _viewRect, viewRect, "viewport");
    }

    public void AttachCam(IFramebufferCameraMark cam)
    {
        ArgumentNullException.ThrowIfNull(cam);
        AttachOnce(ref _cam, cam, "camera");
    }

    public void AttachDevTools(IFramebufferDeviceToolsMark devTools)
    {
        ArgumentNullException.ThrowIfNull(devTools);
        AttachOnce(ref _devTools, devTools, "developer tools");
    }

    public void LoosenDevTools(IFramebufferDeviceToolsMark devTools)
    {
        ArgumentNullException.ThrowIfNull(devTools);
        if (ReferenceEquals(_devTools, devTools))
            _devTools = null;
    }

    public void Resize(Vector2D<int> newDims) => Resize(newDims.X, newDims.Y);

    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        Console.WriteLine($"window: framebuffer resize event {width}x{height}");

        _viewRect?.RescaleViewRect(width, height);
        _viewRectAspect.Update(width, height);
        _cam?.SetAspect(width / (float)height);
        _devTools?.ResetLayout(width, height);
    }

    private static void AttachOnce<T>(ref T? socket, T val, string label)
        where T : class
    {
        if (socket is not null && !ReferenceEquals(socket, val))
            throw new InvalidOperationException(
                $"The framebuffer {label} target is by now bound");
        socket = val;
    }
}
