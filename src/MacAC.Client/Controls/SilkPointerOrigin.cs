using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Input;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal interface IPointerSignalCanvas
{
    void AppendPointerDown(Action<MouseButton> hook);
    void DropPointerDown(Action<MouseButton> hook);
    void AppendPointerUp(Action<MouseButton> hook);
    void DropPointerUp(Action<MouseButton> hook);
    void AttachPointerRelocate(Action<Vector2> hook);
    void DeletePointerRelocate(Action<Vector2> hook);
    void AppendRoll(Action<float> hook);
    void DropRoll(Action<float> hook);
    bool IsBtnPressed(MouseButton btn);
}

internal sealed class SilkPointerSignalCanvas : IPointerSignalCanvas
{
    private readonly IMouse _pointer;
    private Action<MouseButton>? _downHook;
    private Action<MouseButton>? _upHook;
    private Action<Vector2>? _relocateHook;
    private Action<float>? _rollHook;
    private readonly Action<IMouse, MouseButton> _down;
    private readonly Action<IMouse, MouseButton> _up;
    private readonly Action<IMouse, Vector2> _relocate;
    private readonly Action<IMouse, ScrollWheel> _roll;

    public SilkPointerSignalCanvas(IMouse mouse)
    {
        _pointer = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _down = OnDown;
        _up = OnUp;
        _relocate = OnRelocate;
        _roll = OnRoll;
    }

    public void AppendPointerDown(Action<MouseButton> callback)
    {
        _downHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseDown += _down;
    }

    public void DropPointerDown(Action<MouseButton> hook)
    {
        if (!ReferenceEquals(_downHook, hook)) return;
        _pointer.MouseDown -= _down;
        _downHook = null;
    }

    public void AppendPointerUp(Action<MouseButton> callback)
    {
        _upHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseUp += _up;
    }

    public void DropPointerUp(Action<MouseButton> hook)
    {
        if (!ReferenceEquals(_upHook, hook)) return;
        _pointer.MouseUp -= _up;
        _upHook = null;
    }

    public void AttachPointerRelocate(Action<Vector2> callback)
    {
        _relocateHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseMove += _relocate;
    }

    public void DeletePointerRelocate(Action<Vector2> hook)
    {
        if (!ReferenceEquals(_relocateHook, hook)) return;
        _pointer.MouseMove -= _relocate;
        _relocateHook = null;
    }

    public void AppendRoll(Action<float> callback)
    {
        _rollHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.Scroll += _roll;
    }

    public void DropRoll(Action<float> hook)
    {
        if (!ReferenceEquals(_rollHook, hook)) return;
        _pointer.Scroll -= _roll;
        _rollHook = null;
    }

    public bool IsBtnPressed(MouseButton btn) => _pointer.IsButtonPressed(btn);

    private void OnDown(IMouse _, MouseButton btn) => _downHook?.Invoke(btn);
    private void OnUp(IMouse _, MouseButton btn) => _upHook?.Invoke(btn);
    private void OnRelocate(IMouse _, Vector2 locus) => _relocateHook?.Invoke(locus);
    private void OnRoll(IMouse _, ScrollWheel roll) => _rollHook?.Invoke(roll.Y);
}

public sealed class SilkPointerOrigin : IMouseFeed, IDisposable
{
    private readonly IPointerSignalCanvas _canvas;
    private readonly IFeedGrabOrigin _grab;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<MouseButton> _pointerDown;
    private readonly Action<MouseButton> _pointerUp;
    private readonly Action<Vector2> _pointerRelocate;
    private readonly Action<float> _roll;
    private readonly bool[] _affixed = new bool[4];
    private AssetShutdownTransaction? _unfasten;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;
    private float _previousX;
    private float _previousY;
    private bool _havePreviousLocus;

    public event Action<MouseButton, ModifierBits>? MouseDown;
    public event Action<MouseButton, ModifierBits>? MouseUp;
    public event Action<float, float>? MouseMove;
    public event Action<float>? Scroll;

