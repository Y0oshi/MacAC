using MacAC.Client.Graphics;
using MacAC.Client.Link;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Drawing;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal interface IFeedMonotonicTimer
{
    float InstantSecs { get; }
}

internal sealed class SurroundingsFeedMonotonicTimer : IFeedMonotonicTimer
{
    public float InstantSecs => (float)(Environment.TickCount64 / 1000.0);
}

internal interface IPtrLocusOrigin
{
    float X { get; }
    float Y { get; }
}

internal sealed class PointerPositionLedger : IPtrLocusOrigin
{
    public float X { get; set; }
    public float Y { get; set; }
}

internal interface IPointerGazeCursor
{
    bool HasStoredManner { get; }
    void Hide();
    void Restore();
}

internal interface IMouseLookInputFrameDriver
{
    bool Active { get; }
    bool ProcessPointerAction(FeedAct act, ActivationKind activation);
    void EnqueueRawDiff(float dx, float dy);
    void Tick();
    void FinishAndRevertCur();
    void FinishForLifecycle();
    void ResetSession();
}

internal sealed class SilkPointerGazeCursor(IMouse mouse) : IPointerGazeCursor
{
    private readonly IMouse _pointer = mouse ?? throw new ArgumentNullException(nameof(mouse));
    private CursorMode? _storedManner;

    public bool HasStoredManner => _storedManner.HasValue;

    public void Hide()
    {
        _storedManner = _pointer.Cursor.CursorMode;
        _pointer.Cursor.CursorMode = CursorMode.Hidden;
    }

    public void Restore()
    {
        _pointer.Cursor.CursorMode = _storedManner ?? CursorMode.Normal;
        _storedManner = null;
    }
}

internal interface IFollowCameraSource
{
    FollowCamera? Legacy { get; }
    CanonFollowCamera? Retail { get; }
}

internal sealed class FollowCameraInputLedger : IFollowCameraSource
{
    public FollowCamera? Legacy { get; set; }
    public CanonFollowCamera? Retail { get; set; }
    public float Sensitivity { get; set; } = 0.15f;
    public bool RmbOrbitHeld { get; set; }
}

internal sealed class MouseLookDriver : IMouseLookInputFrameDriver
{
    private readonly IMouseFeed _pointerSrc;
    private readonly IPtrLocusOrigin _ptr;
    private readonly IAvatarModeSource _avatarManner;
    private readonly ISimAvatarDriverSource _playerController;
    private readonly CameraDriver _cam;
    private readonly FollowCameraInputLedger _pursue;
    private readonly ILocomotionInputSource _travelFeed;
    private readonly AvatarOutboundDriver _outgoing;
    private readonly IOnlineRealmSessionSource _session;
    private readonly IPointerGazeCursor _cur;
    private readonly IFeedMonotonicTimer _clock;
    private readonly MouseLookTracker _phase;
    private bool _previousWantGrabPointer;

    public MouseLookDriver(
        IMouseFeed mouseSource,
        IPtrLocusOrigin pointer,
        IAvatarModeSource playerMode,
        ISimAvatarDriverSource playerController,
        CameraDriver camera,
        FollowCameraInputLedger chase,
        ILocomotionInputSource movementInput,
        AvatarOutboundDriver outbound,
        IOnlineRealmSessionSource session,
        IPointerGazeCursor cursor,
        IFeedMonotonicTimer clock)
    {
        _pointerSrc = mouseSource ?? throw new ArgumentNullException(nameof(mouseSource));
        _ptr = pointer ?? throw new ArgumentNullException(nameof(pointer));
        _avatarManner = playerMode ?? throw new ArgumentNullException(nameof(playerMode));
        _playerController = playerController
            ?? throw new ArgumentNullException(nameof(playerController));
        _cam = camera ?? throw new ArgumentNullException(nameof(camera));
        _pursue = chase ?? throw new ArgumentNullException(nameof(chase));
        _travelFeed = movementInput
            ?? throw new ArgumentNullException(nameof(movementInput));
        _outgoing = outbound ?? throw new ArgumentNullException(nameof(outbound));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _cur = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _phase = new MouseLookTracker(ImposeHorizontalAdjustment);
    }

