namespace MacAC.Mechanics.Drawing;

/// <summary>Camera tuning knobs, seeded from MACAC_* variables at start-up.</summary>
public static class CameraTelemetry
{
    public static bool UseCanonPursueCam { get; set; } = Switch("MACAC_RETAIL_CHASE");

    public static bool AlignToSlope { get; set; } = Switch("MACAC_CAMERA_ALIGN_SLOPE");

    public static bool CollideCam { get; set; } = Switch("MACAC_CAMERA_COLLIDE");

    public static float TranslationStiffness { get; set; } = 0.45f;

    public static float SpinStiffness { get; set; } = 0.45f;

    public static float PointerLoPassPaneSec { get; set; } = 0.25f;

    public static float CamAdjustmentPace { get; set; } = 40.0f;

    // On unless the variable is set to exactly "0"
    private static bool Switch(string variable) => Environment.GetEnvironmentVariable(variable) != "0";
}
