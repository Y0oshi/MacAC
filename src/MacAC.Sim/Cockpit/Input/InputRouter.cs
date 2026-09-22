using Silk.NET.Input;

namespace MacAC.Cockpit.Input;

public sealed partial class InputRouter : IDisposable
{
    private const long DoublePressThresholdMsec = 500;
    private const float PressPullThresholdPx = 3f;

    private readonly IKeyFeed _keyboard;
    private readonly IMouseFeed _pointer;
    private readonly Func<long> _beatMsec;
    private readonly Hooks _taps;
    private readonly Stack<InputLayer> _scopes = new();
    private readonly HashSet<KeyStroke> _pinnedChords = [];
    private readonly HashSet<FeedAct> _automationPinned = [];
    private readonly Dictionary<MouseButton, float> _pressTravel = new();
    private KeyBindingBook _bindings;
    private InputLayer? _fightingAmbit;
    private bool _alternateCam;
    private int _teardownAsked;
    private int _engaged;

    private MouseButton? _previousDownBtn;
    private long _previousDownAtMsec;

    private Action<KeyStroke>? _grab;
    private Key? _grabModifier;

    public event Action<FeedAct, ActivationKind>? Fired;

    /// <summary>The keyboard chord being dispatched right now, for handlers that want the physical key.</summary>
    public KeyStroke? LatestPhysicalChord { get; private set; }

    private InputRouter(IKeyFeed keyboard, IMouseFeed mouse, KeyBindingBook bindings, Func<long> getTickCount64)
    {
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _pointer = mouse ?? throw new ArgumentNullException(nameof(mouse));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _beatMsec = getTickCount64 ?? throw new ArgumentNullException(nameof(getTickCount64));
        _taps = new Hooks(this);

        _scopes.Push(InputLayer.Always);
        _scopes.Push(InputLayer.Game);
    }

    public static InputRouter MakeDetached(IKeyFeed keyboard, IMouseFeed pointer, KeyBindingBook mappings) =>
        new(keyboard, pointer, mappings, static () => Environment.TickCount64);

