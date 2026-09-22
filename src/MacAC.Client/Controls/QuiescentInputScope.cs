using System.Numerics;
using MacAC.Client.Graphics;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal interface IDevToolsInputScope : IInputContext, IDisposable
{
    bool IsDisposalComplete { get; }
    void Activate();
    void Deactivate();
}

internal sealed class QuiescentInputScope : IDevToolsInputScope
{
    private readonly IInputContext _interior;
    private readonly HostQuiescenceTurnstile _stillness;
    private readonly ClientQuiescentKeyboard[] _keyboards;
    private readonly QuiescentPointer[] _mice;
    private readonly Action<IInputDevice, bool> _connectionAltered;
    private Action<IInputDevice, bool>? _connectionSubscribers;
    private AssetShutdownTransaction? _unfasten;
    private bool _connectionAffixed;
    private int _engaged;
    private bool _activationBegun;
    private int _teardownAsked;

    public QuiescentInputScope(
        IInputContext inner,
        HostQuiescenceTurnstile quiescence)
    {
        _interior = inner ?? throw new ArgumentNullException(nameof(inner));
        _stillness = quiescence ?? throw new ArgumentNullException(nameof(quiescence));
        _keyboards = [.. inner.Keyboards.Select(keyboard => new ClientQuiescentKeyboard(keyboard, quiescence))];
        _mice = [.. inner.Mice.Select(pointer => new QuiescentPointer(pointer, quiescence))];
        _connectionAltered = OnConnectionAltered;
    }

    public nint Handle => _interior.Handle;
    public IReadOnlyList<IGamepad> Gamepads => _interior.Gamepads;
    public IReadOnlyList<IJoystick> Joysticks => _interior.Joysticks;
    public IReadOnlyList<IKeyboard> Keyboards => _keyboards;
    public IReadOnlyList<IMouse> Mice => _mice;
    public IReadOnlyList<IInputDevice> OtherDevices => _interior.OtherDevices;

    public event Action<IInputDevice, bool>? ConnectionChanged
    {
        add
        {
            if (value is null) return;
            HurlIfDestroyed();
            _connectionSubscribers += value;
            if (_connectionAffixed) return;
            _connectionAffixed = true;
            _interior.ConnectionChanged += _connectionAltered;
        }
        remove
        {
            if (value is null) return;
            _connectionSubscribers -= value;
            if (_connectionSubscribers is not null || !_connectionAffixed) return;
            _interior.ConnectionChanged -= _connectionAltered;
            _connectionAffixed = false;
        }
    }

    public bool IsDisposalComplete
    {
        get
        {
            return !_connectionAffixed
        && _keyboards.All(static keyboard => keyboard.IsDisposalComplete)
        && _mice.All(static pointer => pointer.IsDisposalComplete);
        }
    }

    public void Activate()
    {
        HurlIfDestroyed();
        if (_activationBegun)
            throw new InvalidOperationException(
                "Frontend input activation has by now been attempted");
        _activationBegun = true;
        foreach (ClientQuiescentKeyboard keyboard in _keyboards)
            keyboard.Activate();
        foreach (QuiescentPointer pointer in _mice)
            pointer.Activate();
        Volatile.Write(ref _engaged, 1);
    }

    public void Deactivate()
    {
        Interlocked.Exchange(ref _engaged, 0);
        foreach (ClientQuiescentKeyboard keyboard in _keyboards)
            keyboard.Deactivate();
        foreach (QuiescentPointer pointer in _mice)
            pointer.Deactivate();
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        foreach (ClientQuiescentKeyboard keyboard in _keyboards)
            keyboard.ReqDisposal();
        foreach (QuiescentPointer pointer in _mice)
            pointer.RequestDisposal();
        Deactivate();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private void HurlIfDestroyed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
    }

    private void OnConnectionAltered(IInputDevice dev, bool connected)
    {
        _stillness.Invoke(() =>
        {
            if (Volatile.Read(ref _engaged) is not 0)
                _connectionSubscribers?.Invoke(dev, connected);
        });
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        if (_unfasten is not null)
            return _unfasten;

        var ops = new List<AssetShutdownOp>();
        for (int idx = _mice.Length - 1; idx >= 0; --idx)
        {
            int ordinal = idx;
            ops.Add(new AssetShutdownOp(
                $"frontend mouse {ordinal}",
                _mice[ordinal].Dispose));
        }
        for (int idx = _keyboards.Length - 1; idx >= 0; --idx)
        {
            int ordinal = idx;
            ops.Add(new AssetShutdownOp(
                $"frontend keyboard {ordinal}",
                _keyboards[ordinal].Dispose));
        }
        ops.Add(new AssetShutdownOp(
            "frontend connection event",
            DropConnectionAltered));
        _unfasten = new AssetShutdownTransaction(
            new AssetShutdownJuncture("frontend input callbacks", [.. ops]));
        return _unfasten;
    }

