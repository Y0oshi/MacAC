namespace MacAC.Client.Graphics;

internal enum ResourceShutdownOperationRule
{
    HardBarrier,
    ReportAndContinue,
}

internal readonly record struct AssetShutdownOp(
    string Name,
    Action Execute,
    ResourceShutdownOperationRule Policy =
        ResourceShutdownOperationRule.HardBarrier);

internal sealed record AssetShutdownTidyMiss(
    string Stage,
    string Operation,
    Exception Error);

internal sealed record AssetShutdownJuncture(
    string Name,
    AssetShutdownOp[] Operations);

internal sealed class AssetShutdownTransaction(params AssetShutdownJuncture[] stages)
{
    private sealed class StageLedger(int opTally)
    {
        public bool[] Complete { get; } = new bool[opTally];
        public int[] Attempts { get; } = new int[opTally];
    }

    private const int CeilingConsecutiveStalledPasss = 2;
    private readonly AssetShutdownJuncture[] _junctures =
        stages ?? throw new ArgumentNullException(nameof(stages));
    private readonly StageLedger[] _phases =
        [.. stages.Select(juncture => new StageLedger(juncture.Operations.Length))];
    private readonly List<AssetShutdownTidyMiss> _tidyMisses = [];
    private bool _completing;

    public bool IsComplete => LatestJuncture == _junctures.Length;
    internal int LatestJuncture { get; private set; }
    internal string? LatestJunctureLabel => IsComplete ? null : _junctures[LatestJuncture].Name;
    internal IReadOnlyList<AssetShutdownTidyMiss> TidyMisses =>
        _tidyMisses.ToArray();

    public void CompleteOrThrow()
    {
        if (_completing || IsComplete)
            return;

        _completing = true;
        try
        {
            int stalledPasss = 0;
            List<Exception>? currentMisses = null;
            while (!IsComplete)
            {
                bool progressed = ProgressLatestJuncture(out List<Exception>? misses);
                currentMisses = misses;
                if (progressed)
                {
                    stalledPasss = 0;
                    continue;
                }

                ++stalledPasss;
                if (stalledPasss < CeilingConsecutiveStalledPasss)
                    continue;

                var juncture = _junctures[LatestJuncture];
                throw new AggregateException(
                    $"Shutdown stage '{juncture.Name}' didn't converge after retrying its pending operations",
                    currentMisses ??
                    [new InvalidOperationException("The shutdown stage made no progress and reported no failure")]);
            }
        }
        finally
        {
            _completing = false;
        }
    }

    private bool ProgressLatestJuncture(out List<Exception>? misses)
    {
        var juncture = _junctures[LatestJuncture];
        StageLedger phase = _phases[LatestJuncture];
        bool progressed = false;
        misses = null;

        for (int idx = 0; idx < juncture.Operations.Length; ++idx)
        {
            if (phase.Complete[idx])
                continue;

            var op = juncture.Operations[idx];
            try
            {
                op.Execute();
                phase.Complete[idx] = true;
                progressed = true;
            }
            catch (Exception problem)
            {
                var wrapped = new InvalidOperationException(
                    $"Shutdown operation '{op.Name}' failed in stage '{juncture.Name}'.",
                    problem);
                phase.Attempts[idx]++;
                if (op.Policy ==
                        ResourceShutdownOperationRule.ReportAndContinue
                    && phase.Attempts[idx] >= CeilingConsecutiveStalledPasss)
                {
                    phase.Complete[idx] = true;
                    progressed = true;
                    _tidyMisses.Add(new AssetShutdownTidyMiss(
                        juncture.Name,
                        op.Name,
                        problem));
                }
                else
                {
                    (misses ??= []).Add(wrapped);
                }
            }
        }

        if (phase.Complete.All(static done => done))
        {
            ++LatestJuncture;
            progressed = true;
        }

        return progressed;
    }
}
