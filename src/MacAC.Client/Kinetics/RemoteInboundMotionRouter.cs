using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Wire;

namespace MacAC.Client.Kinetics;

internal sealed class RemoteInboundMotionRouter(
    Func<LocomotionKeeper, uint, RealmSession.MoverMotionUpdate, bool>
            routeServerMoveTo,
    Action<IKineticObjHost?, uint> stickToObject)
{
    private readonly Func<
        LocomotionKeeper,
        uint,
        RealmSession.MoverMotionUpdate,
        bool> _courseSrvRelocateTo = routeServerMoveTo
            ?? throw new ArgumentNullException(nameof(routeServerMoveTo));
    private readonly Action<IKineticObjHost?, uint> _stickToObject = stickToObject
            ?? throw new ArgumentNullException(nameof(stickToObject));

    public DistantIncomingLocomotionRelayOutcome Apply(
        RealmSession.MoverMotionUpdate refresh,
        LocomotionKeeper travel,
        IDecodedMotionTap? animDrain,
        IKineticObjHost? hub,
        uint chamberIdent,
        uint backupAheadClass,
        Func<bool>? isLatest = null)
    {
        ArgumentNullException.ThrowIfNull(travel);
        bool Latest() => isLatest?.Invoke() ?? true;

        var locomotion = travel.Minterp;
        uint earlierAhead = locomotion.InterpretedPhase.ForwardCommand;
        DistantIncomingLocomotionRelayOutcome Superseded() => new(
            RoutedMoveTo: false,
            AppliedInterpretedState: false,
            PreviousForwardCommand: earlierAhead,
            CurrentForwardCommand: locomotion.InterpretedPhase.ForwardCommand,
            Superseded: true);
        if (!Latest())
            return Superseded();

        locomotion.InterruptCurrentMovement?.Invoke();
        if (!Latest())
            return Superseded();
        locomotion.UnstickFromObject?.Invoke();
        if (!Latest())
            return Superseded();

        uint wireStyling = refresh.MotionState.Stance is not 0
            ? 0x80000000u | refresh.MotionState.Stance
            : 0x8000003Du;
        if (locomotion.InterpretedPhase.LatestStyle != wireStyling)
        {
            locomotion.DoMotion(
                wireStyling,
                new LocomotionParams());
            if (!Latest())
                return Superseded();
        }

        bool routedRelocateTo = _courseSrvRelocateTo(travel, chamberIdent, refresh);
        if (!Latest())
            return Superseded();
        if (routedRelocateTo)
        {
            return new DistantIncomingLocomotionRelayOutcome(
                RoutedMoveTo: true,
                AppliedInterpretedState: false,
                PreviousForwardCommand: earlierAhead,
                CurrentForwardCommand: locomotion.InterpretedPhase.ForwardCommand);
        }

        if (refresh.MotionState.MovementType is not 0)
        {
            return new DistantIncomingLocomotionRelayOutcome(
                RoutedMoveTo: false,
                AppliedInterpretedState: false,
                PreviousForwardCommand: earlierAhead,
                CurrentForwardCommand: locomotion.InterpretedPhase.ForwardCommand);
        }

        var interpreted =
            InboundInterpretedMotionMint.Create(
                refresh.MotionState,
                backupAheadClass);
        locomotion.ShiftToInterpretedPhase(interpreted, animDrain);
        if (!Latest())
            return Superseded();

        if (refresh.MotionState.StickyObjectGuid is { } stickyOid
            && stickyOid is not 0)
        {
            _stickToObject(hub, stickyOid);
            if (!Latest())
                return Superseded();
        }
        locomotion.StandingLongJump = refresh.MotionState.StandingLongJump;

        return new DistantIncomingLocomotionRelayOutcome(
            RoutedMoveTo: false,
            AppliedInterpretedState: true,
            PreviousForwardCommand: earlierAhead,
            CurrentForwardCommand: interpreted.ForwardCommand);
    }
}

internal readonly record struct DistantIncomingLocomotionRelayOutcome(
    bool RoutedMoveTo,
    bool AppliedInterpretedState,
    uint PreviousForwardCommand,
    uint CurrentForwardCommand,
    bool Superseded = false)
{
    public bool AheadDirectiveAltered
    {
        get
        {
            return AppliedInterpretedState
        && PreviousForwardCommand != CurrentForwardCommand;
        }
    }
}