    private void DropConnectionAltered()
    {
        if (!_connectionAffixed) return;
        _interior.ConnectionChanged -= _connectionAltered;
        _connectionAffixed = false;
        _connectionSubscribers = null;
    }

    private sealed class ClientQuiescentKeyboard : IKeyboard, IDisposable
    {
        private readonly IKeyboard _interior;
        private readonly HostQuiescenceTurnstile _stillness;
        private readonly Action<IKeyboard, Key, int> _downRelay;
        private readonly Action<IKeyboard, Key, int> _upRelay;
        private readonly Action<IKeyboard, char> _charRelay;
        private Action<IKeyboard, Key, int>? _downSubscribers;
        private Action<IKeyboard, Key, int>? _upSubscribers;
        private Action<IKeyboard, char>? _charSubscribers;
        private readonly bool[] _affixed = new bool[3];
        private AssetShutdownTransaction? _unfasten;
        private int _teardownAsked;
        private int _engaged;

        public ClientQuiescentKeyboard(IKeyboard inner, HostQuiescenceTurnstile stillness)
        {
            _interior = inner ?? throw new ArgumentNullException(nameof(inner));
            _stillness = stillness;
            _downRelay = OnDown;
            _upRelay = OnUp;
            _charRelay = OnChar;
        }

        public string Name => _interior.Name;
        public int Index => _interior.Index;
        public bool IsConnected => _interior.IsConnected;
        public IReadOnlyList<Key> SupportedKeys => _interior.SupportedKeys;
        public string ClipboardText
        {
            get => _interior.ClipboardText;
            set => _interior.ClipboardText = value;
        }

        public event Action<IKeyboard, Key, int>? KeyDown
        {
            add
            {
                if (value is null) return;
                HurlIfDestroyed();
                _downSubscribers += value;
                if (_affixed[0]) return;
                _affixed[0] = true;
                _interior.KeyDown += _downRelay;
            }
            remove
            {
                if (value is null) return;
                _downSubscribers -= value;
                if (_downSubscribers is not null || !_affixed[0]) return;
                _interior.KeyDown -= _downRelay;
                _affixed[0] = false;
            }
        }

        public event Action<IKeyboard, Key, int>? KeyUp
        {
            add
            {
                if (value is null) return;
                HurlIfDestroyed();
                _upSubscribers += value;
                if (_affixed[1]) return;
                _affixed[1] = true;
                _interior.KeyUp += _upRelay;
            }
            remove
            {
                if (value is null) return;
                _upSubscribers -= value;
                if (_upSubscribers is not null || !_affixed[1]) return;
                _interior.KeyUp -= _upRelay;
                _affixed[1] = false;
            }
        }

        public event Action<IKeyboard, char>? KeyChar
        {
            add
            {
                if (value is null) return;
                HurlIfDestroyed();
                _charSubscribers += value;
                if (_affixed[2]) return;
                _affixed[2] = true;
                _interior.KeyChar += _charRelay;
            }
            remove
            {
                if (value is null) return;
                _charSubscribers -= value;
                if (_charSubscribers is not null || !_affixed[2]) return;
                _interior.KeyChar -= _charRelay;
                _affixed[2] = false;
            }
        }

        public bool IsDisposalComplete => _affixed.All(static val => !val);
        public bool IsKeyPressed(Key tag) => _interior.IsKeyPressed(tag);
        public bool IsScancodePressed(int scancode) => _interior.IsScancodePressed(scancode);
        public void BeginInput() => _interior.BeginInput();
        public void EndInput() => _interior.EndInput();
        public void Activate()
        {
            HurlIfDestroyed();
            Volatile.Write(ref _engaged, 1);
        }
        public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

        public void ReqDisposal() =>
            Interlocked.Exchange(ref _teardownAsked, 1);

        public void Dispose()
        {
            ReqDisposal();
            Deactivate();
            _unfasten ??= new AssetShutdownTransaction(
                new AssetShutdownJuncture("frontend keyboard callbacks",
                [
                    new("character", () => Drop(2)),
                    new("up", () => Drop(1)),
                    new("down", () => Drop(0)),
                ]));
            _unfasten.CompleteOrThrow();
        }

