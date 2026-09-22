namespace MacAC.Client.Link;

using MacAC.Sim;

// One named host operation around Runtime's generation reset
internal sealed record OnlineSessionResetStage(
    string Name,
    Action<SimEpochTicket> Reset)
{
    public OnlineSessionResetStage(string label, Action reset)
        : this(
            label,
            _ => (reset ?? throw new ArgumentNullException(nameof(reset)))())
    {
    }
}

internal sealed class OnlineSessionResetPlan
{
    private readonly OnlineSessionResetStage[] _junctures;
    private int _executing;

    public OnlineSessionResetPlan(IEnumerable<OnlineSessionResetStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _junctures = [.. stages];
        HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnlineSessionResetStage juncture in _junctures)
        {
            ArgumentNullException.ThrowIfNull(juncture);
            if (string.IsNullOrWhiteSpace(juncture.Name))
                throw new ArgumentException("Reset stage names has to be non-empty", nameof(stages));
            ArgumentNullException.ThrowIfNull(juncture.Reset);
            if (!labels.Add(juncture.Name))
                throw new ArgumentException(
                    $"Duplicate live-session reset stage '{juncture.Name}'.",
                    nameof(stages));
        }
    }

    public IReadOnlyList<string> JunctureLabels =>
        Array.ConvertAll(_junctures, static juncture => juncture.Name);

    public void Execute()
        => Execute(default);

    public void Execute(SimEpochTicket sunsettingGen)
    {
        if (Interlocked.Exchange(ref _executing, 1) is not 0)
            throw new InvalidOperationException(
                "Live-session reset can't run concurrently or reentrantly");

        List<Exception>? misses = null;
        try
        {
            foreach (OnlineSessionResetStage juncture in _junctures)
            {
                try
                {
                    juncture.Reset(sunsettingGen);
                }
                catch (Exception problem)
                {
                    (misses ??= []).Add(new OnlineSessionResetStageException(
                        juncture.Name,
                        problem));
                }
            }
        }
        finally
        {
            Volatile.Write(ref _executing, 0);
        }

        if (misses is not null)
            throw new AggregateException(
                "Live-session state didn't converge; a new session must not start",
                misses);
    }
}

internal sealed class OnlineSessionResetStageException(
    string junctureLabel,
    Exception interiorException) : Exception(
        $"Live-session reset stage '{junctureLabel}' failed.",
        interiorException)
{
    public string JunctureLabel { get; } = junctureLabel;
}
