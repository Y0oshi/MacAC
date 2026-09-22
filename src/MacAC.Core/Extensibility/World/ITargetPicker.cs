namespace MacAC.Extensibility.World;

public readonly record struct TargetChange(
    uint? PreviousObjectId,
    uint? SelectedObjectId);

/// <summary>The player's current selection, readable and settable.</summary>
public interface ITargetPicker
{
    uint? ChosenObjectTag { get; }

    uint? EarlierObjectIdent { get; }

    event Action<TargetChange> Changed;

    bool Select(uint objectIdent);

    bool Clear();
}
