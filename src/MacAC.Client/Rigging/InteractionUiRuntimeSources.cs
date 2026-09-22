using System.Numerics;
using MacAC.Client.Dealing;
using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Client.Shell;
using MacAC.Client.Shell.Panels;
using MacAC.Client.Shell.Testing;
using MacAC.Mechanics.Gear;
using MacAC.Sim;
using MacAC.Wire;
using MacAC.Wire.Messages;
using Silk.NET.Windowing;

namespace MacAC.Client.Rigging;

internal sealed partial class DeferredGameEngineStateDirectives
{
    private readonly object _latch = new();

    private ISimCoreLens? _lens;

    private ISimCoreDirectives? _commands;

    private bool _deactivated;

    private sealed class ExpectedEngineWiring(
        DeferredGameEngineStateDirectives holder,
        ISimCoreLens lens,
        ISimCoreDirectives directives)
        : IDisposable
    {
        private DeferredGameEngineStateDirectives? _holder = holder;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Release(lens, directives);
    }
}

internal sealed class DeferredOnlineSessionWidgetAuthority
    : IOnlineInRealmSource,
      IOnlineRealmSessionSource
{
    private readonly object _latch = new();
    private IOnlineWidgetSessionTarget? _mark;
    private bool _deactivated;

    public bool IsInWorld
    {
        get
        {
            lock (_latch)
                return !_deactivated && _mark?.IsInWorld == true;
        }
    }

    public RealmSession? LatestSess
    {
        get
        {
            lock (_latch)
                return !_deactivated ? _mark?.LatestSess : null;
        }
    }

    public IDirectiveBus Commands
    {
        get
        {
            lock (_latch)
                return !_deactivated
                    ? _mark?.Commands ?? NullDirectiveBus.Instance
                    : NullDirectiveBus.Instance;
        }
    }

    public string? AcctMoniker => LatestSess?.Characters?.AccountName;

    public LinkStatusFrame ConnectCondition =>
        LatestSess?.LinkStatus ?? LinkStatusFrame.Disconnected;

    public IDisposable Bind(IOnlineWidgetSessionTarget mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_mark is not null)
            {
                throw new InvalidOperationException(
                    "The retained-UI live-session authority is by now bound");
            }

            _mark = mark;
        }

        return new ExpectedOwnerWiring<IOnlineWidgetSessionTarget>(
            mark,
            Release);
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _mark = null;
        }
    }

    public bool TryUseGear(uint oid, Action<string>? trace = null)
    {
        var sess = LatestSess;
        if (!IsInWorld || sess is null)
            return false;

        uint series = sess.UpcomingPlayActSeries();
        sess.TransmitPlayAct(InteractAsks.AssembleUse(series, oid));
        trace?.Invoke($"[UI] toolbar use-item guid=0x{oid:X8} seq={series}");
        return true;
    }

    private void Release(IOnlineWidgetSessionTarget anticipated)
    {
        lock (_latch)
        {
            if (ReferenceEquals(_mark, anticipated))
                _mark = null;
        }
    }
}

internal sealed class DeferredPickingWidgetAuthority
{
    private readonly object _latch = new();
    private IRetainedWidgetPickingProbe? _ask;
    private PickingDealingDriver? _interactions;
    private bool _deactivated;

    public IDisposable Bind(
        IRetainedWidgetPickingProbe ask,
        PickingDealingDriver interactions)
    {
        ArgumentNullException.ThrowIfNull(ask);
        ArgumentNullException.ThrowIfNull(interactions);
        lock (_latch)
        {
            ObjectDisposedException.ThrowIf(_deactivated, this);
            if (_ask is not null || _interactions is not null)
            {
                throw new InvalidOperationException(
                    "The retained-UI selection authority is by now bound");
            }

            _ask = ask;
            _interactions = interactions;
        }

        return new ExpectedPickingWiring(this, ask, interactions);
    }

    public void Deactivate()
    {
        lock (_latch)
        {
            _deactivated = true;
            _ask = null;
            _interactions = null;
        }
    }

    public uint? ChooseAtCursor(bool includeSelf)
    {
        PickingDealingDriver? mark;
        lock (_latch)
            mark = !_deactivated ? _interactions : null;
        return mark?.ChooseAtCur(includeSelf);
    }

    public void DispatchUse(uint oid)
    {
        PickingDealingDriver? mark;
        lock (_latch)
            mark = !_deactivated ? _interactions : null;
        mark?.TransmitUse(oid);
    }

    public void RequestUse(uint oid, ItemUseHold reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        PickingDealingDriver? mark;
        lock (_latch)
            mark = !_deactivated ? _interactions : null;
        if (mark is null)
            reservation.AbortPriorRelay();
        else
            mark.ReqUse(oid, reservation);
    }

