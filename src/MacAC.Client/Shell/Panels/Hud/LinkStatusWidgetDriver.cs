using System.Globalization;
using MacAC.Wire;

namespace MacAC.Client.Shell.Panels;

public sealed class LinkStatusWidgetDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x2100001Du;
    public const uint RootIdent = 0x10000167u;
    public const uint PrimaryPhraseIdent = 0x10000169u;
    public const uint CloseIdent = 0x100000FCu;

    private const double RefreshIntervalSecs = 5d;
    private const double PingIntervalSecs = 120d;

    private readonly WidgetPhrase _primaryPhrase;
    private readonly WidgetBtn? _shut;
    private readonly Func<LinkStatusFrame> _capture;
    private readonly Func<double> _latestMoment;
    private readonly Action _reqPing;
    private readonly ConnectConditionTexts _texts;
    private IReadOnlyList<WidgetPhrase.Line> _strokes = Array.Empty<WidgetPhrase.Line>();
    private double _upcomingRefreshMoment = -1d;
    private double _previousPingReqMoment = -1d;
    private bool _pleaseReqPing;
    private bool _shown;

    private LinkStatusWidgetDriver(
        ImportedArrangement arrangement,
        Func<LinkStatusFrame> capture,
        Func<double> latestMoment,
        Action reqPing,
        ConnectConditionTexts texts,
        Action? shut)
    {
        _primaryPhrase = (WidgetPhrase)arrangement.SeekElem(PrimaryPhraseIdent)!;
        _shut = arrangement.SeekElem(CloseIdent) as WidgetBtn;
        _capture = capture;
        _latestMoment = latestMoment;
        _reqPing = reqPing;
        _texts = texts;
        _primaryPhrase.StrokesSupplier = () => _strokes;
        _shut?.OnClick = shut;
    }

    public static LinkStatusWidgetDriver? Bind(
        ImportedArrangement arrangement,
        Func<LinkStatusFrame> capture,
        Func<double> latestMoment,
        Action reqPing,
        ConnectConditionTexts texts,
        Action? shut = null)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(latestMoment);
        ArgumentNullException.ThrowIfNull(reqPing);
        ArgumentNullException.ThrowIfNull(texts);
        return arrangement.SeekElem(PrimaryPhraseIdent) is WidgetPhrase
            ? new LinkStatusWidgetDriver(
                arrangement, capture, latestMoment, reqPing, texts, shut)
            : null;
    }

    public void Tick()
    {
        if (!_shown) return;
        double instant = _latestMoment();
        if (!double.IsFinite(instant) || instant < _upcomingRefreshMoment) return;
        _upcomingRefreshMoment = instant + RefreshIntervalSecs;
        Update(instant);
    }

    public void OnShown()
    {
        _shown = true;
        _pleaseReqPing = true;
        Update(_latestMoment());
    }

    public void OnConcealed()
    {
        _shown = false;
        _pleaseReqPing = false;
    }

    public void Dispose() => _shut?.OnClick = null;

    private void Update(double instant)
    {
        var val = _capture();
        string ping = val.RoundTripSeconds is double secs
                      && double.IsFinite(secs)
                      && secs >= 0d
            ? (secs * 1000d).ToString("F0", CultureInfo.InvariantCulture)
            : "????";
        string corpus = _texts.Description
                      + _texts.Legend
                      + _texts.DisconnectWarning
                      + _texts.PacketLossPrefix
                      + val.PacketLossPercentage.ToString("F2", CultureInfo.InvariantCulture)
                      + _texts.PingPrefix
                      + ping;
        _strokes = IndicatorSpecificsPhrase.Shape(_primaryPhrase, corpus);

        if (!double.IsFinite(instant)) return;
        bool periodicPing = _previousPingReqMoment >= 0d
            && instant - _previousPingReqMoment >= PingIntervalSecs;
        if (!_pleaseReqPing && !periodicPing) return;
        _previousPingReqMoment = instant;
        _pleaseReqPing = false;
        _reqPing();
    }
}

public sealed record ConnectConditionTexts(
    string Description,
    string Legend,
    string DisconnectWarning,
    string PacketLossPrefix,
    string PingPrefix)
{
    public static ConnectConditionTexts English { get; } = new(
        "The Link Indicator shows the current status of your connection to the game servers.",
        "\n\nGREEN = your link is good.\nYELLOW = no packets for at least 5 sec.\nRED = no packets for at least 20 sec.",
        "\n\nIf approximately forty seconds pass without receiving a packet, you will be disconnected from the server.",
        "\n\n\nPacket loss for the last 10 sec: ",
        "\n\nRoundtrip Ping time to Server: ");
}
