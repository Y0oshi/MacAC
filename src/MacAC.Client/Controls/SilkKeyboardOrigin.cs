using MacAC.Client.Graphics;
using MacAC.Cockpit.Input;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal interface IKeyboardSignalCanvas
{
    void AppendTagDown(Action<Key> hook);
    void DropTagDown(Action<Key> hook);
    void AppendTagUp(Action<Key> hook);
    void DropTagUp(Action<Key> hook);
    bool IsTagPressed(Key tag);
}

internal sealed class SilkKeyboardSignalCanvas : IKeyboardSignalCanvas
{
    private readonly IKeyboard _keyboard;
    private Action<Key>? _tagDownHook;
    private Action<Key>? _tagUpHook;
    private readonly Action<IKeyboard, Key, int> _tagDown;
    private readonly Action<IKeyboard, Key, int> _tagUp;

    public SilkKeyboardSignalCanvas(IKeyboard keyboard)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _tagDown = OnTagDown;
        _tagUp = OnTagUp;
    }

    public void AppendTagDown(Action<Key> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _tagDownHook = hook;
        _keyboard.KeyDown += _tagDown;
    }

    public void DropTagDown(Action<Key> hook)
    {
        if (!ReferenceEquals(_tagDownHook, hook))
            return;
        _keyboard.KeyDown -= _tagDown;
        _tagDownHook = null;
    }

    public void AppendTagUp(Action<Key> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _tagUpHook = hook;
        _keyboard.KeyUp += _tagUp;
    }

    public void DropTagUp(Action<Key> hook)
    {
        if (!ReferenceEquals(_tagUpHook, hook))
            return;
        _keyboard.KeyUp -= _tagUp;
        _tagUpHook = null;
    }

    public bool IsTagPressed(Key tag) => _keyboard.IsKeyPressed(tag);

    private void OnTagDown(IKeyboard _, Key tag, int __) =>
        _tagDownHook?.Invoke(tag);

    private void OnTagUp(IKeyboard _, Key tag, int __) =>
        _tagUpHook?.Invoke(tag);
}

public sealed class SilkKeyboardOrigin : IKeyFeed, IDisposable
{
    private readonly IKeyboardSignalCanvas _canvas;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly Action<Key> _tagDown;
    private readonly Action<Key> _tagUp;
    private readonly bool[] _affixed = new bool[2];
    private AssetShutdownTransaction? _unfasten;
    private bool _fastenBegun;
    private int _teardownAsked;
    private int _engaged;

    public event Action<Key, ModifierBits>? KeyDown;
    public event Action<Key, ModifierBits>? KeyUp;

    private SilkKeyboardOrigin(
        IKeyboardSignalCanvas surface,
        HostQuiescenceTurnstile quiescence)
    {
        _canvas = surface ?? throw new ArgumentNullException(nameof(surface));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _tagDown = OnTagDown;
        _tagUp = OnTagUp;
    }

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
            throw new InvalidOperationException("Keyboard source attachment has by now started");
        _fastenBegun = true;

        try
        {
            _affixed[0] = true;
            _canvas.AppendTagDown(_tagDown);
            _affixed[1] = true;
            _canvas.AppendTagUp(_tagUp);
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            RollBackOrThrow(fastenProblem);
        }
    }

    public bool IsPinned(Key tag) => _canvas.IsTagPressed(tag);

    public bool IsDisposalComplete => _affixed.All(static val => !val);

    public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    public ModifierBits LatestModifiers => ScanModifiers();

    internal static SilkKeyboardOrigin BuildDetached(
        IKeyboard keyboard,
        HostQuiescenceTurnstile stillness)
    {
        return new(
            new SilkKeyboardSignalCanvas(keyboard),
            stillness);
    }

    internal static SilkKeyboardOrigin BuildDetached(
        IKeyboardSignalCanvas canvas,
        HostQuiescenceTurnstile stillness) =>
        new(canvas, stillness);

    private void OnTagDown(Key tag)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                KeyDown?.Invoke(tag, ScanModifiers());
        });
    }

    private void OnTagUp(Key tag)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                KeyUp?.Invoke(tag, ScanModifiers());
        });
    }

    private ModifierBits ScanModifiers()
    {
        var modifiers = ModifierBits.None;
        if (_canvas.IsTagPressed(Key.ShiftLeft) || _canvas.IsTagPressed(Key.ShiftRight))
            modifiers |= ModifierBits.Shift;
        if (_canvas.IsTagPressed(Key.ControlLeft) || _canvas.IsTagPressed(Key.ControlRight))
            modifiers |= ModifierBits.Ctrl;
        if (_canvas.IsTagPressed(Key.AltLeft) || _canvas.IsTagPressed(Key.AltRight))
            modifiers |= ModifierBits.Alt;
        if (_canvas.IsTagPressed(Key.SuperLeft) || _canvas.IsTagPressed(Key.SuperRight))
            modifiers |= ModifierBits.Win;
        return modifiers;
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        return _unfasten ??= new AssetShutdownTransaction(
            new AssetShutdownJuncture("keyboard source callbacks",
            [
                new("key up", () => Drop(1)),
                new("key down", () => Drop(0)),
            ]));
    }

    private void Drop(int ordinal)
    {
        if (!_affixed[ordinal])
            return;
        if (ordinal is 1)
            _canvas.DropTagUp(_tagUp);
        else
            _canvas.DropTagDown(_tagDown);
        _affixed[ordinal] = false;
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
                "Keyboard source registration and rollback both failed",
                new InvalidOperationException(
                    "Keyboard source registration failed", fastenProblem),
                undoProblem);
        }

        throw new InvalidOperationException(
            "Keyboard source registration failed and was rolled back",
            fastenProblem);
    }
}
