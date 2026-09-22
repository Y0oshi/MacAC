namespace MacAC.Client.Shell.Panels;

public sealed class JumpPowerbarDriver : IRetainedPaneDriver
{
    public const uint LayoutId = 0x21000072u;
    public const uint GaugeIdent = 0x10000034u;
    public const uint LeapMannerPhase = 0x10000042u;

    private readonly WidgetGauge _gauge;
    private readonly Func<JumpChargeCapture> _capture;
    private readonly Action<bool> _setPaneShown;
    private float _strength;
    private bool _isCharging;
    private bool _hasSynced;
    private bool _destroyed;

    private JumpPowerbarDriver(
        WidgetGauge gauge,
        Func<JumpChargeCapture> capture,
        Action<bool> setPaneShown)
    {
        _gauge = gauge;
        _capture = capture;
        _setPaneShown = setPaneShown;
        _gauge.Populate = () => _strength;
        SynchronizeVis(force: true);
    }

    public static JumpPowerbarDriver? Bind(
        ImportedArrangement arrangement,
        Func<JumpChargeCapture> capture,
        Action<bool> setPaneShown)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(setPaneShown);

        return arrangement.SeekElem(GaugeIdent) is not WidgetGauge gauge
            || !gauge.TrySetCanonPhase(LeapMannerPhase)
            ? null
            : new JumpPowerbarDriver(gauge, capture, setPaneShown);
    }

    public void Tick() => SynchronizeVis(force: false);

    public void AlignVis() => SynchronizeVis(force: true);

    public void OnShown() => SynchronizeVis(force: false);

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        _strength = 0f;
        _gauge.Populate = () => null;
    }

    private void SynchronizeVis(bool force)
    {
        if (_destroyed) return;

        var phase = _capture();
        _strength = phase.IsCharging ? Math.Clamp(phase.Power, 0f, 1f) : 0f;

        if (force || !_hasSynced || phase.IsCharging != _isCharging)
        {
            _isCharging = phase.IsCharging;
            _hasSynced = true;
            _setPaneShown(_isCharging);
        }
    }
}
