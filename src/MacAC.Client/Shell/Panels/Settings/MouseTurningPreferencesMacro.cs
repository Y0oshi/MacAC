using MacAC.Cockpit.Panels.Settings;
using MacAC.Mechanics.Comms;

namespace MacAC.Client.Shell.Panels;

public static class MouseTurningPreferencesMacro
{
    public readonly record struct ResultDef(
        CameraTurnSettings Updated,
        bool UseMouseTurningChanged,
        IReadOnlyList<string> ChatLines);

    public static ResultDef Compute(CameraTurnSettings latest, bool usePointerTurningLatest)
    {
        var mark = CameraTurnSettings.PointerTurningMark;
        List<string> strokes = new List<string>();

        float stiffness = latest.Stiffness;
        if (stiffness != mark.Stiffness)
        {
            strokes.Add(OptionsPaneText.CamStiffnessAltered(stiffness, mark.Stiffness));
            stiffness = mark.Stiffness;
        }

        float adjustmentPace = latest.AdjustmentSpeed;
        if (adjustmentPace != mark.AdjustmentSpeed)
        {
            strokes.Add(OptionsPaneText.CamAdjustmentAltered(adjustmentPace, mark.AdjustmentSpeed));
            adjustmentPace = mark.AdjustmentSpeed;
        }

        float sensitivity = latest.MouseLookSensitivity;
        if (sensitivity != mark.MouseLookSensitivity)
        {
            strokes.Add(OptionsPaneText.PointerSensitivityAltered(sensitivity, mark.MouseLookSensitivity));
            sensitivity = mark.MouseLookSensitivity;
        }

        bool alignToSlope = latest.AlignToSlope;
        if (alignToSlope != mark.AlignToSlope)
        {
            strokes.Add(OptionsPaneText.AlignToSlopeAltered);
            alignToSlope = mark.AlignToSlope;
        }

        bool invertY = latest.InvertMouseLookYAxis;
        if (invertY != mark.InvertMouseLookYAxis)
        {
            strokes.Add(OptionsPaneText.InvertPointerGazeAxesAltered);
            invertY = mark.InvertMouseLookYAxis;
        }

        bool usePointerTurningAltered = usePointerTurningLatest != true;
        if (usePointerTurningAltered)
            strokes.Add(OptionsPaneText.PivotToFaceCamAltered);

        return new ResultDef(
            new CameraTurnSettings(stiffness, adjustmentPace, sensitivity, alignToSlope, invertY),
            usePointerTurningAltered,
            strokes);
    }
}
