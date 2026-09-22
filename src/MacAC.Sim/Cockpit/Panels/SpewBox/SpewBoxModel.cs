using MacAC.Mechanics.Comms;

namespace MacAC.Cockpit.Panels.SpewBox;

public readonly record struct SpewLine(string Text, double RemainingLifetimeSeconds);

public sealed class SpewBoxModel(SpewPaneState state)
{
    private readonly SpewPaneState _phase = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>Bumps whenever the visible set changes.</summary>
    public long Revision => _phase.Revision;

    public bool HasShownStrokes => _phase.Count > 0;

    public void Clear() => _phase.Reset();

    public IReadOnlyList<SpewLine> Strokes(double instantSecs)
    {
        _phase.Tick(instantSecs);
        SpewRow[] ranks = _phase.Snapshot();
        if (ranks.Length is 0)
            return [];
        return Array.ConvertAll(ranks, rank => new SpewLine(rank.Text, Math.Max(0d, rank.ExpiresAtSeconds - instantSecs)));
    }
}