    public void Attach()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _teardownAsked) is not 0, this);
        if (_fastenBegun)
            throw new InvalidOperationException("Input dispatcher attachment has by now started");
        _fastenBegun = true;

        try
        {
            _taps.FastenAll();
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Deactivate();
            var undo = _taps.UnfastenAll();
            if (undo.Count is not 0)
            {
                undo.Insert(0, new InvalidOperationException("Input dispatcher source registration failed", fastenProblem));
                throw new AggregateException("Input dispatcher registration and rollback both failed", undo);
            }
            throw new InvalidOperationException("Input dispatcher registration failed and was rolled back", fastenProblem);
        }
    }

    private bool Live => Volatile.Read(ref _engaged) is not 0;

    public bool IsDisposalComplete => _taps.NoneAffixed;

    public KeyBindingBook Bindings => _bindings;

    public bool IsCapturing => _grab is not null;

    private sealed class Hooks(InputRouter holder)
    {
        private readonly bool[] _on = new bool[6];

        public bool NoneAffixed => Array.TrueForAll(_on, static on => !on);

        public void FastenAll()
        {
            Hook(0, () => holder._keyboard.KeyDown += holder.OnTagDown);
            Hook(1, () => holder._keyboard.KeyUp += holder.OnTagUp);
            Hook(2, () => holder._pointer.MouseDown += holder.OnPointerDown);
            Hook(3, () => holder._pointer.MouseUp += holder.OnPointerUp);
            Hook(4, () => holder._pointer.MouseMove += holder.OnPointerRelocate);
            Hook(5, () => holder._pointer.Scroll += holder.OnRoll);
        }

        // Unhooks in reverse order; returns the failures instead of stopping at the first
        public List<Exception> UnfastenAll()
        {
            List<Exception> misses = new List<Exception>();
            for (int index = _on.Length - 1; index >= 0; --index)
            {
                if (!_on[index])
                    continue;
                try
                {
                    switch (index)
                    {
                        case 0: holder._keyboard.KeyDown -= holder.OnTagDown; break;
                        case 1: holder._keyboard.KeyUp -= holder.OnTagUp; break;
                        case 2: holder._pointer.MouseDown -= holder.OnPointerDown; break;
                        case 3: holder._pointer.MouseUp -= holder.OnPointerUp; break;
                        case 4: holder._pointer.MouseMove -= holder.OnPointerRelocate; break;
                        case 5: holder._pointer.Scroll -= holder.OnRoll; break;
                        default: throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    _on[index] = false;
                }
                catch (Exception problem)
                {
                    misses.Add(new InvalidOperationException($"Input dispatcher source callback {index} could not be detached", problem));
                }
            }
            return misses;
        }

        private void Hook(int ordinal, Action enlist)
        {
            _on[ordinal] = true;
            enlist();
        }
    }

    private bool _fastenBegun;

    /// <summary>Stops dispatching and forgets every transient state without unhooking the sources.</summary>
    public void Deactivate()
    {
        Interlocked.Exchange(ref _engaged, 0);
        _grab = null;
        _grabModifier = null;
        _pinnedChords.Clear();
        _automationPinned.Clear();
        _pressTravel.Clear();
        _alternateCam = false;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Deactivate();
        var misses = _taps.UnfastenAll();
        if (misses.Count is not 0)
            throw new AggregateException("One or more input dispatcher source callbacks could not be detached", misses);
    }

    public bool TryInvokeAutomationAct(FeedAct act)
    {
        Vet(act);
        if (!AcceptsSynthetic)
            return false;
        Fired?.Invoke(act, ActivationKind.Press);
        return true;
    }

    public bool TrySetAutomationActPinned(FeedAct act, bool pinned)
    {
        Vet(act);
        if (!Live)
            return false;

        if (!pinned)
        {
            if (_automationPinned.Remove(act))
                Fired?.Invoke(act, ActivationKind.Release);
            return true;
        }

        if (_grab is not null || _pointer.WantCaptureKeyboard)
            return false;
        if (_automationPinned.Add(act))
            Fired?.Invoke(act, ActivationKind.Press);
        return true;
    }

    // True when the router would accept a synthetic press right now
    private bool AcceptsSynthetic => Live && _grab is null && !_pointer.WantCaptureKeyboard;

    public void ApplyCamAlternateAmbit(bool engaged)
    {
        if (_alternateCam == engaged)
            return;
        FreePinned();
        _alternateCam = engaged;
    }

    public void ApplyFightingAmbit(InputLayer? scope)
    {
        if (scope is not null and not (InputLayer.MeleeCombat or InputLayer.MissileCombat or InputLayer.MagicCombat))
            throw new ArgumentOutOfRangeException(nameof(scope));
        if (_fightingAmbit == scope)
            return;
        FreePinned();
        _fightingAmbit = scope;
    }

    public InputLayer EngagedAmbit
    {
        get
        {
            return _alternateCam ? InputLayer.Camera
        : _scopes.Peek() == InputLayer.Game && _fightingAmbit is { } fighting ? fighting
        : _scopes.Peek();
        }
    }

    public void PushAmbit(InputLayer ambit)
    {
        FreePinned();
        _scopes.Push(ambit);
    }

    public void PopScope(InputLayer anticipated)
    {
        if (_scopes.Peek() != anticipated)
            throw new InvalidOperationException($"PopScope wanted {anticipated} but top is {_scopes.Peek()}");
        FreePinned();
        _scopes.Pop();
    }

    public void AssignMappings(KeyBindingBook mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        FreePinned();
        _bindings = mappings;
    }

    public void Tick()
    {
        if (!Live || _pointer.WantCaptureKeyboard || _pinnedChords.Count is 0)
            return;

        KeyStroke[] pinned = [.. _pinnedChords];
        foreach (KeyStroke chord in pinned)
        {
            if (!_pinnedChords.Contains(chord))
                continue;
            foreach (CockpitBinding grip in Resolve(chord, ActivationKind.Hold))
                Fired?.Invoke(grip.Action, ActivationKind.Hold);
        }
    }

    public void CommenceGrab(Action<KeyStroke> onCaptured)
    {
        _grab = onCaptured ?? throw new ArgumentNullException(nameof(onCaptured));
        _grabModifier = null;
    }

    public void AbortGrab() => FinishGrab(default);

    internal static InputRouter BuildDetached(IKeyFeed keyboard, IMouseFeed pointer, KeyBindingBook mappings, Func<long> fetchBeatCount64) =>
        new(keyboard, pointer, mappings, fetchBeatCount64);

    private static void Vet(FeedAct action)
    {
        if (action == FeedAct.None || !Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));
    }

    // Any scope or binding change ends every hold in flight, releasing what those chords resolve to
    // right now
    private void FreePinned()
    {
        if (_pinnedChords.Count is 0)
            return;
        List<CockpitBinding> releases = new List<CockpitBinding>(_pinnedChords.Count);
        foreach (KeyStroke chord in _pinnedChords)
            releases.AddRange(Resolve(chord, ActivationKind.Hold));
        _pinnedChords.Clear();
        foreach (CockpitBinding mapping in releases)
            Fired?.Invoke(mapping.Action, ActivationKind.Release);
    }

    // Hands the chord to the pending capture callback and leaves capture mode; a no-op without one
    private void FinishGrab(KeyStroke chord)
    {
        var hook = _grab;
        if (hook is null)
            return;
        _grab = null;
        _grabModifier = null;
        hook(chord);
    }
}
