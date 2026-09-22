using MacAC.Cockpit.Panels.Settings;

namespace MacAC.Client.Shell;

internal static class PaneStanceGeo
{
    internal static UiWindowArrangement Project(
        UiWindowSpot stance, int width, int height, float paneWidth, float paneHeight)
    {
        var arrangement = stance.Layout;
        return arrangement with
        {
            X = ProjectAxis(arrangement.X, stance.ScreenWidth, arrangement.Width, width, paneWidth),
            Y = ProjectAxis(arrangement.Y, stance.ScreenHeight, arrangement.Height, height, paneHeight),
        };
    }

    internal static UiWindowArrangement Default(string label, UiWindowArrangement authored,
        int width, int height, IReadOnlyDictionary<string, UiWindowArrangement> panes)
    {
        const float margin = 10f;
        float right = MathF.Max(0f, width - authored.Width - margin);
        float bottom = MathF.Max(0f, height - authored.Height - margin);
        float middleX = MathF.Max(0f, (width - authored.Width) * 0.5f);
        float middleY = MathF.Max(0f, (height - authored.Height) * 0.5f);
        float toolbarHeight = panes.TryGetValue(PaneLabels.Toolbar, out var toolbar) ? toolbar.Height : 0f;
        float indicatorWidth = panes.TryGetValue(PaneLabels.Indicators, out var indicators) ? indicators.Width : 0f;
        (float x, float y) = label switch
        {
            PaneLabels.Chat => (margin, bottom),
            PaneLabels.Radar => (right, margin),
            PaneLabels.Toolbar => (right, bottom),
            PaneLabels.Combat => (right, MathF.Max(0f, bottom - toolbarHeight - margin)),
            PaneLabels.Indicators => (margin, margin),
            PaneLabels.Vitals or PaneLabels.FlankVitals => (indicatorWidth + margin * 2f, margin),
            PaneLabels.ExtensionShelf => (margin, middleY),
            _ => (middleX, middleY),
        };
        return authored with
        {
            X = Math.Clamp(x, 0f, MathF.Max(0f, width - authored.Width)),
            Y = Math.Clamp(y, 0f, MathF.Max(0f, height - authored.Height)),
        };
    }

    private static float ProjectAxis(float locus, int srcDims, float srcReach,
        int markDims, float markReach)
    {
        float srcTravel = MathF.Max(0f, srcDims - srcReach);
        float markTravel = MathF.Max(0f, markDims - markReach);
        if (!float.IsFinite(locus)) return 0f;
        return srcDims == markDims
            ? Math.Clamp(locus, 0f, markTravel)
            : srcTravel > 0f
            ? Math.Clamp(locus / srcTravel, 0f, 1f) * markTravel
            : 0f;
    }
}
