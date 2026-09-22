using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Picks the walk/run cycle a server-moved object should show for its velocity.</summary>
public static class ServerDrivenGait
{
    public const float HaltPace = 0.20f;
    public const float ExecThreshold = 1.25f;
    public const float LowerPaceMod = 0.25f;
    public const float UpperPaceMod = 3.00f;

    public readonly record struct GaitCycle(uint Motion, float SpeedMod, bool IsMoving);

    public static GaitCycle PlanFromVel(Vector3 realmVel)
    {
        float terrain = MathF.Sqrt(realmVel.X * realmVel.X + realmVel.Y * realmVel.Y);
        if (terrain < HaltPace)
            return new GaitCycle(LocomotionDirective.Ready, 1f, false);

        bool walking = terrain < ExecThreshold;
        float animPace = walking ? MotionUnpacker.StrollAnimPace : MotionUnpacker.ExecAnimPace;
        uint locomotion = walking ? LocomotionDirective.WalkForward : LocomotionDirective.ExecAhead;
        return new GaitCycle(locomotion, Math.Clamp(terrain / animPace, LowerPaceMod, UpperPaceMod), true);
    }

    public static bool CanEnactVelCycle(uint latestLocomotion)
    {
        return latestLocomotion == LocomotionDirective.Ready || IsLocomotion(latestLocomotion);
    }

    public static bool IsLocomotion(uint locomotion) => (locomotion & 0xFFu) is 0x05 or 0x06 or 0x07 or 0x0F or 0x10;
}