        private void HurlIfDestroyed()
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _teardownAsked) is not 0,
                this);
        }

        private void OnDown(IKeyboard _, Key tag, int scanCode) =>
            Invoke(() => _downSubscribers?.Invoke(this, tag, scanCode));
        private void OnUp(IKeyboard _, Key tag, int scanCode) =>
            Invoke(() => _upSubscribers?.Invoke(this, tag, scanCode));
        private void OnChar(IKeyboard _, char val) =>
            Invoke(() => _charSubscribers?.Invoke(this, val));
        private void Invoke(Action hook)
        {
            _stillness.Invoke(() =>
            {
                if (Volatile.Read(ref _engaged) is not 0)
                    hook();
            });
        }

        private void Drop(int index)
        {
            if (!_affixed[index]) return;
            switch (index)
            {
                case 0: _interior.KeyDown -= _downRelay; _downSubscribers = null; break;
                case 1: _interior.KeyUp -= _upRelay; _upSubscribers = null; break;
                case 2: _interior.KeyChar -= _charRelay; _charSubscribers = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
            _affixed[index] = false;
        }
    }

    private sealed class QuiescentPointer : IMouse, IDisposable
    {
        private readonly IMouse _interior;
        private readonly HostQuiescenceTurnstile _stillness;
        private readonly Action<IMouse, MouseButton> _downRelay;
        private readonly Action<IMouse, MouseButton> _upRelay;
        private readonly Action<IMouse, MouseButton, Vector2> _pressRelay;
        private readonly Action<IMouse, MouseButton, Vector2> _doublePressRelay;
        private readonly Action<IMouse, Vector2> _relocateRelay;
        private readonly Action<IMouse, ScrollWheel> _rollRelay;
        private Action<IMouse, MouseButton>? _downSubscribers;
        private Action<IMouse, MouseButton>? _upSubscribers;
        private Action<IMouse, MouseButton, Vector2>? _pressSubscribers;
        private Action<IMouse, MouseButton, Vector2>? _doublePressSubscribers;
        private Action<IMouse, Vector2>? _relocateSubscribers;
        private Action<IMouse, ScrollWheel>? _rollSubscribers;
        private readonly bool[] _affixed = new bool[6];
        private AssetShutdownTransaction? _unfasten;
        private int _teardownAsked;
        private int _engaged;

        public QuiescentPointer(IMouse inner, HostQuiescenceTurnstile stillness)
        {
            _interior = inner ?? throw new ArgumentNullException(nameof(inner));
            _stillness = stillness;
            _downRelay = OnDown;
            _upRelay = OnUp;
            _pressRelay = OnClick;
            _doublePressRelay = OnDoublePress;
            _relocateRelay = OnRelocate;
            _rollRelay = OnRoll;
        }

        public string Name => _interior.Name;
        public int Index => _interior.Index;
        public bool IsConnected => _interior.IsConnected;
        public IReadOnlyList<MouseButton> SupportedButtons => _interior.SupportedButtons;
        public IReadOnlyList<ScrollWheel> ScrollWheels => _interior.ScrollWheels;
        public Vector2 Position { get => _interior.Position; set => _interior.Position = value; }
        public ICursor Cursor => _interior.Cursor;
        public int DoubleClickTime { get => _interior.DoubleClickTime; set => _interior.DoubleClickTime = value; }
        public int DoubleClickRange { get => _interior.DoubleClickRange; set => _interior.DoubleClickRange = value; }

        public event Action<IMouse, MouseButton> MouseDown
        {
            add => Add(ref _downSubscribers, value, 0, () => _interior.MouseDown += _downRelay);
            remove => DropSubscriber(ref _downSubscribers, value, 0, () => _interior.MouseDown -= _downRelay);
        }
        public event Action<IMouse, MouseButton> MouseUp
        {
            add => Add(ref _upSubscribers, value, 1, () => _interior.MouseUp += _upRelay);
            remove => DropSubscriber(ref _upSubscribers, value, 1, () => _interior.MouseUp -= _upRelay);
        }
        public event Action<IMouse, MouseButton, Vector2> Click
        {
            add => Add(ref _pressSubscribers, value, 2, () => _interior.Click += _pressRelay);
            remove => DropSubscriber(ref _pressSubscribers, value, 2, () => _interior.Click -= _pressRelay);
        }
        public event Action<IMouse, MouseButton, Vector2> DoubleClick
        {
            add => Add(ref _doublePressSubscribers, value, 3, () => _interior.DoubleClick += _doublePressRelay);
            remove => DropSubscriber(ref _doublePressSubscribers, value, 3, () => _interior.DoubleClick -= _doublePressRelay);
        }
        public event Action<IMouse, Vector2> MouseMove
        {
            add => Add(ref _relocateSubscribers, value, 4, () => _interior.MouseMove += _relocateRelay);
            remove => DropSubscriber(ref _relocateSubscribers, value, 4, () => _interior.MouseMove -= _relocateRelay);
        }
        public event Action<IMouse, ScrollWheel> Scroll
        {
            add => Add(ref _rollSubscribers, value, 5, () => _interior.Scroll += _rollRelay);
            remove => DropSubscriber(ref _rollSubscribers, value, 5, () => _interior.Scroll -= _rollRelay);
        }

        public bool IsDisposalComplete => _affixed.All(static val => !val);
        public bool IsButtonPressed(MouseButton btn) => _interior.IsButtonPressed(btn);
        public void Activate()
        {
            HurlIfDestroyed();
            Volatile.Write(ref _engaged, 1);
        }
        public void Deactivate() => Interlocked.Exchange(ref _engaged, 0);

        public void RequestDisposal() =>
            Interlocked.Exchange(ref _teardownAsked, 1);

        public void Dispose()
        {
            RequestDisposal();
            Deactivate();
            _unfasten ??= new AssetShutdownTransaction(
                new AssetShutdownJuncture("frontend mouse callbacks",
                [
                    new("scroll", () => DropPhysical(5)),
                    new("move", () => DropPhysical(4)),
                    new("double click", () => DropPhysical(3)),
                    new("click", () => DropPhysical(2)),
                    new("up", () => DropPhysical(1)),
                    new("down", () => DropPhysical(0)),
                ]));
            _unfasten.CompleteOrThrow();
        }

        private void HurlIfDestroyed()
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _teardownAsked) is not 0,
                this);
        }

        private void OnDown(IMouse _, MouseButton btn) =>
            Invoke(() => _downSubscribers?.Invoke(this, btn));
        private void OnUp(IMouse _, MouseButton btn) =>
            Invoke(() => _upSubscribers?.Invoke(this, btn));
        private void OnClick(IMouse _, MouseButton btn, Vector2 locus) =>
            Invoke(() => _pressSubscribers?.Invoke(this, btn, locus));
        private void OnDoublePress(IMouse _, MouseButton btn, Vector2 locus) =>
            Invoke(() => _doublePressSubscribers?.Invoke(this, btn, locus));
        private void OnRelocate(IMouse _, Vector2 locus) =>
            Invoke(() => _relocateSubscribers?.Invoke(this, locus));
        private void OnRoll(IMouse _, ScrollWheel roll) =>
            Invoke(() => _rollSubscribers?.Invoke(this, roll));
        private void Invoke(Action hook)
        {
            _stillness.Invoke(() =>
            {
                if (Volatile.Read(ref _engaged) is not 0)
                    hook();
            });
        }

        private void Add<T>(ref T? subscribers, T val, int ordinal, Action fasten)
            where T : Delegate
        {
            HurlIfDestroyed();
            subscribers = (T?)Delegate.Combine(subscribers, val);
            if (_affixed[ordinal]) return;
            _affixed[ordinal] = true;
            fasten();
        }

        private void DropSubscriber<T>(
            ref T? subscribers,
            T val,
            int ordinal,
            Action unfasten)
            where T : Delegate
        {
            subscribers = (T?)Delegate.Remove(subscribers, val);
            if (subscribers is not null || !_affixed[ordinal]) return;
            unfasten();
            _affixed[ordinal] = false;
        }

        private void DropPhysical(int index)
        {
            if (!_affixed[index]) return;
            switch (index)
            {
                case 0: _interior.MouseDown -= _downRelay; _downSubscribers = null; break;
                case 1: _interior.MouseUp -= _upRelay; _upSubscribers = null; break;
                case 2: _interior.Click -= _pressRelay; _pressSubscribers = null; break;
                case 3: _interior.DoubleClick -= _doublePressRelay; _doublePressSubscribers = null; break;
                case 4: _interior.MouseMove -= _relocateRelay; _relocateSubscribers = null; break;
                case 5: _interior.Scroll -= _rollRelay; _rollSubscribers = null; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
            _affixed[index] = false;
        }
    }
}
