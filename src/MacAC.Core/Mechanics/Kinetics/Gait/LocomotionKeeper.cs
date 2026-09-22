namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class LocomotionKeeper(MotionUnpacker minterp)
{
    public MotionUnpacker Minterp { get; } = minterp ?? throw new ArgumentNullException(nameof(minterp));

    public MoveToKeeper? MoveTo { get; private set; }

    public Func<MoveToKeeper>? RelocateToMaker { get; set; }

    public Action? EngageKineticsObject { get; set; }

    public void CraftRelocateToKeeper()
    {
        if (MoveTo is null && RelocateToMaker is { } craft)
            MoveTo = craft();
    }

    public WeenieProblem PerformMovement(LocomotionPacket mvs)
    {
        EngageKineticsObject?.Invoke();

        switch (mvs.Type)
        {
            case TravelKind.RawCommand:
            case TravelKind.InterpretedCommand:
            case TravelKind.StopRawCommand:
            case TravelKind.StopInterpretedCommand:
            case TravelKind.StopCompletely:
                return Minterp.PerformMovement(mvs);

            case TravelKind.MoveToObject:
            case TravelKind.MoveToPosition:
            case TravelKind.TurnToObject:
            case TravelKind.TurnToHeading:
                CraftRelocateToKeeper();
                if (MoveTo is null)
                    return WeenieProblem.GeneralMovementFailure;
                MoveTo.PerformMovement(mvs);
                return WeenieProblem.None;

            default:
                return WeenieProblem.GeneralMovementFailure; // 0x47
        }
    }

    public void EmployMoment() => MoveTo?.EmployTime();

    public void HitGround()
    {
        Minterp.HitGround();
        MoveTo?.HitGround();
    }

    public void ProcessQuitRealm() => Minterp.ProcessExitWorld();

    public void CancelMoveTo(WeenieProblem problem) => MoveTo?.CancelMoveTo(problem);

    public void ProcessRefreshMark(TargetFacts details) => MoveTo?.ProcessRefreshObjective(details);

    public bool IsMovingTo() => MoveTo?.IsMovingTo() ?? false;
}
