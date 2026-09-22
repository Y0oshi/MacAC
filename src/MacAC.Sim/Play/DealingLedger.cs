namespace MacAC.Sim.Play;

public enum DealingModeKind
{
    None,
    Use,
    Examine,
    UseItemOnTarget,
}

/// <summary>The current interaction mode; the source object only matters for use-item-on-target.</summary>
public readonly record struct DealingMode(DealingModeKind Kind, uint SourceObjectId = 0)
{
    public static DealingMode None => new(DealingModeKind.None);
}

public readonly record struct DealingModeShift(DealingMode Previous, DealingMode Current);

public sealed class DealingLedger
{
    public DealingMode Current { get; private set; } = DealingMode.None;

    public event Action<DealingModeShift>? Changed;

    public bool JoinUse() => Shift(new DealingMode(DealingModeKind.Use));

    public bool JoinExamine() => Shift(new DealingMode(DealingModeKind.Examine));

    public bool JoinUseGearOnMark(uint sourceObjectId)
    {
        if (sourceObjectId is 0)
            throw new ArgumentOutOfRangeException(nameof(sourceObjectId));
        return Shift(new DealingMode(DealingModeKind.UseItemOnTarget, sourceObjectId));
    }

    public bool Clear() => Shift(DealingMode.None);

    /// <summary>Forces the mode back to none, telling every listener even if some of them throw.</summary>
    public void ResetSession()
    {
        DealingMode earlier = Current;
        Current = DealingMode.None;
        if (Changed is not { } listeners)
            return;

        DealingModeShift shift = new DealingModeShift(earlier, DealingMode.None);
        ResetSessionRest(listeners, shift);
    }

    private void ResetSessionRest(Action<DealingModeShift> listeners, DealingModeShift shift)
    {
        List<Exception>? misses = null;
        foreach (Action<DealingModeShift> listener in listeners.GetInvocationList())
        {
            try
            {
                listener(shift);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        if (misses is not null)
            throw new AggregateException("One or more interaction-mode reset observers failed", misses);
    }

    private bool Shift(DealingMode manner)
    {
        if (Current == manner)
            return false;
        DealingMode earlier = Current;
        Current = manner;
        Changed?.Invoke(new DealingModeShift(earlier, manner));
        return true;
    }
}
