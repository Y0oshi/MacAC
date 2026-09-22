using MacAC.Extensibility.World;

namespace MacAC.Mechanics.Targeting;

public enum PickChangeSource
{
    System,
    World,
    Radar,
    Inventory,
    ExternalContainer,
    Paperdoll,
    Toolbar,
    Keyboard,
    Plugin,
    Vendor,
    Social,
}

public enum PickChangeReason
{
    Selected,
    Cleared,
    SelectedObjectRemoved,
    CombatTargetDied,
    PreviousSelection,
    SessionReset,
}

public readonly record struct PickShift(
    uint? PreviousObjectId,
    uint? SelectedObjectId,
    PickChangeSource Source,
    PickChangeReason Reason);

public sealed class PickPhase : ITargetPicker
{
    private Action<TargetChange>? _extensionWatchers;

    public uint? ChosenObjectTag { get; private set; }

    public uint? EarlierObjectIdent { get; private set; }

    public uint? EarlierValidObjectIdent { get; private set; }

    public event Action<PickShift>? Changed;

    event Action<TargetChange> ITargetPicker.Changed
    {
        add => _extensionWatchers += value;
        remove => _extensionWatchers -= value;
    }

    public bool Select(
        uint objectIdent,
        PickChangeSource src,
        PickChangeReason cause = PickChangeReason.Selected) =>
        objectIdent is 0 ? Clear(src) : Move(objectIdent, src, cause);

    public bool Clear(
        PickChangeSource src,
        PickChangeReason cause = PickChangeReason.Cleared) =>
        Move(null, src, cause);

    public bool PickEarlier(PickChangeSource src = PickChangeSource.Keyboard)
    {
        return EarlierObjectIdent is { } earlier
        && Move(earlier, src, PickChangeReason.PreviousSelection);
    }

    public bool Reset(PickChangeSource src = PickChangeSource.System)
    {
        uint? was = ChosenObjectTag;
        bool anything = was is not null || EarlierObjectIdent is not null || EarlierValidObjectIdent is not null;

        ChosenObjectTag = null;
        EarlierObjectIdent = null;
        EarlierValidObjectIdent = null;

        var misses = TellHubWatchers(new PickShift(was, null, src, PickChangeReason.SessionReset));
        TellExtensionWatchers(new TargetChange(was, null));
        if (misses is not null)
            throw new AggregateException("One or more selection reset observers failed", misses);
        return anything;
    }

    bool ITargetPicker.Select(uint objectIdent) => Select(objectIdent, PickChangeSource.Plugin);

    bool ITargetPicker.Clear() => Clear(PickChangeSource.Plugin);

    private bool Move(uint? upcoming, PickChangeSource src, PickChangeReason cause)
    {
        if (ChosenObjectTag == upcoming)
            return false;

        uint? was = ChosenObjectTag;
        ChosenObjectTag = upcoming;
        EarlierObjectIdent = was;
        if (was is not null)
            EarlierValidObjectIdent = was;

        Changed?.Invoke(new PickShift(was, upcoming, src, cause));
        TellExtensionWatchers(new TargetChange(was, upcoming));
        return true;
    }

    private List<Exception>? TellHubWatchers(PickShift shift)
    {
        if (Changed is not { } watchers)
            return null;

        List<Exception>? misses = null;
        foreach (Action<PickShift> watcher in watchers.GetInvocationList())
        {
            try
            {
                watcher(shift);
            }
            catch (Exception problem)
            {
                (misses ??= []).Add(problem);
            }
        }
        return misses;
    }

    private void TellExtensionWatchers(TargetChange edit)
    {
        if (_extensionWatchers is not { } watchers)
            return;
        foreach (Action<TargetChange> watcher in watchers.GetInvocationList())
        {
            try
            {
                watcher(edit);
            }
            catch
            {
                // An extension's observer must never take the host down
            }
        }
    }
}
