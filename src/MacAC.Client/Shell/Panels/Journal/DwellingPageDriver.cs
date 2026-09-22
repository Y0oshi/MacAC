using System.Numerics;

namespace MacAC.Client.Shell.Panels;

public sealed class DwellingPageDriver
{
    public const uint PhraseBboxIdent = 0x100001E6u;

    public sealed record Bindings(
        Func<IReadOnlyList<string>> Lines,
        Action? OnShown = null,
        Func<uint, uint, WidgetElem?>? TemplateResolver = null,
        Func<IReadOnlyList<DwellingPanelLine>>? PanelLines = null);

    private readonly WidgetBlueprintRosterBbox _rosterBbox;
    private readonly Bindings _bindings;
    private IReadOnlyList<DwellingPanelLine> _previousStrokes = Array.Empty<DwellingPanelLine>();

    private DwellingPageDriver(WidgetBlueprintRosterBbox rosterBbox, Bindings mappings)
    {
        _rosterBbox = rosterBbox;
        _bindings = mappings;
    }

    public static DwellingPageDriver? Bind(WidgetElem sheet, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(mappings);

        if (WidgetElem.SeekDescendant(sheet, PhraseBboxIdent) is not WidgetBlueprintRosterBbox rosterBbox)
        {
            Console.WriteLine(
                $"[UI] House tab: ListBox 0x{PhraseBboxIdent:X8} not found or not a template list box");
            return null;
        }

        rosterBbox.TemplateResolver = mappings.TemplateResolver;
        DwellingPageDriver driver = new DwellingPageDriver(rosterBbox, mappings);
        driver.Refresh(driver.LatestStrokes());
        return driver;
    }

    public void Tick()
    {
        var strokes = LatestStrokes();
        if (strokes.SequenceEqual(_previousStrokes)) return;
        Refresh(strokes);
    }

    public void OnShown() => _bindings.OnShown?.Invoke();

    private IReadOnlyList<DwellingPanelLine> LatestStrokes()
    {
        if (_bindings.PanelLines is { } styled)
            return styled();

        var plain = _bindings.Lines();
        if (plain.Count is 0)
            return Array.Empty<DwellingPanelLine>();

        DwellingPanelLine[] projected = new DwellingPanelLine[plain.Count];
        for (int idx = 0; idx < plain.Count; ++idx)
            projected[idx] = new DwellingPanelLine(plain[idx], DwellingPanelTextColor.Normal);
        return projected;
    }

    private void Refresh(IReadOnlyList<DwellingPanelLine> strokes)
    {
        _previousStrokes = strokes;
        _rosterBbox.Flush();
        foreach (DwellingPanelLine stroke in strokes)
        {
            WidgetElem? rank = _rosterBbox.AppendGearFromBlueprintRoster(0);
            if (rank is WidgetPhrase phrase)
            {
                int tintOrdinal = (int)stroke.Color;
                Vector4 tint = tintOrdinal >= 0 && tintOrdinal < phrase.TypefaceTintSwatch.Count
                    ? phrase.TypefaceTintSwatch[tintOrdinal]
                    : phrase.DefaultTint;
                phrase.StrokesSupplier = () => [new WidgetPhrase.Line(stroke.Text, tint)];
            }
        }
    }
}
