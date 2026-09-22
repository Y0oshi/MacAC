namespace MacAC.Cockpit.Input;

public sealed class MouseLookTracker(Action<float> applyHorizontalAdjustment)
{
    public const float IdleZeroDelaySecs = 0.2f;
    private const int SettleSpecimens = 5;

    private readonly Action<float> _pivot = applyHorizontalAdjustment ?? throw new ArgumentNullException(nameof(applyHorizontalAdjustment));
    private int _specimensInPull;
    private float _queuedX;
    private float _queuedY;
    private float _previousFeedAt;

    public bool Active { get; private set; }

    public float GrabbedCurX { get; private set; }

    public float GrabbedCurY { get; private set; }

    public float SensitivityPerPixel { get; set; } = 0.0666666701f;

    public void Press(float curX, float curY, bool wantGrabPointer, float instantSecs = 0f)
    {
        if (wantGrabPointer || Active)
            return;
        Active = true;
        GrabbedCurX = curX;
        GrabbedCurY = curY;
        Reset();
        _previousFeedAt = instantSecs;
    }

    public void Release()
    {
        if (!Active)
            return;
        Active = false;
        Reset();
    }

    /// <summary>The UI taking the mouse ends a drag in progress.</summary>
    public void OnWantGrabPointerAltered(bool wantGrabPointer)
    {
        if (Active && wantGrabPointer)
            Release();
    }

    public void EnqueueDiff(float dx, float dy = 0f)
    {
        if (Active && float.IsFinite(dx) && float.IsFinite(dy))
        {
            _queuedX += dx;
            _queuedY += dy;
        }
    }

    public bool TryTakeRawSample(float instantSecs, out float dx, out float dy)
    {
        dx = _queuedX;
        dy = _queuedY;
        _queuedX = 0f;
        _queuedY = 0f;
        if (!Active)
        {
            dx = dy = 0f;
            return false;
        }

        if (dx != 0f || dy != 0f)
        {
            _previousFeedAt = instantSecs;
            return true;
        }
        return instantSecs > _previousFeedAt + IdleZeroDelaySecs;
    }

    public void ApplyDelta(float dx, float extraSensitivity)
    {
        if (!Active)
            return;

        float adjustment = -dx * SensitivityPerPixel * extraSensitivity;
        if (adjustment == 0f)
        {
            _specimensInPull = 0;
            return;
        }
        if (++_specimensInPull > SettleSpecimens)
            _pivot(adjustment);
    }

    private void Reset()
    {
        _specimensInPull = 0;
        _queuedX = 0f;
        _queuedY = 0f;
    }
}
