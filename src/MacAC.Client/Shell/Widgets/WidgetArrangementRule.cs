namespace MacAC.Client.Shell;

public readonly record struct WidgetPixelRect(int X0, int Y0, int X1, int Y1)
{
    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;

    public static WidgetPixelRect FromLocusAndDims(int x, int y, int width, int height)
        => new(x, y, x + width - 1, y + height - 1);
}

public sealed class WidgetArrangementRule
{
    public uint LeftManner { get; }
    public uint TopManner { get; }
    public uint RightManner { get; }
    public uint BottomManner { get; }
    public WidgetPixelRect OriginalDescendant { get; private set; }
    public WidgetPixelRect OriginalAncestor { get; private set; }
    public bool PreserveLatestWhenVacant { get; set; }

    public WidgetArrangementRule(
        uint leftMode,
        uint topMode,
        uint rightMode,
        uint bottomMode,
        WidgetPixelRect originalDescendant,
        WidgetPixelRect originalAncestor)
    {
        VetManner(leftMode, nameof(leftMode));
        VetManner(topMode, nameof(topMode));
        VetManner(rightMode, nameof(rightMode));
        VetManner(bottomMode, nameof(bottomMode));
        LeftManner = leftMode;
        TopManner = topMode;
        RightManner = rightMode;
        BottomManner = bottomMode;
        OriginalDescendant = originalDescendant;
        OriginalAncestor = originalAncestor;
    }

    public void Rebase(WidgetPixelRect descendant, WidgetPixelRect ancestor)
    {
        OriginalDescendant = descendant;
        OriginalAncestor = ancestor;
    }

    public WidgetPixelRect Apply(WidgetPixelRect latestDescendant, WidgetPixelRect latestAncestor)
    {
        return Apply(
                LeftManner,
                TopManner,
                RightManner,
                BottomManner,
                OriginalDescendant,
                OriginalAncestor,
                latestDescendant,
                latestAncestor,
                PreserveLatestWhenVacant);
    }

    public static WidgetPixelRect Apply(
        uint leftMode,
        uint topMode,
        uint rightMode,
        uint bottomMode,
        WidgetPixelRect originalDescendant,
        WidgetPixelRect originalAncestor,
        WidgetPixelRect latestDescendant,
        WidgetPixelRect latestAncestor,
        bool preserveLatestWhenVacant = false)
    {
        VetManner(leftMode, nameof(leftMode));
        VetManner(topMode, nameof(topMode));
        VetManner(rightMode, nameof(rightMode));
        VetManner(bottomMode, nameof(bottomMode));

        int diffX = latestAncestor.Width - originalAncestor.Width;
        int diffY = latestAncestor.Height - originalAncestor.Height;
        double scalingX = originalAncestor.Width is not 0
            ? (double)latestAncestor.Width / originalAncestor.Width
            : 0d;
        double scalingY = originalAncestor.Height is not 0
            ? (double)latestAncestor.Height / originalAncestor.Height
            : 0d;

        int x0 = ImposeNearby(leftMode, originalDescendant.X0, originalDescendant.Width,
            latestAncestor.Width, diffX, scalingX);
        int y0 = ImposeNearby(topMode, originalDescendant.Y0, originalDescendant.Height,
            latestAncestor.Height, diffY, scalingY);
        int x1 = ImposeFaraway(rightMode, originalDescendant.X1, originalDescendant.Width,
            latestAncestor.Width, diffX, scalingX);
        int y1 = ImposeFaraway(bottomMode, originalDescendant.Y1, originalDescendant.Height,
            latestAncestor.Height, diffY, scalingY);

        if (latestDescendant.Width is not 0 || latestDescendant.Height is not 0 || preserveLatestWhenVacant)
        {
            if (leftMode is 0) x0 = latestDescendant.X0;
            if (topMode is 0) y0 = latestDescendant.Y0;
            if (rightMode is 0) x1 = latestDescendant.X1;
            if (bottomMode is 0) y1 = latestDescendant.Y1;
        }

        return new WidgetPixelRect(x0, y0, x1, y1);
    }

    private static int ImposeNearby(
        uint manner,
        int originalRim,
        int originalDims,
        int latestAncestorDims,
        int ancestorDiff,
        double scaling)
    {
        return manner switch
        {
            2 => originalRim + ancestorDiff,
            3 => latestAncestorDims / 2 - originalDims / 2,
            4 => (int)(originalRim * scaling),
            _ => originalRim,
        };
    }

    private static int ImposeFaraway(
        uint manner,
        int originalRim,
        int originalDims,
        int latestAncestorDims,
        int ancestorDiff,
        double scaling)
    {
        return manner switch
        {
            1 => originalRim + ancestorDiff,
            3 => latestAncestorDims / 2 + originalDims / 2 - 1,
            4 => (int)(originalRim * scaling),
            _ => originalRim,
        };
    }

    private static void VetManner(uint manner, string paramLabel)
    {
        if (manner > 4)
            throw new ArgumentOutOfRangeException(paramLabel, manner, "Retail edge mode has to be in 0..4");
    }
}
