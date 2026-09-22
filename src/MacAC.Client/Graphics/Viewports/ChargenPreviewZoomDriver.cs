using System.Numerics;

namespace MacAC.Client.Graphics;

internal sealed class ChargenPreviewZoomDriver
{
    private const double InvalidIntervalSentinel = -0.1;

    private readonly uint _lineageIdent;
    private readonly ClientChargenPreviewAnimator _animator;
    private Vector3 _beginEyePt;
    private Vector3 _markEyePt;
    private double _animBeginMoment;
    private double _animInterval;
    private bool _shouldTween;

    public ChargenPreviewZoomDriver(uint lineageIdent, ClientChargenPreviewCamera cam, ClientChargenPreviewAnimator animator)
    {
        ArgumentNullException.ThrowIfNull(cam);
        ArgumentNullException.ThrowIfNull(animator);
        _lineageIdent = lineageIdent;
        Camera = cam;
        _animator = animator;
    }

    public ClientChargenPreviewCamera Camera { get; }

    public bool IsZoomedIn => _animator.IsZoomedIn;

    public void ZoomIn()
    {
        if (IsZoomedIn)
            return;
        BeginTween(ClientChargenPreviewCamera.LocateDefaultEyePt(_lineageIdent));
        _animator.AssignZoomedIn(true);
    }

    public void ZoomOut()
    {
        if (!IsZoomedIn)
            return;
        BeginTween(ClientChargenPreviewCamera.LocateZoomedOutEyePt(_lineageIdent));
        _animator.AssignZoomedIn(false);
    }

    public void Tick(double instant)
    {
        if (!_shouldTween)
            return;

        if (_animInterval <= 0d)
        {
            _animInterval = ClientChargenPreviewCamera.ZoomTweenIntervalSecs;
            _animBeginMoment = instant;
        }

        double passed = instant - _animBeginMoment;
        if (passed >= _animInterval)
        {
            _shouldTween = false;
            passed = _animInterval;
        }

        float t = (float)(passed / _animInterval);
        Camera.Eye = Vector3.Lerp(_beginEyePt, _markEyePt, t);
    }

    private void BeginTween(Vector3 markEyePt)
    {
        _beginEyePt = Camera.Eye;
        _markEyePt = markEyePt;
        _shouldTween = true;
        _animInterval = InvalidIntervalSentinel;
    }
}