    public void DispatchLift(uint gearOid, uint destVesselIdent, int stance)
    {
        PickingDealingDriver? mark;
        lock (_latch)
            mark = !_deactivated ? _interactions : null;
        mark?.TransmitLift(gearOid, destVesselIdent, stance);
    }

    public uint? PickClosestFightingObjective(bool unhideToast)
    {
        PickingDealingDriver? mark;
        lock (_latch)
            mark = !_deactivated ? _interactions : null;
        return mark?.PickClosestFightingMark(unhideToast);
    }

    public bool IsWithinExternalVesselUseRange(uint markOid)
    {
        IRetainedWidgetPickingProbe? ask;
        lock (_latch)
            ask = !_deactivated ? _ask : null;
        return ask?.IsWithinExternalVesselUseSpan(markOid) != false;
    }

    public bool ShouldShowHealth(uint oid)
    {
        IRetainedWidgetPickingProbe? ask;
        lock (_latch)
            ask = !_deactivated ? _ask : null;
        return ask?.ShouldUnhideHealth(oid) == true;
    }

    public VividMarkDetails? LocateVividObjectiveDetails(uint oid)
    {
        IRetainedWidgetPickingProbe? ask;
        lock (_latch)
            ask = !_deactivated ? _ask : null;
        return ask?.LocateVividMarkDetails(oid);
    }

    private void Release(
        IRetainedWidgetPickingProbe anticipatedAsk,
        PickingDealingDriver anticipatedInteractions)
    {
        lock (_latch)
        {
            if (ReferenceEquals(_ask, anticipatedAsk)
                && ReferenceEquals(_interactions, anticipatedInteractions))
            {
                _ask = null;
                _interactions = null;
            }
        }
    }

    private sealed class ExpectedPickingWiring(
        DeferredPickingWidgetAuthority holder,
        IRetainedWidgetPickingProbe ask,
        PickingDealingDriver interactions) : IDisposable
    {
        private DeferredPickingWidgetAuthority? _holder = holder;
        private readonly IRetainedWidgetPickingProbe _ask = ask;
        private readonly PickingDealingDriver _interactions = interactions;

        public void Dispose() =>
            Interlocked.Exchange(ref _holder, null)?.Release(_ask, _interactions);
    }
}

internal sealed class DeferredPickingViewPlaneSource
{
    private IPickingViewPlaneSource? _mark;
    private bool _deactivated;

    public IDisposable Bind(IPickingViewPlaneSource mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null)
        {
            throw new InvalidOperationException(
                "The retained-UI selection view plane is by now bound");
        }

        _mark = mark;
        return new ExpectedOwnerWiring<IPickingViewPlaneSource>(mark, Release);
    }

    public IClientCamera Apply(IClientCamera cam)
    {
        return !_deactivated && _mark is { } mark
            ? mark.ApplyViewPlane(cam)
            : cam;
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private void Release(IPickingViewPlaneSource anticipated)
    {
        if (ReferenceEquals(_mark, anticipated))
            _mark = null;
    }
}

internal sealed class PickingCameraSource(
    CameraDriver camera,
    IView window,
    DeferredPickingViewPlaneSource viewPlane)
{
    private readonly CameraDriver _cam = camera ?? throw new ArgumentNullException(nameof(camera));
    private readonly IView _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly DeferredPickingViewPlaneSource _lensPlane = viewPlane ?? throw new ArgumentNullException(nameof(viewPlane));

    public PickingCameraCapture Snapshot()
    {
        IClientCamera cam = _lensPlane.Apply(_cam.Active);
        return new PickingCameraCapture(
            cam.View,
            cam.Projection,
            new Vector2(_window.Size.X, _window.Size.Y));
    }

    public (Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport) WidgetCapture()
    {
        var capture = Snapshot();
        return (capture.View, capture.Projection, capture.Viewport);
    }
}

internal sealed class DeferredRadarCaptureSource
{
    private RadarCaptureSupplier? _mark;
    private bool _deactivated;

    public WidgetRadarCapture Snapshot()
    {
        return !_deactivated && _mark is { } mark
            ? mark.AssembleCapture()
            : WidgetRadarCapture.Empty;
    }

    public IDisposable Bind(RadarCaptureSupplier mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null)
            throw new InvalidOperationException("The retained-UI radar is by now bound");
        _mark = mark;
        return new ExpectedOwnerWiring<RadarCaptureSupplier>(mark, Release);
    }

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private void Release(RadarCaptureSupplier anticipated)
    {
        if (ReferenceEquals(_mark, anticipated))
            _mark = null;
    }
}

internal sealed class DeferredStashContainerSource
{
    private CanonWidgetEngine? _runtime;
    private bool _deactivated;

