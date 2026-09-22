using MacAC.Cockpit.Input;

namespace MacAC.Client.Controls;

internal readonly record struct FlyCameraFeed(
    bool Forward,
    bool Left,
    bool Backward,
    bool Right,
    bool Up,
    bool Down,
    bool Boost);

internal readonly record struct FollowCameraAdjustmentInput(
    bool ZoomIn,
    bool ZoomOut,
    bool Raise,
    bool Lower,
    bool RotateLeft,
    bool RotateRight);

internal interface ICameraCycleFeedOrigin
{
    bool IsOnHand { get; }
    FlyCameraFeed GrabFly();
    FollowCameraAdjustmentInput GrabPursueAdjustment();
}

internal sealed class RouterCameraInputSource : ICameraCycleFeedOrigin
{
    private InputRouter? _router;

    public bool IsOnHand => _router is not null;

    public void Bind(InputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (_router is not null && !ReferenceEquals(_router, router))
            throw new InvalidOperationException(
                "The camera input source is by now bound to another dispatcher");
        _router = router;
    }

    public void Loosen(InputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (ReferenceEquals(_router, router))
            _router = null;
    }

    public FlyCameraFeed GrabFly()
    {
        return _router is not { } router
            ? default
            : new FlyCameraFeed(
            router.IsActPinned(FeedAct.MovementForward),
            router.IsActPinned(FeedAct.MovementTurnLeft),
            router.IsActPinned(FeedAct.MovementBackup),
            router.IsActPinned(FeedAct.MovementTurnRight),
            router.IsActPinned(FeedAct.MovementJump),
            router.IsActPinned(FeedAct.EngineFlyDown),
            router.IsActPinned(FeedAct.MovementRunLock));
    }

    public FollowCameraAdjustmentInput GrabPursueAdjustment()
    {
        return _router is not { } router
            ? default
            : new FollowCameraAdjustmentInput(
            router.IsActPinned(FeedAct.CameraZoomIn)
                || router.IsActPinned(FeedAct.CameraMoveToward)
                || router.IsActPinned(FeedAct.CameraAlternateMoveToward),
            router.IsActPinned(FeedAct.CameraZoomOut)
                || router.IsActPinned(FeedAct.CameraMoveAway)
                || router.IsActPinned(FeedAct.CameraAlternateMoveAway),
            router.IsActPinned(FeedAct.CameraRaise)
                || router.IsActPinned(FeedAct.CameraRotateUp)
                || router.IsActPinned(FeedAct.CameraAlternateRotateUp),
            router.IsActPinned(FeedAct.CameraLower)
                || router.IsActPinned(FeedAct.CameraRotateDown)
                || router.IsActPinned(FeedAct.CameraAlternateRotateDown),
            router.IsActPinned(FeedAct.CameraRotateLeft)
                || router.IsActPinned(FeedAct.CameraAlternateRotateLeft),
            router.IsActPinned(FeedAct.CameraRotateRight)
                || router.IsActPinned(FeedAct.CameraAlternateRotateRight));
    }
}
