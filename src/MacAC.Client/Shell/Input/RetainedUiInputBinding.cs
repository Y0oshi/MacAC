using System.Numerics;
using MacAC.Client.Graphics;
using Silk.NET.Input;

namespace MacAC.Client.Shell;

internal interface IRetainedWidgetInputWiring : IDisposable
{
    bool IsDisposalComplete { get; }
    void Attach();
    void Deactivate();
}

internal interface IKeptPointerCanvas
{
    void AttachPointerDown(Action<MouseButton, int, int> hook);
    void DeletePointerDown(Action<MouseButton, int, int> hook);
    void AttachPointerUp(Action<MouseButton, int, int> hook);
    void DeletePointerUp(Action<MouseButton, int, int> hook);
    void AppendMouseMove(Action<int, int> hook);
    void DropMouseMove(Action<int, int> hook);
    void AttachRoll(Action<int> hook);
    void DeleteRoll(Action<int> hook);
}

internal sealed class SilkKeptPointerCanvas : IKeptPointerCanvas
{
    private readonly IMouse _pointer;
    private Action<MouseButton, int, int>? _downHook;
    private Action<MouseButton, int, int>? _upHook;
    private Action<int, int>? _relocateHook;
    private Action<int>? _rollHook;
    private readonly Action<IMouse, MouseButton> _down;
    private readonly Action<IMouse, MouseButton> _up;
    private readonly Action<IMouse, Vector2> _relocate;
    private readonly Action<IMouse, ScrollWheel> _roll;

    public SilkKeptPointerCanvas(IMouse mouse)
    {
        _pointer = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _down = OnDown;
        _up = OnUp;
        _relocate = OnRelocate;
        _roll = OnRoll;
    }

    public void AttachPointerDown(Action<MouseButton, int, int> callback)
    {
        _downHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseDown += _down;
    }

    public void DeletePointerDown(Action<MouseButton, int, int> hook)
    {
        if (!ReferenceEquals(_downHook, hook)) return;
        _pointer.MouseDown -= _down;
        _downHook = null;
    }

    public void AttachPointerUp(Action<MouseButton, int, int> callback)
    {
        _upHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseUp += _up;
    }

    public void DeletePointerUp(Action<MouseButton, int, int> hook)
    {
        if (!ReferenceEquals(_upHook, hook)) return;
        _pointer.MouseUp -= _up;
        _upHook = null;
    }

    public void AppendMouseMove(Action<int, int> callback)
    {
        _relocateHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.MouseMove += _relocate;
    }

    public void DropMouseMove(Action<int, int> hook)
    {
        if (!ReferenceEquals(_relocateHook, hook)) return;
        _pointer.MouseMove -= _relocate;
        _relocateHook = null;
    }

    public void AttachRoll(Action<int> callback)
    {
        _rollHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _pointer.Scroll += _roll;
    }

    public void DeleteRoll(Action<int> hook)
    {
        if (!ReferenceEquals(_rollHook, hook)) return;
        _pointer.Scroll -= _roll;
        _rollHook = null;
    }

    private void OnDown(IMouse sender, MouseButton btn) =>
        _downHook?.Invoke(btn, (int)sender.Position.X, (int)sender.Position.Y);
    private void OnUp(IMouse sender, MouseButton btn) =>
        _upHook?.Invoke(btn, (int)sender.Position.X, (int)sender.Position.Y);
    private void OnRelocate(IMouse _, Vector2 locus) =>
        _relocateHook?.Invoke((int)locus.X, (int)locus.Y);
    private void OnRoll(IMouse _, ScrollWheel roll) =>
        _rollHook?.Invoke((int)roll.Y);
}

internal sealed class RetainedMouseInputWiring : IRetainedWidgetInputWiring
{
    private readonly IKeptPointerCanvas _canvas;
    private readonly WidgetTrunk _trunk;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<MouseButton, int, int> _down;
    private readonly Action<MouseButton, int, int> _up;
    private readonly Action<int, int> _relocate;
    private readonly Action<int> _roll;
    private readonly bool[] _affixed = new bool[4];
    private AssetShutdownTransaction? _unfasten;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;

    public RetainedMouseInputWiring(
        IKeptPointerCanvas surface,
        WidgetTrunk root,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _trunk = root ?? throw new ArgumentNullException(nameof(root));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _down = OnDown;
        _up = OnUp;
        _relocate = OnRelocate;
        _roll = OnRoll;
    }

    public bool IsDisposalComplete => _affixed.All(static val => !val);

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
            throw new InvalidOperationException("Retained mouse attachment has by now started");
        _fastenBegun = true;
        try
        {
            _affixed[0] = true;
            _canvas.AttachPointerDown(_down);
            _affixed[1] = true;
            _canvas.AttachPointerUp(_up);
            _affixed[2] = true;
            _canvas.AppendMouseMove(_relocate);
            _affixed[3] = true;
            _canvas.AttachRoll(_roll);
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            RollBackOrThrow(fastenProblem);
        }
    }

    public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private void OnDown(MouseButton btn, int x, int y) =>
        Invoke(() => _trunk.OnPointerDown(ChartBtn(btn), x, y));
    private void OnUp(MouseButton btn, int x, int y) =>
        Invoke(() => _trunk.OnPointerUp(ChartBtn(btn), x, y));
    private void OnRelocate(int x, int y) => Invoke(() => _trunk.OnPointerRelocate(x, y));
    private void OnRoll(int quantity) => Invoke(() => _trunk.OnRoll(quantity));

    private void Invoke(Action hook)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                hook();
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("retained mouse callbacks",
            [
                new("scroll", () => Drop(3)),
                new("move", () => Drop(2)),
                new("up", () => Drop(1)),
                new("down", () => Drop(0)),
            ]));
    }

    private void Drop(int index)
    {
        if (!_affixed[index]) return;
        switch (index)
        {
            case 0: _canvas.DeletePointerDown(_down); break;
            case 1: _canvas.DeletePointerUp(_up); break;
            case 2: _canvas.DropMouseMove(_relocate); break;
            case 3: _canvas.DeleteRoll(_roll); break;
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
                "Retained mouse registration and rollback both failed",
                new InvalidOperationException("Retained mouse registration failed", fastenProblem),
                undoProblem);
        }
        throw new InvalidOperationException(
            "Retained mouse registration failed and was rolled back", fastenProblem);
    }

    private static WidgetMouseButton ChartBtn(MouseButton btn)
    {
        return btn switch
        {
            MouseButton.Left => WidgetMouseButton.Left,
            MouseButton.Right => WidgetMouseButton.Right,
            MouseButton.Middle => WidgetMouseButton.Middle,
            _ => WidgetMouseButton.Left,
        };
    }
}