    public uint Current(uint avatarOid)
    {
        return !_deactivated
            ? _runtime?.SatchelBoardDriver?.LatestOpenVesselIdent ?? avatarOid
            : avatarOid;
    }

    public IDisposable Bind(CanonWidgetEngine core)
    {
        ArgumentNullException.ThrowIfNull(core);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_runtime is not null)
            throw new InvalidOperationException("The retained inventory container is by now bound");
        _runtime = core;
        return new ExpectedOwnerWiring<CanonWidgetEngine>(core, Release);
    }

    public void Deactivate()
    {
        _deactivated = true;
        _runtime = null;
    }

    private void Release(CanonWidgetEngine anticipated)
    {
        if (ReferenceEquals(_runtime, anticipated))
            _runtime = null;
    }
}

internal sealed class DeferredRealmLifespanAutopilotEngine
    : ICanonWidgetAutopilotEngine
{
    private ICanonWidgetAutopilotEngine? _mark;
    private bool _deactivated;

    public bool IsRealmReady => !_deactivated && _mark?.IsRealmReady == true;
    public bool IsRealmViewRectShown =>
        !_deactivated && _mark?.IsRealmViewRectShown == true;
    public int GatewayMaterializationCount =>
        !_deactivated ? _mark?.GatewayMaterializationCount ?? 0 : 0;
    public int RasterizeBundlePerformanceSpecimenTally =>
        !_deactivated ? _mark?.RasterizeBundlePerformanceSpecimenTally ?? 0 : 0;
    public bool RasterizeBundleFailedToCanon =>
        !_deactivated && _mark?.RasterizeBundleFailedToCanon == true;
    public CanonWidgetAutopilotRenderPackStatus RasterizeBundleCondition
    {
        get
        {
            return !_deactivated
            ? _mark?.RasterizeBundleCondition
                ?? CanonWidgetAutopilotRenderPackStatus.Retail
            : CanonWidgetAutopilotRenderPackStatus.Retail;
        }
    }

    public int FramebufferWidth =>
        !_deactivated ? _mark?.FramebufferWidth ?? 0 : 0;
    public int FramebufferHeight =>
        !_deactivated ? _mark?.FramebufferHeight ?? 0 : 0;

    public bool TryPickRasterizeBundle(string presetIdent, out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryPickRasterizeBundle(presetIdent, out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool TryDeactivateRasterizeBundle(out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryDeactivateRasterizeBundle(out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool TryReenableRasterizeBundle(out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryReenableRasterizeBundle(out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool TryRescaleFramebuffer(int width, int height, out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryRescaleFramebuffer(width, height, out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool TryRestartRasterizeBundlePerformance(out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryRestartRasterizeBundlePerformance(out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool TryReqClientShut(out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryReqClientShut(out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public IDisposable Bind(ICanonWidgetAutopilotEngine mark)
    {
        ArgumentNullException.ThrowIfNull(mark);
        ObjectDisposedException.ThrowIf(_deactivated, this);
        if (_mark is not null)
            throw new InvalidOperationException("World lifecycle automation is by now bound");
        _mark = mark;
        return new ExpectedOwnerWiring<ICanonWidgetAutopilotEngine>(mark, Release);
    }

    public bool TryReqCheckpoint(
        string label,
        out ICanonWidgetAutopilotCheckpoint? checkpoint,
        out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryReqCheckpoint(label, out checkpoint, out problem);
        checkpoint = null;
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public void AbortCheckpoint(ICanonWidgetAutopilotCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (!_deactivated && _mark is { } mark)
            mark.AbortCheckpoint(checkpoint);
    }

    public bool TryReqScreenshot(string label, out string problem)
    {
        if (!_deactivated && _mark is { } mark)
            return mark.TryReqScreenshot(label, out problem);
        problem = "world lifecycle automation is not bound";
        return false;
    }

    public bool IsScreenshotDone(string label) =>
        !_deactivated && _mark?.IsScreenshotDone(label) == true;

    public void Deactivate()
    {
        _deactivated = true;
        _mark = null;
    }

    private void Release(ICanonWidgetAutopilotEngine anticipated)
    {
        if (ReferenceEquals(_mark, anticipated))
            _mark = null;
    }
}

internal sealed class ExpectedOwnerWiring<T>(T expected, Action<T> release) : IDisposable where T : class
{
    private Action<T>? _free = release ?? throw new ArgumentNullException(nameof(release));
    private readonly T _anticipated = expected ?? throw new ArgumentNullException(nameof(expected));

    public void Dispose() => Interlocked.Exchange(ref _free, null)?.Invoke(_anticipated);
}