    public bool Active => _phase.Active;

    public bool ProcessPointerAction(FeedAct act, ActivationKind activation)
    {
        if (act == FeedAct.EngineRmbOrbitHold)
        {
            if (activation == ActivationKind.Press)
                _pursue.RmbOrbitHeld = _avatarManner.IsPlayerMode && _cam.IsPursueManner;
            else if (activation == ActivationKind.Release)
                _pursue.RmbOrbitHeld = false;
            return true;
        }

        if (act is not (
            FeedAct.CameraInstantMouseLook
            or FeedAct.CameraActivateAlternateMode))
            return false;

        if (activation == ActivationKind.Press)
            Begin();
        else if (activation == ActivationKind.Release)
            FinishAndRevertCur();
        return true;
    }

    public void EnqueueRawDiff(float dx, float dy) => _phase.EnqueueDiff(dx, dy);

    public void Tick()
    {
        bool wantGrabPointer = _pointerSrc.WantCaptureMouse;
        if (wantGrabPointer != _previousWantGrabPointer)
        {
            if (wantGrabPointer && _phase.Active)
                FinishAndRevertCur();
            _phase.OnWantGrabPointerAltered(wantGrabPointer);
            _previousWantGrabPointer = wantGrabPointer;
        }

        float instantSecs = _clock.InstantSecs;
        if (!_phase.TryTakeRawSample(instantSecs, out float rawX, out float rawY))
            return;

        var driver = _playerController.Controller;
        if (rawX == 0f && rawY == 0f)
            driver?.StopMouseDrift(_travelFeed.Capture());

        (float filteredX, float filteredY) =
            CameraTelemetry.UseCanonPursueCam && _pursue.Retail is { } canon
                ? canon.FilterMouseDelta(rawX, rawY, weight: 0.5f, instantSec: instantSecs)
                : (rawX, rawY);
        _phase.ApplyDelta(filteredX, _pursue.Sensitivity);
        if (_pursue.Retail is { } canonCam)
            canonCam.TunePitch(filteredY * 0.0666666701f * _pursue.Sensitivity);
        else
            _pursue.Legacy?.TweakPitch(filteredY * 0.003f * _pursue.Sensitivity);
    }

    public void FinishAndRevertCur()
    {
        bool phaseWasEngaged = _phase.Active;
        _phase.Release();

        var driver = _playerController.Controller;
        if (driver is { CanPerformOnlineTravel: true }
            && driver.EndMouseLook(_travelFeed.Capture()))
        {
            _outgoing.TryTransmitTravel(
                _session.LatestSess,
                driver,
                driver.GrabTravelOutcome(pointerGazeSignal: false));
        }

        if (phaseWasEngaged || _cur.HasStoredManner)
            _cur.Restore();
    }

    public void FinishForLifecycle()
    {
        FinishAndRevertCur();
        _pursue.RmbOrbitHeld = false;
    }

    public void ResetSession()
    {
        bool phaseWasEngaged = _phase.Active;
        _phase.Release();
        _pursue.RmbOrbitHeld = false;
        _previousWantGrabPointer = false;
        if (phaseWasEngaged || _cur.HasStoredManner)
            _cur.Restore();
    }

    private void Begin()
    {
        var driver = _playerController.Controller;
        if (!_avatarManner.IsPlayerMode
            || !_cam.IsPursueManner
            || driver is not { State: AvatarPhase.InWorld })

            return;

        float instantSecs = _clock.InstantSecs;
        _phase.Press(
            _ptr.X,
            _ptr.Y,
            _pointerSrc.WantCaptureMouse,
            instantSecs);
        if (!_phase.Active)
            return;

        if (!driver.CommencePointerGaze(_travelFeed.Capture()))
        {
            _phase.Release();
            return;
        }

        _outgoing.TryTransmitTravel(
            _session.LatestSess,
            driver,
            driver.GrabTravelOutcome(pointerGazeSignal: false));
        _cur.Hide();
    }

    private void ImposeHorizontalAdjustment(float adjustment)
    {
        _playerController.Controller?.SubmitPointerPivotAdjustment(
            adjustment,
            _travelFeed.Capture());
    }
}
