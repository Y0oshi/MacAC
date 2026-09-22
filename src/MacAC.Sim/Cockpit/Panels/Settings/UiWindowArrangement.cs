namespace MacAC.Cockpit.Panels.Settings;

public readonly record struct UiWindowArrangement(
    float X,
    float Y,
    float Width,
    float Height,
    bool Visible,
    bool Collapsed,
    bool Maximized,
    int AuthoredGeometryRevision = 0);
