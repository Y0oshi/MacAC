namespace MacAC.Client.Shell;

public sealed class WidgetScrollable
{
    public int ContentHeight { get; set; }
    public int LensHeight { get; set; }
    public int LineHeight { get; set; } = 16;
    public int RollY { get; private set; }

    public int UpperRoll => Math.Max(0, ContentHeight - LensHeight);

    public bool HasOverflow => ContentHeight > LensHeight;

    /// <summary>True when the offset is at (or past) the bottom - used for bottom-pin.</summary>
    public bool AtFinish => RollY >= UpperRoll;

    public void AssignExtents(int substanceHeight, int lensHeight, bool preserveFinish = false)
    {
        bool wasAtFinish = AtFinish;
        ContentHeight = Math.Max(0, substanceHeight);
        LensHeight = Math.Max(0, lensHeight);

        if (preserveFinish && wasAtFinish)
            RollToFinish();
        else
            AssignRollY(RollY);
    }

    public void AssignRollY(int y) => RollY = Math.Clamp(y, 0, UpperRoll);

    public void RollToFinish() => RollY = UpperRoll;

    public float ThumbRatio
    {
        get
        {
            return ContentHeight <= 0 ? 1f : Math.Min(1f, (float)LensHeight / ContentHeight);
        }
    }

    public float LocusRatio => UpperRoll <= 0 ? 0f : (float)RollY / UpperRoll;

    /// <summary>Inverse of PositionRatio - used when the user drags the thumb.</summary>
    public void AssignLocusRatio(float ratio)
        => AssignRollY((int)MathF.Round(Math.Clamp(ratio, 0f, 1f) * UpperRoll));

    public void RollByStrokes(int strokes) => AssignRollY(RollY + strokes * LineHeight);

    public void RollBySheet(int sheets) => AssignRollY(RollY + sheets * LensHeight);
}
