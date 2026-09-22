using MacAC.Cockpit.Input;

namespace MacAC.Client.Controls;

internal interface ILocomotionInputSource
    : ISimLocomotionInputSource
{
}

internal sealed class RouterLocomotionInputSource(
    SimAvatarLocomotionLedger movement,
    IFeedGrabOrigin? grab = null) : ILocomotionInputSource
{
    private readonly SimAvatarLocomotionLedger _movement = movement ?? throw new ArgumentNullException(nameof(movement));
    private readonly IFeedGrabOrigin? _grab = grab;
    private InputRouter? _router;

    public bool AutoRunActive => _movement.AutoRunActive;
    public bool IsAvailable => _router is not null;

    public void Bind(InputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (_router is not null && !ReferenceEquals(_router, router))
            throw new InvalidOperationException(
                "The movement input source is by now bound to another dispatcher");
        _router = router;
    }

    public void Unwire(InputRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (ReferenceEquals(_router, router))
            _router = null;
    }

    public LocomotionInput Capture()
    {
        if (_movement.DirectiveInterpreterDisabled)
            return default;

        if (_grab?.DevToolsWantGrabKeyboard == true)
            return default;

        if (_movement.HasDirectiveFeed)
            return _movement.DirectiveFeed with { IsPersistentCommand = true };

        if (_router is not { } router)
            return default;

        bool walking = router.IsActPinned(FeedAct.MovementWalkMode);
        bool ahead = router.IsActPinned(FeedAct.MovementForward);
        return new LocomotionInput(
            Forward: ahead || AutoRunActive,
            Backward: router.IsActPinned(FeedAct.MovementBackup),
            StrafeLeft: router.IsActPinned(FeedAct.MovementStrafeLeft),
            StrafeRight: router.IsActPinned(FeedAct.MovementStrafeRight),
            TurnLeft: router.IsActPinned(FeedAct.MovementTurnLeft),
            TurnRight: router.IsActPinned(FeedAct.MovementTurnRight),
            Run: (_movement.RunAsDefaultMovement != walking) || AutoRunActive,
            Jump: router.IsActPinned(FeedAct.MovementJump));
    }

    public bool ProcessPressedAct(FeedAct act)
    {
        if (act == FeedAct.MovementRunLock)
            return _movement.Execute(
                MacAC.Sim.SimLocomotionDirective.ToggleRunLock);

        if (AutoRunActive && act is (
            FeedAct.MovementForward
            or FeedAct.MovementBackup
            or FeedAct.MovementStop
            or FeedAct.MovementStrafeLeft
            or FeedAct.MovementStrafeRight))

            _movement.AbortAutoExec();

        return false;
    }

    public void ResetSession() => _movement.RestartFeedIntent();
}
