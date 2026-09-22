using Silk.NET.GLFW;
using Silk.NET.Windowing;

namespace MacAC.Client.Preferences;

internal interface IReadoutMannerSwitcher
{
    // True while the window is a native fullscreen window (has a monitor attached)
    bool IsFullscreen { get; }

    (int Width, int Height)? LatestFullscreenManner { get; }

    bool TryJoinFullscreen(int width, int height, out string? problem);

    bool TryDepartFullscreen(int width, int height, out string? problem);
}

internal sealed unsafe class GlfwReadoutMannerSwitcher(IWindow window) : IReadoutMannerSwitcher
{
    private readonly IWindow _window = window ?? throw new ArgumentNullException(nameof(window));

    private static readonly Lazy<Glfw> Api = new(Glfw.GetApi);

    private static (int X, int Y) _windowedLocus = (60, 60);

    private static bool MacOsHub { get; } =
        MacAC.Client.Machine.GraphicalHubPlatformServices.SenseOperatingSys()
            == MacAC.Client.Machine.GraphicalHubOperatingSys.MacOS;

    public bool IsFullscreen
    {
        get
        {
            try
            {
                var hnd = Handle();
                return hnd is null ? false : Api.Value.GetWindowMonitor(hnd) is not null;
            }
            catch (GlfwException)
            {
                return false;
            }
        }
    }

    public (int Width, int Height)? LatestFullscreenManner
    {
        get
        {
            try
            {
                var hnd = Handle();
                if (hnd is null) return null;
                Glfw glfw = Api.Value;
                var observe = glfw.GetWindowMonitor(hnd);
                if (observe is null) return null;
                var manner = glfw.GetVideoMode(observe);
                return manner is null ? null : (manner->Width, manner->Height);
            }
            catch (GlfwException)
            {
                return null;
            }
        }
    }

    public bool TryJoinFullscreen(int width, int height, out string? problem)
    {
        problem = null;
        var hnd = Handle();
        if (hnd is null)
        {
            problem = "no native GLFW window handle";
            return false;
        }

        try
        {
            Glfw glfw = Api.Value;
            var observe = LocatePaneObserve(glfw, hnd);
            if (observe is null)
            {
                problem = "no monitor";
                return false;
            }

            if (!TrySeekRenewRate(glfw, observe, width, height, out int renew))
            {
                problem = $"mode {width}x{height} is not in the monitor's mode list";
                return false;
            }

            if (glfw.GetWindowMonitor(hnd) is null)
            {
                glfw.GetWindowPos(hnd, out int x, out int y);
                _windowedLocus = (x, y);
            }

            if (MacOsHub)
            {
                glfw.SetWindowAttrib(
                    hnd,
                    WindowAttributeSetter.AutoIconify,
                    false);
            }

            glfw.SetWindowMonitor(hnd, observe, 0, 0, width, height, renew);

            if (glfw.GetWindowMonitor(hnd) is null)
            {
                problem = "the mode switch did not take (GLFW reports no monitor attached)";
                return false;
            }

            Console.WriteLine(
                $"display: fullscreen mode switch {width}x{height}@{renew}");
            return true;
        }
        catch (GlfwException exc)
        {
            problem = exc.Message;
            return false;
        }
    }

    public bool TryDepartFullscreen(int width, int height, out string? problem)
    {
        problem = null;
        var hnd = Handle();
        if (hnd is null)
        {
            problem = "no native GLFW window handle";
            return false;
        }

        try
        {
            Glfw glfw = Api.Value;
            if (glfw.GetWindowMonitor(hnd) is null)
                return true;   // already windowed
            glfw.SetWindowMonitor(
                hnd, null,
                _windowedLocus.X, _windowedLocus.Y,
                width, height, 0);

            if (glfw.GetWindowMonitor(hnd) is not null)
            {
                problem = "the window is still fullscreen (GLFW reports a monitor attached)";
                return false;
            }

            Console.WriteLine(
                $"display: left fullscreen to windowed {width}x{height}");
            return true;
        }
        catch (GlfwException exc)
        {
            problem = exc.Message;
            return false;
        }
    }

    private Silk.NET.GLFW.Monitor* LocatePaneObserve(Glfw glfw, WindowHandle* hnd)
    {
        var affixed = glfw.GetWindowMonitor(hnd);
        if (affixed is not null) return affixed;

        int? ordinal = _window.Monitor?.Index;
        if (ordinal is int idx && idx >= 0)
        {
            var monitors = glfw.GetMonitors(out int tally);
            if (monitors is not null && idx < tally)
                return monitors[idx];
        }
        return glfw.GetPrimaryMonitor();
    }

    private static bool TrySeekRenewRate(
        Glfw glfw, Silk.NET.GLFW.Monitor* observe, int width, int height, out int renew)
    {
        renew = 0;
        var manners = glfw.GetVideoModes(observe, out int tally);
        if (manners is null) return false;
        for (int idx = 0; idx < tally; ++idx)
        {
            if (manners[idx].Width == width && manners[idx].Height == height)
                renew = Math.Max(renew, manners[idx].RefreshRate);
        }
        return renew > 0;
    }

    private WindowHandle* Handle()
    {
        nint native = _window.Native?.Glfw ?? 0;
        return native == 0 ? null : (WindowHandle*)native;
    }
}