    public IKeyFeed? ModifierSrc { get; set; }

    private SilkPointerOrigin(
        IPointerSignalCanvas surface,
        IFeedGrabOrigin capture,
        IKeyFeed? modifierSrc,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _grab = capture ?? throw new ArgumentNullException(nameof(capture));
        ModifierSrc = modifierSrc;
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _pointerDown = OnPointerDown;
        _pointerUp = OnPointerUp;
        _pointerRelocate = OnPointerRelocate;
        _roll = OnRoll;
    }

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
            throw new InvalidOperationException("Mouse source attachment has by now started");
        _fastenBegun = true;
        try
        {
            _affixed[0] = true;
            _canvas.AppendPointerDown(_pointerDown);
            _affixed[1] = true;
            _canvas.AppendPointerUp(_pointerUp);
            _affixed[2] = true;
            _canvas.AttachPointerRelocate(_pointerRelocate);
            _affixed[3] = true;
            _canvas.AppendRoll(_roll);
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            RollBackOrThrow(fastenProblem);
        }
    }

    public bool IsHeld(MouseButton btn) => _canvas.IsBtnPressed(btn);

    public bool IsDisposalComplete => _affixed.All(static val => !val);

    public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }
    public bool WantCaptureMouse => _grab.WantCaptureMouse;
    public bool WantCaptureKeyboard => _grab.WantGrabKeyboard;

    internal static SilkPointerOrigin BuildDetached(
        IMouse pointer,
        IFeedGrabOrigin grab,
        IKeyFeed? modifierSrc,
        HostQuiescenceTurnstile stillness)
    {
        return new(
            new SilkPointerSignalCanvas(pointer),
            grab,
            modifierSrc,
            stillness);
    }

    internal static SilkPointerOrigin BuildDetached(
        IPointerSignalCanvas canvas,
        IFeedGrabOrigin grab,
        IKeyFeed? modifierSrc,
        HostQuiescenceTurnstile stillness) =>
        new(canvas, grab, modifierSrc, stillness);

    private ModifierBits ScanModifiers() =>
        ModifierSrc?.LatestModifiers ?? ModifierBits.None;

    private void OnPointerDown(MouseButton btn)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                MouseDown?.Invoke(btn, ScanModifiers());
        });
    }

    private void OnPointerUp(MouseButton btn)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                MouseUp?.Invoke(btn, ScanModifiers());
        });
    }

    private void OnPointerRelocate(Vector2 locus)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is 0)
                return;

            float dx = 0f;
            float dy = 0f;
            if (_havePreviousLocus)
            {
                dx = locus.X - _previousX;
                dy = locus.Y - _previousY;
            }
            else
            {
                _havePreviousLocus = true;
            }

            _previousX = locus.X;
            _previousY = locus.Y;
            MouseMove?.Invoke(dx, dy);
        });
    }

    private void OnRoll(float diff)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                Scroll?.Invoke(diff);
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("mouse source callbacks",
            [
                new("scroll", () => Drop(3)),
                new("move", () => Drop(2)),
                new("up", () => Drop(1)),
                new("down", () => Drop(0)),
            ]));
    }

    private void Drop(int index)
    {
        if (!_affixed[index])
            return;
        switch (index)
        {
            case 0: _canvas.DropPointerDown(_pointerDown); break;
            case 1: _canvas.DropPointerUp(_pointerUp); break;
            case 2: _canvas.DeletePointerRelocate(_pointerRelocate); break;
            case 3: _canvas.DropRoll(_roll); break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
        _affixed[index] = false;
    }

    private void RollBackOrThrow(Exception fastenProblem)
    {
        try
        {
            SecureUnfastenTransaction().CompleteOrThrow();
        }
        catch (Exception undoProblem)
        {
            throw new AggregateException(
                "Mouse source registration and rollback both failed",
                new InvalidOperationException(
                    "Mouse source registration failed", fastenProblem),
                undoProblem);
        }

        throw new InvalidOperationException(
            "Mouse source registration failed and was rolled back",
            fastenProblem);
    }
}
