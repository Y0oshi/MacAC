namespace MacAC.Cockpit.Panels.Settings;

public sealed record CameraTurnSettings(
    float Stiffness,
    float AdjustmentSpeed,
    float MouseLookSensitivity,
    bool AlignToSlope,
    bool InvertMouseLookYAxis,
    bool UseMouseTurning = false)
{
    public static CameraTurnSettings Default { get; } = new(
        Stiffness: 0.45f,
        AdjustmentSpeed: 40.0f,
        MouseLookSensitivity: 0.55f,
        AlignToSlope: true,
        InvertMouseLookYAxis: false);

    /// <summary>What the mouse-turning macro (<c>SetMouseTurningDefaults</c>) dials in.</summary>
    public static CameraTurnSettings PointerTurningMark { get; } = new(
        Stiffness: 0.95f,
        AdjustmentSpeed: 50.0f,
        MouseLookSensitivity: 0.7f,
        AlignToSlope: false,
        InvertMouseLookYAxis: true);
}
