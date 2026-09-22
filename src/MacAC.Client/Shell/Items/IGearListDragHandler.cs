namespace MacAC.Client.Shell;

public enum GearDragAcceptance
{
    None,
    Accept,
    Reject,
}

public interface IGearListDragHandler
{
    void OnPullLift(WidgetGearRoster srcRoster, WidgetGearSlot srcChamber, GearDragPayload cargo);

    GearDragAcceptance OnPullOver(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo);

    void ProcessDiscardFree(WidgetGearRoster markRoster, WidgetGearSlot markChamber, GearDragPayload cargo);
}
