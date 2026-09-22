using System.Globalization;

namespace MacAC.Client.Shell.Panels;

public sealed class CanonFpsDriver
{
    public const uint ArrangementId = 0x2100000Fu;
    public const uint ReadoutElemIdent = 0x10000047u;
    private readonly Func<double> _cyclesPerSecond;
    private readonly Func<double> _downgradeMultiplier;
    private readonly Func<bool> _isShown;

    private CanonFpsDriver(
        WidgetPhrase readout,
        Func<double> cyclesPerSecond,
        Func<double> downgradeMultiplier,
        Func<bool> isShown)
    {
        _readout = readout;
        _cyclesPerSecond = cyclesPerSecond;
        _downgradeMultiplier = downgradeMultiplier;
        _isShown = isShown;

        _readout.Left = 1f;
        _readout.Top = 1f;
        _readout.Padding = 1f;
        _readout.Selectable = false;
        _readout.StrokesSupplier = AssembleStrokes;
        Tick();
    }

    private readonly WidgetPhrase _readout;

    public WidgetPhrase Display => _readout;
    public static CanonFpsDriver? Bind(
        ImportedArrangement arrangement,
        Func<double> cyclesPerSecond,
        Func<double> downgradeMultiplier,
        Func<bool> isShown)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(cyclesPerSecond);
        ArgumentNullException.ThrowIfNull(downgradeMultiplier);
        ArgumentNullException.ThrowIfNull(isShown);

        return arrangement.SeekElem(ReadoutElemIdent) is WidgetPhrase readout
            ? new CanonFpsDriver(readout, cyclesPerSecond, downgradeMultiplier, isShown)
            : null;
    }

    public void Tick() => _readout.Visible = _isShown();

    private IReadOnlyList<WidgetPhrase.Line> AssembleStrokes()
    {
        string fps = _cyclesPerSecond().ToString("F2", CultureInfo.InvariantCulture);
        string downgrade = _downgradeMultiplier().ToString("F2", CultureInfo.InvariantCulture);
        return
        [
            new WidgetPhrase.Line($"FPS: {fps}", _readout.DefaultTint),
            new WidgetPhrase.Line($"DEG: {downgrade}", _readout.DefaultTint),
        ];
    }
}
