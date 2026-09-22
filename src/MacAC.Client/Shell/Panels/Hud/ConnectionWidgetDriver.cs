using System.Diagnostics;
using System.Numerics;
using MacAC.Sim.Presence;

namespace MacAC.Client.Shell.Panels;

internal sealed class ConnectionWidgetDriver : IDisposable
{
    internal const uint TrunkEnum = 0x10000001u;
    internal const uint TrunkElemIdent = 0x1000041Au;
    internal const uint ConnectionGaugeIdent = 0x1000041Eu;
    internal const uint RefreshGaugeIdent = 0x1000041Fu;
    internal const uint ConnectionPhraseIdent = 0x10000420u;
    internal const uint RefreshPhraseIdent = 0x10000421u;
    internal const uint AbortIdent = 0x1000041Cu;

    private readonly WidgetTrunk _hub;
    private readonly ConnectionEngineWiring _bindings;
    private readonly WidgetGauge _connectionGauge;
    private readonly WidgetGauge _refreshGauge;
    private readonly WidgetPhrase _connectionPhrase;
    private readonly WidgetPhrase _refreshPhrase;
    private readonly WidgetBtn _abort;
    private readonly WidgetBoard _problemBoard;
    private readonly string _checkingPhrase;
    private readonly string _finishedPhrase;
    private readonly Func<IReadOnlyList<WidgetPhrase.Line>> _startingRefreshPhrase;
    private SimLinkCapture _capture;
    private bool _destroyed;
    private readonly Func<double> _instantSecs;
    private readonly double _floorShownSecs;
    private double? _shownAt;
    private bool _exhibitFinished;

    private ConnectionWidgetDriver(WidgetTrunk hub, ImportedArrangement arrangement,
        ConnectionEngineWiring mappings, string checkingPhrase, string finishedPhrase,
        Func<double>? instantSecs, double floorShownSecs)
    {
        _hub = hub;
        _bindings = mappings;
        _instantSecs = instantSecs ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
        _floorShownSecs = floorShownSecs;
        Root = arrangement.Root;
        Root.Left = Root.Top = 0f;
        Root.Visible = false;
        Root.ClickThrough = false;
        Root.AddChild(new WidgetBoard
        {
            Name = "ConnectionBackdrop",
            Left = 0f,
            Top = 0f,
            Width = Root.Width,
            Height = Root.Height,
            BackgroundColor = new Vector4(0f, 0f, 0f, 1f),
            BorderTint = Vector4.Zero,
            ZOrder = int.MinValue,
            ClickThrough = true,
        });
        _connectionGauge = (WidgetGauge)arrangement.SeekElem(ConnectionGaugeIdent)!;
        _refreshGauge = (WidgetGauge)arrangement.SeekElem(RefreshGaugeIdent)!;
        _connectionPhrase = (WidgetPhrase)arrangement.SeekElem(ConnectionPhraseIdent)!;
        _refreshPhrase = (WidgetPhrase)arrangement.SeekElem(RefreshPhraseIdent)!;
        _abort = (WidgetBtn)arrangement.SeekElem(AbortIdent)!;
        _checkingPhrase = checkingPhrase;
        _finishedPhrase = finishedPhrase;
        _startingRefreshPhrase = _refreshPhrase.StrokesSupplier;
        _connectionGauge.Populate = () => _capture.ConnectionProgress;
        _refreshGauge.Populate = () => _capture.UpdateProgress;
        _abort.OnClick = mappings.RequestExit;
        _problemBoard = new WidgetBoard
        {
            Left = 30f,
            Top = 352f,
            Width = 740f,
            Height = 104f,
            BackgroundColor = new Vector4(0f, 0f, 0f, 0.92f),
            BorderTint = Vector4.Zero,
            Visible = false,
            ClickThrough = true,
        };
        _problemPhrase = new WidgetPhrase
        {
            Left = 12f,
            Top = 12f,
            Width = 716f,
            Height = 80f,
            DatFont = _refreshPhrase.DatFont,
            Font = _refreshPhrase.Font,
            DefaultTint = Vector4.One,
            Outline = true,
            ClickThrough = true,
        };
        _problemBoard.AddChild(_problemPhrase);
        Root.AddChild(_problemBoard);
        _hub.AddChild(Root);
    }

