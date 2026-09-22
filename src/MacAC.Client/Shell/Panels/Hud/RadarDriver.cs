using System.Numerics;
using MacAC.Mechanics.Shell;

namespace MacAC.Client.Shell.Panels;

public sealed class RadarDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x21000074u;
    public const int RadarPixelRadius = 50;
    public const uint RootId = 0x100006D3u;
    public const uint CoordinateVesselIdent = 0x1000003Eu;
    public const uint RadarDiscIdent = 0x1000003Fu;
    public const uint NorthTicketIdent = 0x10000040u;
    public const uint EastTicketIdent = 0x10000041u;
    public const uint SouthTicketIdent = 0x10000042u;
    public const uint WestTicketIdent = 0x10000043u;
    public const uint LockBtnIdent = 0x10000619u;
    public const uint PullBtnIdent = 0x100006A3u;

    private static readonly Vector4 CoordinateTint = Vector4.One;

    private readonly UiRadar _radar;
    private readonly WidgetElem? _coordinateVessel;
    private readonly WidgetPhrase? _coordinatePhrase;
    private readonly WidgetBtn? _mutexBtn;
    private readonly WidgetElem? _pullBtn;
    private readonly Action<bool>? _setWidgetBolted;
    private readonly CompassTicket[] _tickets;
    private float _previousBearing = float.NaN;
    private string? _previousCoordinates;
    private bool _coordinatesInitialized;
    private bool? _previousWidgetBolted;
    private bool _destroyed;

    private RadarDriver(
        ImportedArrangement layout,
        Func<WidgetRadarCapture> captureSupplier,
        Action<uint>? pickObject,
        Action<uint?>? hoveredObjectAltered,
        Action<bool>? setWidgetBolted,
        WidgetDatFont? datTypeface)
    {
        _radar = layout.Root as UiRadar
            ?? throw new ArgumentException(
                $"Layout root has to be {nameof(UiRadar)} (retail class 0x{UiRadar.CanonClassIdent:X8}).",
                nameof(layout));

        _radar.Center = LocateMiddle(layout);
        _radar.CaptureSupplier = () =>
        {
            WidgetRadarCapture capture = captureSupplier() ?? WidgetRadarCapture.Empty;
            ImposeExhibit(capture);
            return capture;
        };
        _radar.PickObject = pickObject;
        _radar.HoveredObjectAltered = hoveredObjectAltered;
        _setWidgetBolted = setWidgetBolted;

        _coordinateVessel = layout.SeekElem(CoordinateVesselIdent);
        _coordinatePhrase = BuildCoordinatePhrase(_coordinateVessel, datTypeface);
        _mutexBtn = layout.SeekElem(LockBtnIdent) as WidgetBtn;
        _pullBtn = layout.SeekElem(PullBtnIdent);

        if (_mutexBtn is not null)
        {
            _mutexBtn.DriverOwnsVisualPhase = true;
            if (_setWidgetBolted is not null)
                _mutexBtn.OnClick = () => _setWidgetBolted(!(_previousWidgetBolted ?? false));
        }

        _tickets =
        [
            BuildTicket(layout.SeekElem(NorthTicketIdent), RadarCompassMark.North, _radar.Center),
            BuildTicket(layout.SeekElem(EastTicketIdent), RadarCompassMark.East, _radar.Center),
            BuildTicket(layout.SeekElem(SouthTicketIdent), RadarCompassMark.South, _radar.Center),
            BuildTicket(layout.SeekElem(WestTicketIdent), RadarCompassMark.West, _radar.Center),
        ];

        _pullBtn?.ClickThrough = false;

        _radar.Draggable = true;
        _radar.ResizeX = false;
        _radar.ResizeY = false;
        _radar.Refresh();
    }

    public static RadarDriver Bind(
        ImportedArrangement arrangement,
        Func<WidgetRadarCapture> captureSupplier,
        Action<uint>? pickObject = null,
        Action<uint?>? hoveredObjectAltered = null,
        Action<bool>? setWidgetBolted = null,
        WidgetDatFont? datTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(captureSupplier);
        return new RadarDriver(
            arrangement,
            captureSupplier,
            pickObject,
            hoveredObjectAltered,
            setWidgetBolted,
            datTypeface);
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _radar.CaptureSupplier = null;
        _radar.PickObject = null;
        _radar.HoveredObjectAltered = null;
        _mutexBtn?.OnClick = null;
    }

    private void ImposeExhibit(WidgetRadarCapture capture)
    {
        if (!capture.PlayerHeadingDegrees.Equals(_previousBearing))
        {
            for (int idx = 0; idx < _tickets.Length; ++idx)
            {
                ref readonly var ticket = ref _tickets[idx];
                if (ticket.Element is null)
                    continue;

                Vector2 p = CanonRadar.FetchCompassTicketTopLeft(
                    capture.PlayerHeadingDegrees,
                    ticket.Point,
                    _radar.Center,
                    ticket.Magnitude,
                    new Vector2(ticket.Element.Width, ticket.Element.Height));
                ticket.Element.Left = p.X;
                ticket.Element.Top = p.Y;
            }

            _previousBearing = capture.PlayerHeadingDegrees;
        }

        if (!_coordinatesInitialized
            || !string.Equals(capture.CoordinatesText, _previousCoordinates, StringComparison.Ordinal))
        {
            _coordinatesInitialized = true;
            _previousCoordinates = capture.CoordinatesText;
            bool shown = !string.IsNullOrEmpty(capture.CoordinatesText);
            _coordinateVessel?.Visible = shown;
            _coordinatePhrase?.Visible = shown;
        }

        if (_previousWidgetBolted != capture.UiLocked)
        {
            _previousWidgetBolted = capture.UiLocked;
            _radar.Draggable = !capture.UiLocked;
            _pullBtn?.Visible = !capture.UiLocked;
            _mutexBtn?.TrySetCanonPhase(
                    capture.UiLocked ? CanonWidgetStateIds.BoltedWidget : CanonWidgetStateIds.UnlockedWidget);
        }
    }

    private static Vector2 LocateMiddle(ImportedArrangement arrangement)
    {
        return arrangement.SeekElem(RadarDiscIdent) is { } disc
            ? new Vector2(disc.Left + disc.Width * 0.5f, disc.Top + disc.Height * 0.5f)
            : new Vector2(arrangement.Root.Width * 0.5f, MathF.Min(120f, arrangement.Root.Height) * 0.5f);
    }

    private static CompassTicket BuildTicket(WidgetElem? elem, RadarCompassMark pt, Vector2 middle)
    {
        if (elem is null)
            return new CompassTicket(null, 0f, pt);

        Vector2 startingMiddle = new Vector2(
            elem.Left + elem.Width * 0.5f,
            elem.Top + elem.Height * 0.5f);
        return new CompassTicket(elem, Vector2.Distance(startingMiddle, middle), pt);
    }

    private WidgetPhrase? BuildCoordinatePhrase(WidgetElem? vessel, WidgetDatFont? datTypeface)
    {
        if (vessel is null)
            return null;

        if (vessel is WidgetPhrase importedPhrase)
        {
            importedPhrase.Centered = true;
            importedPhrase.OneLine = true;
            importedPhrase.Padding = 0f;
            importedPhrase.DatFont = datTypeface ?? importedPhrase.DatFont;
            importedPhrase.ClickThrough = true;
            importedPhrase.AcceptsFocus = false;
            importedPhrase.IsEditControl = false;
            importedPhrase.CapturesPointerDrag = false;
            importedPhrase.StrokesSupplier = CoordinateStrokes;
            return importedPhrase;
        }

        WidgetPhrase phrase = new WidgetPhrase
        {
            Name = "gmRadarUI.Coordinates",
            Left = 0f,
            Top = 0f,
            Width = vessel.Width,
            Height = vessel.Height,
            Moorings = MooringRims.Left | MooringRims.Top | MooringRims.Right | MooringRims.Bottom,
            Centered = true,
            OneLine = true,
            Padding = 0f,
            DatFont = datTypeface,
            ClickThrough = true,
            AcceptsFocus = false,
            IsEditControl = false,
            CapturesPointerDrag = false,
            ZOrder = int.MaxValue,
            StrokesSupplier = CoordinateStrokes,
        };
        vessel.AddChild(phrase);
        return phrase;
    }

    private IReadOnlyList<WidgetPhrase.Line> CoordinateStrokes()
    {
        return string.IsNullOrEmpty(_previousCoordinates)
                ? []
                : new[] { new WidgetPhrase.Line(_previousCoordinates, CoordinateTint) };
    }

    private readonly record struct CompassTicket(
        WidgetElem? Element,
        float Magnitude,
        RadarCompassMark Point);
}
