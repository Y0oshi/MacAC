using MacAC.Cockpit.Input;

namespace MacAC.Client.Shell.Panels;

public interface IKnobRow
{
    bool Changed { get; }

    void PersistLatestVal();

    void ReinstateStoredVal();

    /// <summary><c>RestoreDefaultValue</c>: <c>m_current = m_default</c>, then applies the default live.</summary>
    void ReinstateDefaultVal();

    void FastenSheetAlert(Action alert);
}

public sealed class BoolKnobRow(
    bool starting,
    bool defaultVal,
    Action<bool>? enact = null,
    Func<bool>? scan = null,
    Action<bool>? renew = null) : IKnobRow
{
    private readonly Action<bool>? _enact = enact;
    private readonly Func<bool>? _scan = scan;
    private readonly Action<bool>? _renew = renew;
    private Action? _alertSheetKnobAltered;

    public bool Current { get; private set; } = starting;

    public bool Stored { get; private set; } = starting;

    public bool DefaultValue { get; private set; } = defaultVal;

    public bool Changed => Stored != Current;

    public void AssignDefaultVal(bool val) => DefaultValue = val;

    public void AssignLatestVal(bool val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal()
    {
        if (_scan is not null)
        {
            Current = _scan();
            _renew?.Invoke(Current);
        }
        Stored = Current;
    }

    public void ReinstateStoredVal()
    {
        Current = Stored;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class FloatKnobRow(
    float starting,
    float defaultVal,
    Action<float>? enact = null,
    Func<float>? scan = null,
    Action<float>? renew = null) : IKnobRow
{
    private readonly Action<float>? _enact = enact;
    private readonly Func<float>? _scan = scan;
    private readonly Action<float>? _renew = renew;
    private Action? _alertSheetKnobAltered;

    public float Current { get; private set; } = starting;

    public float Saved { get; private set; } = starting;

    public float DefaultValue { get; private set; } = defaultVal;

    public bool Changed => Saved != Current;

    public void ApplyDefaultVal(float val) => DefaultValue = val;

    public void ApplyLatestVal(float val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void RenewFromConnect(float val)
    {
        Current = val;
        _renew?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal()
    {
        if (_scan is not null)
        {
            Current = _scan();
            _renew?.Invoke(Current);
        }
        Saved = Current;
    }

    public void ReinstateStoredVal()
    {
        Current = Saved;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class IntKnobRow(
    int starting,
    int defaultVal,
    Action<int>? enact = null,
    Func<int>? scan = null,
    Action<int>? renew = null) : IKnobRow
{
    private readonly Action<int>? _enact = enact;
    private readonly Func<int>? _scan = scan;
    private readonly Action<int>? _renew = renew;
    private Action? _alertSheetKnobAltered;

    public int Current { get; private set; } = starting;

    public int Saved { get; private set; } = starting;

    public int DefaultValue { get; private set; } = defaultVal;

    public bool Changed => Saved != Current;

    public void AssignDefaultValue(int val) => DefaultValue = val;

    public void AssignCurrentValue(int val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal()
    {
        if (_scan is not null)
        {
            Current = _scan();
            _renew?.Invoke(Current);
        }
        Saved = Current;
    }

    public void ReinstateStoredVal()
    {
        Current = Saved;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class StringKnobRow(
    string starting,
    string defaultVal,
    Action<string>? enact = null,
    Func<string>? scan = null,
    Action<string>? renew = null) : IKnobRow
{
    private readonly Action<string>? _enact = enact;
    private readonly Func<string>? _scan = scan;
    private readonly Action<string>? _renew = renew;
    private Action? _alertSheetKnobAltered;

    public string Current { get; private set; } = starting;
    public string Saved { get; private set; } = starting;
    public string DefaultValue { get; private set; } = defaultVal;
    public bool Changed => Saved != Current;

    public void SetDefaultVal(string val) => DefaultValue = val;

    public void SetLatestValue(string val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal()
    {
        if (_scan is not null)
        {
            Current = _scan();
            _renew?.Invoke(Current);
        }
        Saved = Current;
    }

    public void ReinstateStoredVal()
    {
        Current = Saved;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class BitfieldKnobRow(
    ulong starting,
    ulong defaultVal,
    Action<ulong>? enact = null,
    Func<ulong>? scan = null,
    Action<ulong>? renew = null) : IKnobRow
{
    private readonly Action<ulong>? _enact = enact;
    private readonly Func<ulong>? _scan = scan;
    private readonly Action<ulong>? _renew = renew;
    private Action? _alertSheetKnobAltered;

    public ulong Current { get; private set; } = starting;
    public ulong Saved { get; private set; } = starting;
    public ulong DefaultValue { get; private set; } = defaultVal;
    public bool Changed => Saved != Current;

    public void SetDefaultValue(ulong val) => DefaultValue = val;

    public void SetCurrentVal(ulong val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal()
    {
        if (_scan is not null)
        {
            Current = _scan();
            _renew?.Invoke(Current);
        }
        Saved = Current;
    }

    public void ReinstateStoredVal()
    {
        Current = Saved;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class ActionKeyMapKnobRow(
    IReadOnlyList<KeyStroke> starting,
    IReadOnlyList<KeyStroke> defaultVal,
    Action<IReadOnlyList<KeyStroke>>? enact = null) : IKnobRow
{
    private readonly Action<IReadOnlyList<KeyStroke>>? _enact = enact;
    private Action? _alertSheetKnobAltered;

    public IReadOnlyList<KeyStroke> Current { get; private set; } = starting;

    public IReadOnlyList<KeyStroke> Saved { get; private set; } = starting;

    public IReadOnlyList<KeyStroke> DefaultValue { get; private set; } = defaultVal;

    public bool Changed => !Current.SequenceEqual(Saved);

    public void SetDefaultValue(IReadOnlyList<KeyStroke> val) => DefaultValue = val;

    public void SetCurrentValue(IReadOnlyList<KeyStroke> val)
    {
        Current = val;
        _enact?.Invoke(val);
        _alertSheetKnobAltered?.Invoke();
    }

    public void FastenSheetAlert(Action alert) => _alertSheetKnobAltered = alert;

    public void PersistLatestVal() => Saved = Current;

    public void ReloadLatestAndStored(IReadOnlyList<KeyStroke> val)
    {
        Current = val;
        Saved = val;
        _alertSheetKnobAltered?.Invoke();
    }

    public void ReinstateStoredVal()
    {
        Current = Saved;
        _enact?.Invoke(Current);
    }

    public void ReinstateDefaultVal()
    {
        Current = DefaultValue;
        _enact?.Invoke(Current);
    }
}

public sealed class KnobPage
{
    private readonly List<IKnobRow> _ranks = [];

    public IReadOnlyList<IKnobRow> Rows => _ranks;

    public Action? FollowingEnact { get; set; }

    public Action? OnKnobAltered { get; set; }

    public void Register(IKnobRow rank)
    {
        ArgumentNullException.ThrowIfNull(rank);
        rank.FastenSheetAlert(() => OnKnobAltered?.Invoke());
        _ranks.Add(rank);
    }

    public void DropRear(int retainedRowCount)
    {
        if (retainedRowCount < 0 || retainedRowCount > _ranks.Count)
            throw new ArgumentOutOfRangeException(nameof(retainedRowCount));
        for (int idx = _ranks.Count - 1; idx >= retainedRowCount; --idx)
        {
            _ranks[idx].FastenSheetAlert(static () => { });
            _ranks.RemoveAt(idx);
        }
        OnKnobAltered?.Invoke();
    }

    public bool Changed => _ranks.Any(static rank => rank.Changed);

    public void Apply()
    {
        foreach (IKnobRow rank in _ranks.ToArray())
            if (_ranks.Contains(rank))
                rank.PersistLatestVal();
        FollowingEnact?.Invoke();
        OnKnobAltered?.Invoke();
    }

    public void Reset()
    {
        foreach (IKnobRow rank in _ranks.Where(static row => row.Changed).ToArray())
            if (_ranks.Contains(rank))
                rank.ReinstateStoredVal();
        OnKnobAltered?.Invoke();
    }

    public void Defaults()
    {
        foreach (IKnobRow rank in _ranks.ToArray())
            if (_ranks.Contains(rank))
                rank.ReinstateDefaultVal();
        OnKnobAltered?.Invoke();
    }

    public void OnShown() => Apply();

    public void ReloadFromOnline()
    {
        foreach (IKnobRow rank in _ranks.ToArray())
            if (_ranks.Contains(rank))
                rank.PersistLatestVal();
        OnKnobAltered?.Invoke();
    }

    public void OnHidden() => Reset();
}
