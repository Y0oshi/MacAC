using System.Globalization;
using MacAC.Cockpit.Panels.Settings;
using Silk.NET.Windowing;

namespace MacAC.Client.Graphics;

internal static class DisplayModeRegistry
{
    public static IReadOnlyList<string>? Resolutions { get; private set; }

    public static IReadOnlyList<string>? WindowedResolutions { get; private set; }

    public static string? DesktopResolution { get; private set; }

    public static void SetupFromPane(IWindow pane)
    {
        ArgumentNullException.ThrowIfNull(pane);
        IMonitor? observe = pane.Monitor;
        if (observe is null)
            return;

        VideoMode latest = observe.VideoMode;
        if (latest.Resolution is not { } desktop || desktop.X <= 0 || desktop.Y <= 0)
            return;

        IEnumerable<(int W, int H)> manners = observe
            .GetAllVideoModes()
            .Select(mode => mode.Resolution)
            .Where(r => r.HasValue)
            .Select(r => (r!.Value.X, r.Value.Y));

        var curated = Curate(manners, (desktop.X, desktop.Y));
        if (curated.Count is 0)
            return;

        SetupFromPaneRest(desktop, curated);
    }

    private static void SetupFromPaneRest(Silk.NET.Maths.Vector2D<int> desktop, IReadOnlyList<string> curated)
    {
        Resolutions = curated;
        WindowedResolutions = AssembleWindowedOffering(curated, (desktop.X, desktop.Y));
        DesktopResolution = $"{desktop.X}x{desktop.Y}";
    }

    internal static void RestartForTests()
    {
        Resolutions = null;
        WindowedResolutions = null;
        DesktopResolution = null;
    }

    internal static IReadOnlyList<string> AssembleWindowedOffering(
        IReadOnlyList<string> curated,
        (int W, int H) desktop)
    {
        var keep = new SortedSet<(int W, int H)>(
            Comparer<(int W, int H)>.Create(static (a, b) =>
                a.W != b.W ? a.W.CompareTo(b.W) : a.H.CompareTo(b.H)));

        foreach (string spec in curated)
        {
            if (TryDecode(spec, out (int W, int H) manner))
                keep.Add(manner);
        }
        foreach (string spec in ReadoutPrefs.OnHandResolutions)
        {
            if (TryDecode(spec, out (int W, int H) manner)
                && manner.W <= desktop.W
                && manner.H <= desktop.H)

                keep.Add(manner);
        }

        return keep.Select(static m => $"{m.W}x{m.H}").ToArray();

        static bool TryDecode(string spec, out (int W, int H) mode)
        {
            mode = default;
            string[] pieces = spec.Split('x', 2);
            if (pieces.Length is 2
                && int.TryParse(
                    pieces[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                && int.TryParse(
                    pieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
                && w > 0
                && h > 0)
            {
                mode = (w, h);
                return true;
            }
            return false;
        }
    }

    internal static IReadOnlyList<string> Curate(
        IEnumerable<(int W, int H)> manners,
        (int W, int H) desktop)
    {
        ReadOnlySpan<float> modernAspects =
        [
            16f / 9f,
            16f / 10f,
            21f / 9f,
            32f / 9f,
        ];

        var keep = new SortedSet<(int W, int H)>(
            Comparer<(int W, int H)>.Create(static (a, b) =>
                a.W != b.W ? a.W.CompareTo(b.W) : a.H.CompareTo(b.H)));

        foreach ((int w, int h) in manners)
        {
            if (w <= 0 || h <= 0)
                continue;
            if (w > desktop.W || h > desktop.H)
                continue;
            if ((w, h) == desktop)
            {
                keep.Add((w, h));
                continue;
            }
            if (w < 1280)
                continue;

            float aspect = w / (float)h;
            bool modern = false;
            foreach (float clan in modernAspects)
            {
                if (MathF.Abs(aspect - clan) <= clan * 0.025f)
                {
                    modern = true;
                    break;
                }
            }
            if (modern)
                keep.Add((w, h));
        }

        return keep.Select(static m => $"{m.W}x{m.H}").ToArray();
    }
}
