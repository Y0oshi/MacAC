using MacAC.Client.Graphics;

namespace MacAC.Client.Kinetics;

internal static class DistantSrvControlledVelCycle
{
    private static bool IsAvatarOid(uint oid) =>
        (oid & 0xFF000000u) == 0x50000000u;
    public static void Apply(
        uint srvOid,
        OnlineActorMotionLedger ledger,
        PeerMotion motion,
        System.Numerics.Vector3 vel)
    {
        if (motion.Airborne) return;
        if (ledger.Sequencer is null) return;
        if (motion.MoveTo is { TravelKindPhase: not MacAC.Mechanics.Kinetics.TravelKind.Invalid }) return;

        if (IsAvatarOid(srvOid))

            return;

        uint latestLocomotion = ledger.Sequencer.CurrentMotion;
        if (!MacAC.Mechanics.Kinetics.ServerDrivenGait
            .CanEnactVelCycle(latestLocomotion))
            return;

        var plan = MacAC.Mechanics.Kinetics.ServerDrivenGait
            .PlanFromVel(vel);

        uint styling = ledger.Sequencer.LatestStyling is not 0
            ? ledger.Sequencer.LatestStyling
            : 0x8000003Du;

        if (System.Environment.GetEnvironmentVariable("MACAC_REMOTE_VEL_DIAG") == "1")
        {
            System.Console.WriteLine(
                $"[UPCYCLE] guid={srvOid:X8} "
                + $"vel=({vel.X:F2},{vel.Y:F2},{vel.Z:F2}) "
                + $"|v|={vel.Length():F2} "
                + $"-> motion=0x{plan.Motion:X8} speedMod={plan.SpeedMod:F2} "
                + $"prev=0x{latestLocomotion:X8} "
                + $"airborne={motion.Airborne} moveTo={motion.MoveTo?.TravelKindPhase ?? MacAC.Mechanics.Kinetics.TravelKind.Invalid}");
        }
        ledger.Sequencer.SetCycle(styling, plan.Motion, plan.SpeedMod);
    }

}
