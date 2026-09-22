namespace MacAC.Mechanics.Kinetics;

public static class CanonObjectKeeperTail
{
    public static void Run(
        Gait.TargetKeeper? mark,
        Gait.LocomotionKeeper? travel,
        Gait.MotionTableKeeper? pieceArr,
        Gait.PositionKeeper? locus)
    {
        mark?.ProcessTargetting();
        travel?.EmployMoment();
        pieceArr?.WieldMoment();
        locus?.UseMoment();
    }

    public static void Run(
        Action? hndTargeting,
        Gait.LocomotionKeeper? travel,
        Action? pieceArrHndTravel,
        Gait.PositionKeeper? locus)
    {
        hndTargeting?.Invoke();
        travel?.EmployMoment();
        pieceArrHndTravel?.Invoke();
        locus?.UseMoment();
    }

    public static void Run(
        Action? verifyDetection,
        Action? hndTargeting,
        Action? travelUseMoment,
        Action? pieceArrHndTravel,
        Action? locusUseMoment)
    {
        verifyDetection?.Invoke();
        hndTargeting?.Invoke();
        travelUseMoment?.Invoke();
        pieceArrHndTravel?.Invoke();
        locusUseMoment?.Invoke();
    }
}
