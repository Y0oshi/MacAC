namespace MacAC.Client.Graphics;

/// <summary>
/// One begin/end pair a frame opens: what to close it with when the frame completes, and what to
/// close it with when the frame fails part-way. A scope that was never entered is never closed.
/// </summary>
internal readonly record struct FrameScope(string Name, Action Leave, Action Unwind)
{
    public static FrameScope Named(string name, Action leave, Action unwind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(leave);
        ArgumentNullException.ThrowIfNull(unwind);
        return new FrameScope(name, leave, unwind);
    }
}

/// <summary>
/// The scopes a frame has opened, in the order it opened them. A frame that throws part-way has to
/// close exactly the ones it opened, in reverse, and report every closing failure alongside the
/// original — leaving one open poisons every later frame. The stack is the record of what was
/// entered, so an unwind cannot disagree with it.
/// </summary>
internal sealed class FrameScopeStack
{
    private readonly List<FrameScope> _entered = [];

    public int Depth => _entered.Count;

    public IEnumerable<string> EnteredNames => _entered.Select(static scope => scope.Name);

    /// <summary>Runs <paramref name="enter"/> and, only if it returns, records the scope as open.</summary>
    public void Enter(string name, Action enter, Action leave, Action unwind)
    {
        ArgumentNullException.ThrowIfNull(enter);
        var scope = FrameScope.Named(name, leave, unwind);
        enter();
        _entered.Add(scope);
    }

    /// <summary>Records an already-opened scope, for a begin that happened before the stack saw it.</summary>
    public void Adopt(string name, Action leave, Action unwind) =>
        _entered.Add(FrameScope.Named(name, leave, unwind));

    /// <summary>
    /// Closes every open scope in reverse order on the way out of a good frame. The first failure
    /// propagates, and the scopes below it are still closed.
    /// </summary>
    public void LeaveAll()
    {
        var failures = Close(static scope => scope.Leave);
        if (failures is { Count: 1 })
            throw failures[0];
        if (failures is { Count: > 1 })
            throw new AggregateException("Closing the frame's scopes failed.", failures);
    }

    /// <summary>
    /// Closes every open scope in reverse order after a failure, and reports what each closing
    /// failure was. Nothing is thrown: the caller owns the original failure and decides.
    /// </summary>
    public List<Exception>? UnwindAll() => Close(static scope => scope.Unwind);

    private List<Exception>? Close(Func<FrameScope, Action> pick)
    {
        List<Exception>? failures = null;
        for (int idx = _entered.Count - 1; idx >= 0; --idx)
        {
            try
            {
                pick(_entered[idx])();
            }
            catch (Exception problem)
            {
                (failures ??= []).Add(problem);
            }
        }

        _entered.Clear();
        return failures;
    }
}
