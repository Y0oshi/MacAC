using System.Numerics;

namespace MacAC.Mechanics.Kinetics;

/// <summary>Jump charging, permission and take-off / landing.</summary>
public sealed partial class MotionUnpacker
{

    public WeenieProblem LeapChargeIsAllowed(float reach)
    {
        if (WeenieObjRef is not null && !WeenieObjRef.CanLeap(reach))
            return WeenieProblem.CantJumpLoadedDown; // 0x49
        return PostureForbidsLeap(InterpretedPhase.ForwardCommand)
            ? WeenieProblem.YouCantJumpFromThisPosition // 0x48
            : WeenieProblem.None;
    }

    public WeenieProblem ChargeJump()
    {
        WeenieProblem verdict = LeapChargeIsAllowed(LeapReach);
        if (verdict != WeenieProblem.None)
            return verdict;

        // Charging while standing still on the ground makes it a standing long jump.
        if (OnTerrain
            && InterpretedPhase.ForwardCommand == LocomotionDirective.Ready
            && InterpretedPhase.FlankHopDirective is 0
            && InterpretedPhase.PivotDirective is 0)

            StandingLongJump = true;
        return WeenieProblem.None;
    }

    public WeenieProblem jump(float reach, int adjustStamina = 0)
    {
        if (KineticsObjRef is null)
            return WeenieProblem.NoPhysicsObject;

        InterruptCurrentMovement?.Invoke();

        WeenieProblem verdict = jump_is_allowed(reach, out _);
        if (verdict == WeenieProblem.None)
        {
            LeapReach = reach;
            KineticsObjRef.set_on_walkable(false);
            return WeenieProblem.None;
        }

        StandingLongJump = false;
        return verdict;
    }

    public float FetchLeapVZ()
    {
        float reach = LeapReach;
        if (reach < LeapVzEpsilon)
            return 0.0f;
        if (reach > UpperLeapReach)
            reach = UpperLeapReach;

        if (WeenieObjRef is null)
            return DefaultLeapVz;
        return WeenieObjRef.InqLeapVel(reach, out float vz) ? vz : 0.0f;
    }

    public Vector3 FetchDepartTerrainVel()
    {
        Vector3 vel = get_state_velocity();
        vel.Z = FetchLeapVZ();

        // A dead-still take-off keeps whatever the body was already doing, in its own frame.
        bool still = MathF.Abs(vel.X) < LeapVzEpsilon && MathF.Abs(vel.Y) < LeapVzEpsilon && MathF.Abs(vel.Z) < LeapVzEpsilon;
        if (still && KineticsObjRef is not null)
            vel = Vector3.Transform(KineticsObjRef.Velocity, Quaternion.Inverse(KineticsObjRef.Orientation));
        return vel;
    }

    public WeenieProblem jump_is_allowed(float reach, out int staminaPrice)
    {
        staminaPrice = 0;
        if (KineticsObjRef is null)
            return WeenieProblem.NotGrounded;

        bool mayDepart = !WeenieIsBeast || !HasGravity || OnTerrain;
        return mayDepart ? LeapLatch(reach, ref staminaPrice) : WeenieProblem.NotGrounded;
    }

    /// <summary>Retail's motion-id range test for "can a jump start from this motion".</summary>
    public static WeenieProblem LocomotionAllowsLeap(uint locomotion)
    {
        if (locomotion > 0x40000018u)
        {
            if (locomotion > 0x41000014u)
                return WeenieProblem.None;
            if (locomotion < 0x41000012u && (locomotion < 0x4000001eu || locomotion > 0x40000039u))
                return WeenieProblem.None;
        }
        else if (locomotion < 0x40000016u)
        {
            if (locomotion > 0x10000131u)
            {
                if (locomotion != 0x40000008u)
                    return WeenieProblem.None;
            }
            else if (locomotion < 0x10000128u && (locomotion < 0x1000006fu || locomotion > 0x10000078u))
            {
                return WeenieProblem.None;
            }
        }

        return WeenieProblem.YouCantJumpFromThisPosition;
    }

    public void LeaveGround()
    {
        if (!TerrainSignalsEnact)
            return;

        KineticsObjRef!.set_local_velocity(FetchDepartTerrainVel(), autonomous: true);
        StandingLongJump = false;
        LeapReach = 0f;

        RemoveLinkAnimations?.Invoke();
        apply_current_movement(abortRelocateTo: false, allowLeap: true);
    }

    public void HitGround()
    {
        if (!TerrainSignalsEnact)
            return;

        RemoveLinkAnimations?.Invoke();
        apply_current_movement(abortRelocateTo: false, allowLeap: true);
    }

    // Creatures under gravity re-plan their movement when leaving or landing
    private bool TerrainSignalsEnact => KineticsObjRef is not null && WeenieIsBeast && HasGravity;
    // Crouch/sit/sleep (and having fallen) forbid a jump
    private static bool PostureForbidsLeap(uint ahead)
    {
        return ahead == LocomotionDirective.Fallen || (ahead > LocomotionDirective.CrouchLowerTied && ahead <= LocomotionDirective.Sleeping);
    }

    // The shared tail of jump permission: constraint, the queued link's stored verdict, posture,
    // stamina
    private WeenieProblem LeapLatch(float reach, ref int staminaPrice)
    {
        if (KineticsObjRef is not null && KineticsObjRef.IsFullyConstrained)
            return WeenieProblem.GeneralMovementFailure; // 0x47

        // A queued link with a recorded verdict answers for us.
        if (_owed.First is { } front && front.Value.JumpErrorCode is not 0)
            return (WeenieProblem)front.Value.JumpErrorCode;

        WeenieProblem charge = LeapChargeIsAllowed(reach);
        if (charge != WeenieProblem.None)
            return charge;

        WeenieProblem posture = LocomotionAllowsLeap(InterpretedPhase.ForwardCommand);
        if (posture != WeenieProblem.None || WeenieObjRef is null)
            return posture;

        return WeenieObjRef.HopStaminaPrice(reach, out staminaPrice)
            ? WeenieProblem.None
            : WeenieProblem.GeneralMovementFailure; // 0x47 - can't afford
    }
}
