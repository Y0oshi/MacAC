using System.Globalization;
using System.Numerics;
using MacAC.Client.Graphics;
using MacAC.Cockpit.Input;
using MacAC.Mechanics.Drawing;
using Silk.NET.Input;

namespace MacAC.Client.Controls;

internal sealed partial class CameraPointerInputDriver
{
    public static CameraPointerInputDriver Create(
        IReadOnlyList<IMouse> mice,
        HostQuiescenceTurnstile stillness,
        IFeedGrabOrigin grab,
        AvatarModeLedger avatarManner,
        CameraDriver cam,
        FollowCameraInputLedger pursue,
        IMouseFeed mouse,
        PointerPositionLedger ptr,
        IFeedMonotonicTimer timer)
    {
        ArgumentNullException.ThrowIfNull(mice);
        return mice.Count is 0
            ? throw new ArgumentException("At least one mouse is needed", nameof(mice))
            : new CameraPointerInputDriver(
            mice.Select(static contender =>
                (ICrudePtrCanvas)new SilkCrudePtrCanvas(contender)).ToArray(),
            new SilkPtrCursorMannerMark(mice[0]),
            stillness,
            grab,
            avatarManner,
            cam,
            pursue,
            mouse,
            ptr,
            timer);
    }

    public bool IsDisposalComplete =>
        !_camAffixed && _canvasAffixed.All(static affixed => !affixed);

    public float EngagedSensitivity
    {
        get
        {
            if (_avatarManner.IsPlayerMode && _cam.IsPursueManner)
                return _pursue.Sensitivity;
            return _cam.IsFlyManner ? _flySensitivity : _orbitSensitivity;
        }
    }

    public bool RmbOrbitPinned => _pursue.RmbOrbitHeld;