    internal WidgetElem Root { get; }
    private readonly WidgetPhrase _problemPhrase;

    internal WidgetPhrase ProblemPhrase => _problemPhrase;

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _abort.OnClick = null;
        _connectionGauge.Populate = static () => 0f;
        _refreshGauge.Populate = static () => 0f;
        _hub.RevokeFixedCanvas(this);
        _hub.DropDescendant(Root);
    }
    internal static ConnectionWidgetDriver? Bind(WidgetTrunk hub, ImportedArrangement arrangement,
        ConnectionEngineWiring mappings, string checkingPhrase, string finishedPhrase,
        Func<double>? instantSecs = null, double floorShownSecs = 2d)
    {
        return arrangement.Root.DatElemIdent != TrunkElemIdent
            || arrangement.SeekElem(ConnectionGaugeIdent) is not WidgetGauge
            || arrangement.SeekElem(RefreshGaugeIdent) is not WidgetGauge
            || arrangement.SeekElem(ConnectionPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(RefreshPhraseIdent) is not WidgetPhrase
            || arrangement.SeekElem(AbortIdent) is not WidgetBtn
            ? null
            : new ConnectionWidgetDriver(hub, arrangement, mappings, checkingPhrase, finishedPhrase,
            instantSecs, floorShownSecs);
    }

    internal void Tick()
    {
        if (_destroyed) return;
        SimLinkCapture capture = _bindings.View()?.Snapshot ?? default;
        if (capture.Status == SimLinkStatus.Inactive
            || capture.Status == SimLinkStatus.Connecting
                && _capture.Status != SimLinkStatus.Connecting)
        {
            _shownAt = null;
            _exhibitFinished = false;
        }
        bool miss = capture.Status is SimLinkStatus.Unsupported or SimLinkStatus.Failed;
        bool headway = _bindings.ShowProgress && !_exhibitFinished
            && capture.Status is SimLinkStatus.Connecting or SimLinkStatus.CheckingData
                or SimLinkStatus.Ready;
        if (headway)
        {
            _shownAt ??= _instantSecs();
            if (capture.Status == SimLinkStatus.Ready
                && _instantSecs() - _shownAt.Value >= _floorShownSecs)
            {
                _exhibitFinished = true;
                headway = false;
            }
        }
        bool shown = miss || headway;
        if (!shown)
        {
            Root.Visible = false;
            _hub.RevokeFixedCanvas(this);
            _capture = capture;
            return;
        }

        if (!Root.Visible)
        {
            Root.Visible = true;
            _hub.DeclareFixedCanvas(this, new Vector2(Root.Width, Root.Height));
            _hub.BringToFront(Root);
        }
        if (_capture == capture) return;
        _capture = capture;
        _connectionPhrase.TrySetCanonPhase(capture.ConnectionProgress >= 1f
            ? 0x1000003Cu : capture.Status == SimLinkStatus.Connecting
                ? 0x1000003Bu : 1u);
        if (capture.UpdateProgress >= 1f)
            AssignPhrase(_refreshPhrase, _finishedPhrase);
        else if (capture.Status == SimLinkStatus.CheckingData)
            AssignPhrase(_refreshPhrase, _checkingPhrase);
        else
            _refreshPhrase.StrokesSupplier = _startingRefreshPhrase;

        _problemBoard.Visible = capture.Status is SimLinkStatus.Unsupported
            or SimLinkStatus.Failed;
        string problem = capture.Error ?? string.Empty;
        WidgetPhrase.Line[] problemStrokes = [.. WidgetPhrase.EncloseWords(problem,
                phrase => _problemPhrase.DatFont?.MeasureWidth(phrase)
                    ?? _problemPhrase.Font?.MeasureWidth(phrase) ?? phrase.Length * 8f,
                _problemPhrase.Width)
            .Select(phrase => new WidgetPhrase.Line(phrase, _problemPhrase.DefaultTint))];
        _problemPhrase.StrokesSupplier = () => problemStrokes;
    }

    private static void AssignPhrase(WidgetPhrase elem, string phrase)
    {
        WidgetPhrase.Line[] strokes = [new(phrase, elem.DefaultTint)];
        elem.StrokesSupplier = () => strokes;
    }
}
