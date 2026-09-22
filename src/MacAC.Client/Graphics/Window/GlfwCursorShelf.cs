using MacAC.Client.Shell;
using Silk.NET.Core;
using Silk.NET.GLFW;
using Silk.NET.Input;

namespace MacAC.Client.Graphics;

internal sealed unsafe class GlfwCursorShelf : IDisposable
{
    private readonly Glfw _glfw;
    private readonly WindowHandle* _window;
    private readonly Dictionary<WidgetCursorMedia, nint> _customCursors = [];
    private readonly Dictionary<StandardCursor, nint> _standardCursors = [];
    private bool _destroyed;

    private GlfwCursorShelf(Glfw glfw, WindowHandle* pane)
    {
        _glfw = glfw;
        _window = pane;
    }

    public static GlfwCursorShelf? TryBuild(nint glfwPaneHnd)
    {
        return glfwPaneHnd == 0
                ? null
                : new GlfwCursorShelf(Glfw.GetApi(), (WindowHandle*)glfwPaneHnd);
    }

    public bool TrySetCustom(WidgetCursorMedia media, RawImage image)
    {
        if (_destroyed)
            return false;

        if (!_customCursors.TryGetValue(media, out nint cur))
        {
            cur = BuildCustomCur(media, image);
            _customCursors[media] = cur;
        }

        if (cur == 0)
            return false;

        _glfw.SetCursor(_window, (Cursor*)cur);
        return true;
    }

    public bool TrySetStandard(StandardCursor wanted)
    {
        if (_destroyed)
            return false;

        if (wanted == StandardCursor.Arrow)
        {
            _glfw.SetCursor(_window, null);
            return true;
        }

        CursorShape form;
        switch (wanted)
        {
            case StandardCursor.Hand: form = CursorShape.Hand; break;
            case StandardCursor.Crosshair: form = CursorShape.Crosshair; break;
            case StandardCursor.IBeam: form = CursorShape.IBeam; break;
            default: return false;
        }

        if (!_standardCursors.TryGetValue(wanted, out nint cur))
        {
            cur = (nint)_glfw.CreateStandardCursor(form);
            _standardCursors[wanted] = cur;
        }

        if (cur == 0)
            return false;

        _glfw.SetCursor(_window, (Cursor*)cur);
        return true;
    }

    public void Dispose()
    {
        if (_destroyed)
            return;
        _destroyed = true;

        _glfw.SetCursor(_window, null);
        foreach (nint cur in _customCursors.Values)
        {
            if (cur != 0)
                _glfw.DestroyCursor((Cursor*)cur);
        }
        foreach (nint cur in _standardCursors.Values)
        {
            if (cur != 0)
                _glfw.DestroyCursor((Cursor*)cur);
        }
        _customCursors.Clear();
        _standardCursors.Clear();
    }

    private nint BuildCustomCur(WidgetCursorMedia media, RawImage image)
    {
        fixed (byte* px = image.Pixels.Span)
        {
            Image glfwImage = new Image
            {
                Width = image.Width,
                Height = image.Height,
                Pixels = px,
            };
            return (nint)_glfw.CreateCursor(&glfwImage, media.HotspotX, media.HotspotY);
        }
    }
}