    public void FastenRaw()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _teardownAsked) is not 0,
            this);
        if (_fastenBegun)
            throw new InvalidOperationException("Raw pointer attachment has by now started");
        _fastenBegun = true;

        try
        {
            for (int idx = 0; idx < _canvases.Count; ++idx)
            {
                _canvasAffixed[idx] = true;
                _canvases[idx].AppendPointerRelocate(_pointerMoved);
            }

            _camAffixed = true;
            _cam.ModeChanged += _camMannerAltered;
            Volatile.Write(ref _engaged, 1);
        }
        catch (Exception fastenProblem)
        {
            Disengage();
            try
            {
                SecureUnfastenTransaction().CompleteOrThrow();
            }
            catch (Exception undoProblem)
            {
                throw new AggregateException(
                    "Raw pointer registration and rollback both failed",
                    new InvalidOperationException(
                        "Raw pointer registration failed", fastenProblem),
                    undoProblem);
            }

            throw new InvalidOperationException(
                "Raw pointer registration failed and was rolled back",
                fastenProblem);
        }
    }

    public void AttachGameplayCycle(GameplayInputFrameDriver gameplayCycle)
    {
        ArgumentNullException.ThrowIfNull(gameplayCycle);
        if (_gameplayCycle is not null
            && !ReferenceEquals(_gameplayCycle, gameplayCycle))
        {
            throw new InvalidOperationException(
                "The raw pointer owner is by now bound to another gameplay frame");
        }

        _gameplayCycle = gameplayCycle;
    }

    public IDisposable AttachGameplayCyclePossessed(
        GameplayInputFrameDriver gameplayCycle)
    {
        ArgumentNullException.ThrowIfNull(gameplayCycle);
        if (_gameplayCycle is not null)
        {
            throw new InvalidOperationException(
                "The raw pointer owner by now has a gameplay frame");
        }

        _gameplayCycle = gameplayCycle;
        return new GameplayFrameWiring(this, gameplayCycle);
    }

    public void LoosenGameplayCycle(GameplayInputFrameDriver gameplayCycle)
    {
        ArgumentNullException.ThrowIfNull(gameplayCycle);
        if (ReferenceEquals(_gameplayCycle, gameplayCycle))
            _gameplayCycle = null;
    }

    public void ProcessFocusAltered(bool focused)
    {
        if (!focused)
            _gameplayCycle?.EndMouseLook();
    }

    public void ProcessRoll(FeedAct action)
    {
        float dir = action switch
        {
            FeedAct.ScrollUp => 1f,
            FeedAct.ScrollDown => -1f,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        if (_avatarManner.IsPlayerMode && _cam.IsPursueManner)
        {
            if (CameraTelemetry.UseCanonPursueCam && _pursue.Retail is not null)
                _pursue.Retail.TuneGap(-dir * 0.8f);
            else
                _pursue.Legacy?.TweakGap(-dir * 0.8f);
        }
        else if (!_cam.IsFlyManner)
        {
            _cam.Orbit.Distance = Math.Clamp(
                _cam.Orbit.Distance - dir * 20f,
                50f,
                2000f);
        }
    }

    public bool ProcessCamAct(
        FeedAct act,
        ActivationKind activation)
    {
        if (activation != ActivationKind.Press
            || !_avatarManner.IsPlayerMode
            || !_cam.IsPursueManner)

            return false;

        bool handled = act is
            FeedAct.CameraViewDefault
            or FeedAct.CameraAlternateViewDefault
            or FeedAct.CameraViewFirstPerson
            or FeedAct.CameraAlternateViewFirstPerson
            or FeedAct.CameraViewLookDown
            or FeedAct.CameraAlternateViewLookDown
            or FeedAct.CameraViewMapMode
            or FeedAct.CameraAlternateViewMapMode;
        if (!handled)
            return false;

        ImposeCamPreset(_pursue.Retail, act);
        ImposeCamPreset(_pursue.Legacy, act);
        return true;
    }

    public string TuneSensitivity(float factor)
    {
        string manner;
        float latest;
        if (_avatarManner.IsPlayerMode && _cam.IsPursueManner)
        {
            manner = "Chase";
            latest = _pursue.Sensitivity;
        }
        else if (_cam.IsFlyManner)
        {
            manner = "Fly";
            latest = _flySensitivity;
        }
        else
        {
            manner = "Orbit";
            latest = _orbitSensitivity;
        }

        float upcoming = MathF.Min(3f, MathF.Max(0.005f, latest * factor));
        if (manner == "Chase")
            _pursue.Sensitivity = upcoming;
        else if (manner == "Fly")
            _flySensitivity = upcoming;
        else
            _orbitSensitivity = upcoming;

        return string.Create(CultureInfo.InvariantCulture, $"{manner} sens {upcoming:F3}x");
    }

    public void Disengage()
    {
        Interlocked.Exchange(ref _engaged, 0);
        _pursue.RmbOrbitHeld = false;
    }

    // Release presentation state only after live-session retirement
    public void FreePointerGazeFollowingSessSunset() =>
        _gameplayCycle?.EndMouseLook();

    public void Dispose()
    {
        Interlocked.Exchange(ref _teardownAsked, 1);
        Disengage();
        SecureUnfastenTransaction().CompleteOrThrow();
    }

    private static void ImposeCamPreset(
        CanonFollowCamera? cam,
        FeedAct act)
    {
        if (cam is null)
            return;
        switch (act)
        {
            case FeedAct.CameraViewDefault:
            case FeedAct.CameraAlternateViewDefault:
                cam.AssignCanonDefaultLens();
                break;
            case FeedAct.CameraViewFirstPerson:
            case FeedAct.CameraAlternateViewFirstPerson:
                cam.AssignCanonLeadPersonLens();
                break;
            case FeedAct.CameraViewLookDown:
            case FeedAct.CameraAlternateViewLookDown:
                cam.FlipCanonGazeDownLens();
                break;
            case FeedAct.CameraViewMapMode:
            case FeedAct.CameraAlternateViewMapMode:
                cam.FlipCanonLookupMannerLens();
                break;
        }
    }

    private static void ImposeCamPreset(
        FollowCamera? cam,
        FeedAct act)
    {
        if (cam is null)
            return;
        switch (act)
        {
            case FeedAct.CameraViewDefault:
            case FeedAct.CameraAlternateViewDefault:
                cam.ApplyCanonDefaultLens();
                break;
            case FeedAct.CameraViewFirstPerson:
            case FeedAct.CameraAlternateViewFirstPerson:
                cam.ApplyCanonLeadPersonLens();
                break;
            case FeedAct.CameraViewLookDown:
            case FeedAct.CameraAlternateViewLookDown:
                cam.SwitchCanonGazeDownLens();
                break;
            case FeedAct.CameraViewMapMode:
            case FeedAct.CameraAlternateViewMapMode:
                cam.SwitchCanonLookupMannerLens();
                break;
        }
    }

    private void ImposeCurForCamManner()
    {
        if (Volatile.Read(ref _engaged) is 0)
            return;

        if (_gameplayCycle?.PointerGazeEngaged == true && !_cam.IsPursueManner)
            _gameplayCycle.EndMouseLook();

        _cur.CurManner = _cam.IsFlyManner
            ? CursorMode.Raw
            : CursorMode.Normal;
    }

    private void OnPointerMoved(Vector2 locus) =>
        _stillness.Invoke(() => OnPointerMovedBranch(locus));

    private void OnCamMannerAltered(bool _) =>
        _stillness.Invoke(ImposeCurForCamManner);

    private void OnPointerMovedBranch(Vector2 locus)
    {
        if (Volatile.Read(ref _engaged) is 0)
            return;

        if (_grab.WantCaptureMouse)
        {
            _ptr.X = locus.X;
            _ptr.Y = locus.Y;
            return;
        }

        float dx = locus.X - _ptr.X;
        float dy = locus.Y - _ptr.Y;

        if (_avatarManner.IsPlayerMode && _cam.IsPursueManner
            && _pursue.Legacy is not null)
        {
            float sensitivity = _pursue.Sensitivity;
            if (_gameplayCycle?.PointerGazeEngaged == true)
            {
                _gameplayCycle.EnqueueRawPointerDiff(dx, dy);
            }
            else if (_pursue.RmbOrbitHeld)
            {
                if (CameraTelemetry.UseCanonPursueCam
                    && _pursue.Retail is not null)
                {
                    var (filteredDx, filteredDy) = _pursue.Retail.FilterMouseDelta(
                        rawX: dx,
                        rawY: dy,
                        weight: 0.5f,
                        instantSec: _clock.InstantSecs);
                    const float canonPointerScaling = 0.0666666701f;
                    _pursue.Retail.TuneYaw(-filteredDx * sensitivity * canonPointerScaling);
                    _pursue.Retail.TunePitch(filteredDy * sensitivity * canonPointerScaling);
                }
                else
                {
                    _pursue.Legacy.YawOffset -= dx * 0.004f * sensitivity;
                    _pursue.Legacy.TweakPitch(dy * 0.003f * sensitivity);
                }
            }
        }
        else if (_cam.IsFlyManner)
        {
            _cam.Fly.Look(dx * _flySensitivity, dy * _flySensitivity);
        }
        else if (_mouse.IsHeld(MouseButton.Left))
        {
            _cam.Orbit.Yaw -= dx * 0.005f * _orbitSensitivity;
            _cam.Orbit.Pitch = Math.Clamp(
                _cam.Orbit.Pitch + dy * 0.005f * _orbitSensitivity,
                0.1f,
                1.5f);
        }

        _ptr.X = locus.X;
        _ptr.Y = locus.Y;
    }

    private AssetShutdownTransaction SecureUnfastenTransaction()
    {
        if (_unfasten is not null)
            return _unfasten;

        var ops = new List<AssetShutdownOp>
        {
            new("camera mode", DropCamManner),
        };
        for (int idx = _canvases.Count - 1; idx >= 0; --idx)
        {
            int ordinal = idx;
            ops.Add(new AssetShutdownOp(
                $"raw mouse {ordinal}",
                () => DropCanvas(ordinal)));
        }

        _unfasten = new AssetShutdownTransaction(
            new AssetShutdownJuncture("raw pointer callbacks", [.. ops]));
        return _unfasten;
    }

    private void DropCamManner()
    {
        if (!_camAffixed)
            return;
        _cam.ModeChanged -= _camMannerAltered;
        _camAffixed = false;
    }

    private void DropCanvas(int ordinal)
    {
        if (!_canvasAffixed[ordinal])
            return;
        _canvases[ordinal].DropPointerRelocate(_pointerMoved);
        _canvasAffixed[ordinal] = false;
    }
}
