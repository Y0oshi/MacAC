using MacAC.Mechanics.Realm;
using MacAC.Mechanics.Shell;
using MacAC.Wire.Messages;

namespace MacAC.Client.Shell.Panels;

public sealed class MapPageDriver
{
    public const uint DateMomentPhraseIdent = 0x100001EBu;
    public const uint LookupWidgetIdent = 0x100001ECu;
    public const uint AvatarGlyphIdent = 0x100001EDu;
    public const uint HouseGlyphIdent = 0x100001EEu;
    public const uint CoordinatePhraseIdent = 0x100001EFu;

    private const uint MarkerAreaX0Attr = 0x1000004Eu;
    private const uint MarkerAreaX1Attr = 0x1000004Fu;
    private const uint MarkerAreaY0Attr = 0x10000050u;
    private const uint MarkerAreaY1Attr = 0x10000051u;
    private const uint HotspotBlueprintElemAttr = 0x47u;
    private const uint HotspotBlueprintArrangementAttr = 0x48u;

    public const double RenewIntervalSecs = 5.0;

    public sealed record Bindings(
        Func<DerethDateMoment.Almanac> CurrentCalendar,
        Func<uint> PlayerCellId,
        Func<ObjectCreation.RemotePosition?> HousePosition,
        Func<uint, uint, WidgetElem?> TemplateResolver,
        Func<ElemDetails, WidgetElem?> IconBuilder,
        Func<uint, uint, ElemDetails?>? TemplateInfoResolver = null);

    private readonly WidgetElem? _dateMomentPhrase;
    private readonly WidgetElem? _lookup;
    private readonly WidgetElem? _avatarGlyph;
    private readonly WidgetElem? _houseGlyph;
    private readonly WidgetElem? _coordinatePhrase;
    private readonly Bindings _bindings;
    private readonly int _markerX0, _markerX1, _markerY0, _markerY1;

    private double _upcomingRefreshSecs;
    private string? _previousDateMomentPhrase;
    private string? _previousCoordinatePhrase;

    private MapPageDriver(
        WidgetElem? dateMomentPhrase,
        WidgetElem lookup,
        WidgetElem? avatarGlyph,
        WidgetElem? houseGlyph,
        WidgetElem? coordinatePhrase,
        (int X0, int X1, int Y0, int Y1) markerArea,
        Bindings mappings)
    {
        _dateMomentPhrase = dateMomentPhrase;
        _lookup = lookup;
        _avatarGlyph = avatarGlyph;
        _houseGlyph = houseGlyph;
        _coordinatePhrase = coordinatePhrase;
        _bindings = mappings;
        (_markerX0, _markerX1, _markerY0, _markerY1) = markerArea;
    }

    public static MapPageDriver? Bind(WidgetElem sheet, ElemDetails sheetDetails, Bindings mappings)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(sheetDetails);
        ArgumentNullException.ThrowIfNull(mappings);

        WidgetElem? lookup = WidgetElem.SeekDescendant(sheet, LookupWidgetIdent);
        if (lookup is null)
        {
            Console.WriteLine($"[UI] Map tab: m_pMap 0x{LookupWidgetIdent:X8} not found - Map tab will not populate");
            return null;
        }

        var lookupDetails = SeekDetails(sheetDetails, LookupWidgetIdent);
        var markerArea = (X0: 0, X1: 0, Y0: 0, Y1: 0);
        if (lookupDetails is not null)
        {
            int x0 = lookupDetails.TryFetchNetProp(MarkerAreaX0Attr, out var vx0) ? vx0.IntegerValue : 0;
            int x1 = lookupDetails.TryFetchNetProp(MarkerAreaX1Attr, out var vx1) ? vx1.IntegerValue : 0;
            int y0 = lookupDetails.TryFetchNetProp(MarkerAreaY0Attr, out var vy0) ? vy0.IntegerValue : 0;
            int y1 = lookupDetails.TryFetchNetProp(MarkerAreaY1Attr, out var vy1) ? vy1.IntegerValue : 0;
            markerArea = (x0, x1, y0, y1);
        }

        WidgetElem? avatarGlyph = LocateSwallowedGlyph(lookup, lookupDetails, mappings.IconBuilder, AvatarGlyphIdent);
        WidgetElem? houseGlyph = LocateSwallowedGlyph(lookup, lookupDetails, mappings.IconBuilder, HouseGlyphIdent);

