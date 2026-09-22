using System.Globalization;

namespace MacAC.Mechanics.Comms;

/// <summary>Notices the options panel prints when mouse-turning resets a setting.</summary>
public static class OptionsPaneText
{
    public const string AlignToSlopeAltered =
        "Align To Slope was changed from TRUE to the mouse turning default of FALSE.";

    public const string InvertPointerGazeAxesAltered =
        "Invert Mouselook Axes was changed from FALSE to the mouse turning default of TRUE.";

    public const string PivotToFaceCamAltered =
        "Turn to Face Camera was changed from FALSE to the mouse turning default of TRUE.";

    public const string SupportUrl = "http://support.turbine.com/ics/support/ticketnewwizard.asp?style=classic";

    public static string CamStiffnessAltered(float from, float to) => Reset("Camera Stiffness", from, to);

    public static string CamAdjustmentAltered(float from, float to) => Reset("Camera Adjustment", from, to);

    public static string PointerSensitivityAltered(float from, float to) => Reset("Mouse Sensitivity", from, to);

    public static string UrgentAssistanceUnavailable => BrowserFailed("an urgent assistance request");

    public static string DossierAbuseUnavailable => BrowserFailed("an abuse report");

    private static string Reset(string setting, float from, float to)
    {
        return $"{setting} was changed from {Six(from)} to the mouse turning default of {Six(to)}.";
    }

    private static string Six(float val) => val.ToString("F6", CultureInfo.InvariantCulture);

    private static string BrowserFailed(string purpose)
    {
        return "An error occurred while trying to launch your web browser.\n"
        + $"The web site to submit {purpose} is listed below. "
        + $"Please go there to complete your request.\n{SupportUrl}";
    }
}