internal interface IKeptKeyboardCanvas
{
    void AppendLookupKeyDown(Action<Key> hook);
    void DropLookupKeyDown(Action<Key> hook);
    void AppendLookupKeyUp(Action<Key> hook);
    void DropLookupKeyUp(Action<Key> hook);
    void AppendTagChar(Action<char> hook);
    void DropTagChar(Action<char> hook);
}

internal sealed class SilkKeptKeyboardCanvas : IKeptKeyboardCanvas
{
    private readonly IKeyboard _keyboard;
    private Action<Key>? _downHook;
    private Action<Key>? _upHook;
    private Action<char>? _charHook;
    private readonly Action<IKeyboard, Key, int> _down;
    private readonly Action<IKeyboard, Key, int> _up;
    private readonly Action<IKeyboard, char> _char;

    public SilkKeptKeyboardCanvas(IKeyboard keyboard)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _down = OnDown;
        _up = OnUp;
        _char = OnChar;
    }

    public void AppendLookupKeyDown(Action<Key> callback)
    {
        _downHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _keyboard.KeyDown += _down;
    }

    public void DropLookupKeyDown(Action<Key> hook)
    {
        if (!ReferenceEquals(_downHook, hook)) return;
        _keyboard.KeyDown -= _down;
        _downHook = null;
    }

    public void AppendLookupKeyUp(Action<Key> callback)
    {
        _upHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _keyboard.KeyUp += _up;
    }

    public void DropLookupKeyUp(Action<Key> hook)
    {
        if (!ReferenceEquals(_upHook, hook)) return;
        _keyboard.KeyUp -= _up;
        _upHook = null;
    }

    public void AppendTagChar(Action<char> callback)
    {
        _charHook = callback ?? throw new ArgumentNullException(nameof(callback));
        _keyboard.KeyChar += _char;
    }

    public void DropTagChar(Action<char> hook)
    {
        if (!ReferenceEquals(_charHook, hook)) return;
        _keyboard.KeyChar -= _char;
        _charHook = null;
    }

    private void OnDown(IKeyboard _, Key tag, int __) => _downHook?.Invoke(tag);
    private void OnUp(IKeyboard _, Key tag, int __) => _upHook?.Invoke(tag);
    private void OnChar(IKeyboard _, char val) => _charHook?.Invoke(val);
}

internal sealed class RetainedKeyboardInputWiring : IRetainedWidgetInputWiring
{
    private readonly IKeptKeyboardCanvas _canvas;
    private readonly WidgetTrunk _trunk;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<Key> _down;
    private readonly Action<Key> _up;
    private readonly Action<char> _char;
    private readonly bool[] _affixed = new bool[3];
    private AssetShutdownTransaction? _unfasten;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;

    public RetainedKeyboardInputWiring(
        IKeptKeyboardCanvas surface,
        WidgetTrunk root,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _trunk = root ?? throw new ArgumentNullException(nameof(root));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _down = tag => Invoke(() => _trunk.OnTagDown((int)tag));
        _up = tag => Invoke(() => _trunk.OnTagUp((int)tag));
        _char = val => Invoke(() => _trunk.OnChar(val));
    }

    public bool IsDisposalComplete => _affixed.All(static val => !val);

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
            throw new InvalidOperationException("Retained keyboard attachment has by now started");
        _fastenBegun = true;
        try
        {
            _affixed[0] = true;
            _canvas.AppendLookupKeyDown(_down);
            _affixed[1] = true;
            _canvas.AppendLookupKeyUp(_up);
            _affixed[2] = true;
            _canvas.AppendTagChar(_char);
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            RollBackOrThrow(fastenProblem);
        }
    }

    public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private void Invoke(Action hook)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                hook();
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("retained keyboard callbacks",
            [
                new("character", () => Drop(2)),
                new("up", () => Drop(1)),
                new("down", () => Drop(0)),
            ]));
    }

    private void Drop(int index)
    {
        if (!_affixed[index]) return;
        switch (index)
        {
            case 0: _canvas.DropLookupKeyDown(_down); break;
            case 1: _canvas.DropLookupKeyUp(_up); break;
            case 2: _canvas.DropTagChar(_char); break;
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
                "Retained keyboard registration and rollback both failed",
                new InvalidOperationException("Retained keyboard registration failed", fastenProblem),
                undoProblem);
        }
        throw new InvalidOperationException(
            "Retained keyboard registration failed and was rolled back", fastenProblem);
    }
}