        MapPageDriver driver = new MapPageDriver(
            WidgetElem.SeekDescendant(sheet, DateMomentPhraseIdent),
            lookup,
            avatarGlyph,
            houseGlyph,
            WidgetElem.SeekDescendant(sheet, CoordinatePhraseIdent),
            markerArea,
            mappings);

        driver.AssembleTownMarkers(lookupDetails, mappings.TemplateResolver);

        if (driver._dateMomentPhrase is WidgetPhrase dateMomentPhrase)
            dateMomentPhrase.StrokesSupplier = () => ToStrokes(driver._previousDateMomentPhrase, dateMomentPhrase.DefaultTint);
        if (driver._coordinatePhrase is WidgetPhrase coordinatePhrase)
            coordinatePhrase.StrokesSupplier = () => ToStrokes(driver._previousCoordinatePhrase, coordinatePhrase.DefaultTint);

        driver.Refresh();
        driver._upcomingRefreshSecs = RenewIntervalSecs;
        return driver;
    }

    public void Tick(double diffSecs)
    {
        _upcomingRefreshSecs -= diffSecs;
        if (_upcomingRefreshSecs > 0) return;
        _upcomingRefreshSecs = RenewIntervalSecs;
        Refresh();
    }

    internal static string ComposeDateMoment(DerethDateMoment.Almanac calendar)
    {
        return $"Date: {calendar.Month} {calendar.Day}, {calendar.Year} P.Y.\nTime: {ComposeHourLabel(calendar.Hour)}";
    }

    internal static (float Left, float Top) CalculateMarkerLocus(
        int markerX0, int markerX1, int markerY0, int markerY1,
        int glyphWidth, int glyphHeight, double x, double y)
    {
        int halfWidth = glyphWidth / 2;
        int halfHeight = glyphHeight / 2;
        int reachX = markerX1 - markerX0 + 1;
        int reachY = markerY1 - markerY0 + 1;

        int xShift = (int)(reachX * (x * 10.0 + 1024.0) * (-1.0 / 2048.0));
        int yShift = (int)(reachY * (2047.0 - (y * 10.0 + 1024.0)) * (-1.0 / 2048.0));

        return (markerX0 - halfWidth - xShift, markerY0 - halfHeight - yShift);
    }

    private static WidgetElem? LocateSwallowedGlyph(
        WidgetElem lookup, ElemDetails? lookupDetails, Func<ElemDetails, WidgetElem?> glyphBuilder, uint glyphElemIdent)
    {
        WidgetElem? extant = WidgetElem.SeekDescendant(lookup, glyphElemIdent);
        if (extant is not null)
            return ReadyGlyph(extant);

        ElemDetails? glyphDetails = lookupDetails is null ? null : SeekDetails(lookupDetails, glyphElemIdent);
        if (glyphDetails is null)
        {
            Console.WriteLine(
                $"[UI] Map tab: icon 0x{glyphElemIdent:X8} not authored under m_pMap's resolved "
                + "info tree - it will not be shown");
            return null;
        }

        WidgetElem? glyph = glyphBuilder(glyphDetails);
        if (glyph is null)
        {
            Console.WriteLine(
                $"[UI] Map tab: icon 0x{glyphElemIdent:X8} didn't build - it will not be shown");
            return null;
        }
        lookup.AddChild(ReadyGlyph(glyph));
        return glyph;
    }

    private static WidgetElem ReadyGlyph(WidgetElem glyph)
    {
        glyph.Moorings = MooringRims.None;
        glyph.Visible = false;
        return glyph;
    }

    private static IReadOnlyList<WidgetPhrase.Line> ToStrokes(string? phrase, System.Numerics.Vector4 tint)
    {
        if (string.IsNullOrEmpty(phrase)) return Array.Empty<WidgetPhrase.Line>();
        string[] pieces = phrase.Split('\n');
        WidgetPhrase.Line[] strokes = new WidgetPhrase.Line[pieces.Length];
        for (int idx = 0; idx < pieces.Length; ++idx)
            strokes[idx] = new WidgetPhrase.Line(pieces[idx], tint);
        return strokes;
    }

    private void AssembleTownMarkers(ElemDetails? lookupDetails, Func<uint, uint, WidgetElem?> blueprintLocator)
    {
        if (lookupDetails is null) return;
        if (!lookupDetails.TryFetchNetProp(HotspotBlueprintElemAttr, out var blueprintElem)) return;
        if (!lookupDetails.TryFetchNetProp(HotspotBlueprintArrangementAttr, out var blueprintArrangement)) return;
        if (blueprintArrangement.UnsignedValue is 0) return;

        var blueprintDetails = _bindings.TemplateInfoResolver?.Invoke(
            (uint)blueprintArrangement.UnsignedValue, (uint)blueprintElem.UnsignedValue);

        foreach (LookupLocale loc in LookupLocales.All)
        {
            WidgetElem? marker = blueprintLocator(
                (uint)blueprintArrangement.UnsignedValue, (uint)blueprintElem.UnsignedValue);
            if (marker is null) continue;

            marker.Left = loc.X;
            marker.Top = loc.Y;
            marker.Width = loc.Width;
            marker.Height = loc.Height;
            if (marker is WidgetBtn markerBtn)
                markerBtn.TooltipText = loc.Name;
            else
                Console.WriteLine(
                    $"[UI] Map tab: town marker '{loc.Name}' template "
                    + $"resolved to {marker.GetType().Name}, not WidgetBtn - "
                    + "TooltipText can't be set, marker will show no tooltip");

            if (blueprintDetails is not null && marker is WidgetBtn phaseHub)
            {
                foreach (ElemDetails descendantDetails in blueprintDetails.Children)
                    if (_bindings.IconBuilder(descendantDetails) is { } highlight)
                        marker.AddChild(highlight);
                phaseHub.TrySetCanonPhase(WidgetButtonStateMachine.Normal);
            }
            _lookup!.AddChild(marker);
        }
    }

    private void Refresh()
    {
        RenewDateMoment();
        RenewCoordinatesAndAvatarMarker();
        RenewHouseMarker();
    }

    private void RenewDateMoment()
    {
        if (_dateMomentPhrase is null) return;
        var calendar = _bindings.CurrentCalendar();
        string phrase = ComposeDateMoment(calendar);
        _previousDateMomentPhrase = phrase;
    }

    private static string ComposeHourLabel(DerethDateMoment.HourLabel hour)
    {
        string label = hour.ToString();
        const string suffix = "AndHalf";
        return label.EndsWith(suffix, StringComparison.Ordinal)
            ? string.Concat(label.AsSpan(0, label.Length - suffix.Length), "-and-Half")
            : label;
    }

    private void RenewCoordinatesAndAvatarMarker()
    {
        if (_coordinatePhrase is null || _avatarGlyph is null) return;

        bool beyond = RadarCoords.TryFromChamber(_bindings.PlayerCellId(), out RadarCoords coords);
        if (beyond)
        {
            _previousCoordinatePhrase = coords.CombinedPhrase;
            PutMarker(_avatarGlyph, coords.X, coords.Y);
        }
        else
        {
            _previousCoordinatePhrase = string.Empty;
            _avatarGlyph.Visible = false;
        }
    }

    private void RenewHouseMarker()
    {
        if (_houseGlyph is null) return;

        var houseLocus = _bindings.HousePosition();
        if (houseLocus is null)
        {
            _houseGlyph.Visible = false;
            return;
        }

        if (!RadarCoords.TryFromChamber(houseLocus.Value.LandblockId, out RadarCoords coords))
        {
            _houseGlyph.Visible = false;
            return;
        }

        PutMarker(_houseGlyph, coords.X, coords.Y);
    }

    private void PutMarker(WidgetElem? glyph, double x, double y)
    {
        if (glyph is null) return;

        (float left, float top) = CalculateMarkerLocus(
            _markerX0, _markerX1, _markerY0, _markerY1,
            (int)glyph.Width, (int)glyph.Height, x, y);
        glyph.Left = left;
        glyph.Top = top;
        glyph.Visible = true;
    }

    private static ElemDetails? SeekDetails(ElemDetails details, uint ident)
    {
        if (details.Id == ident) return details;
        foreach (ElemDetails descendant in details.Children)
        {
            var located = SeekDetails(descendant, ident);
            if (located is not null) return located;
        }
        return null;
    }
}
