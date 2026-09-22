namespace MacAC.Cockpit.Panels.Settings;

/// <summary>A window arrangement together with the screen it was authored for, so it can be rescaled.</summary>
public readonly record struct UiWindowSpot(UiWindowArrangement Layout, int ScreenWidth, int ScreenHeight);
