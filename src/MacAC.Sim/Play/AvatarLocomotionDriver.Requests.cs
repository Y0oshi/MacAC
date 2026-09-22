using System.Numerics;
using MacAC.Mechanics.Comms;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Sim.Play;

public sealed partial class AvatarLocomotionDriver
{
    public bool ReadyForAssaultReq()
    {
        DemandPublished();
        if (_srvDriven)
            return false;

        HaltCompletelyAtKineticsObjectBoundary();
        _onlinePivotCmd = null;
        _onlinePivotPace = 0f;
        _onlinePivotFromPointer = false;
        _onlineSidestepCmd = null;
        _onlineSidestepExecGrip = false;
        _pointerPivotQueued = false;
        _pointerPivotDiff = 0f;
        _hull.PreviousRelocateWasAutonomous = true;
        return true;
    }

    public bool ReqPosture(uint locomotion)
    {
        DemandPublished();
        if (locomotion is not (
            LocomotionDirective.Ready
            or LocomotionDirective.Crouch
            or LocomotionDirective.Sitting
            or LocomotionDirective.Sleeping))

            return false;

        GrabControl();
        LocomotionParams parameters =
            new LocomotionParams();
        if (DoLocomotionAtBoundary(locomotion, parameters)
            != WeenieProblem.None)

            return false;

        _foreignRelocateSignalQueued = true;
        return true;
    }

    public void AssignToonAptitudes(int execAptitude, int leapAptitude)
    {
        DemandMutable();
        _actor.AssignAptitudes(execAptitude, leapAptitude);
    }

    public void AssignToonBurden(float burden)
    {
        DemandMutable();
        _actor.AssignBurden(burden);
    }

    public void AssignToonStamina(int latestStamina)
    {
        DemandMutable();
        _actor.AssignStamina(latestStamina < 0 ? null : (uint)latestStamina);
    }

    public void AssignToonPkCondition(int avatarKillerCondition, float? previousPkAssaultStamp)
    {
        DemandMutable();
        _actor.AssignAvatarKillerCondition(
            avatarKillerCondition < 0 ? null : avatarKillerCondition,
            previousPkAssaultStamp);
    }

    internal bool ReqPivotToBearing(
        float bearingDeg,
        bool enactExecGripTag = false)
    {
        DemandPublished();
        if (!float.IsFinite(bearingDeg) || Movement.MoveTo is null)
            return false;

        GrabControl();

        LocomotionParams parameters =
            new LocomotionParams
            {
                WantedBearing = bearingDeg,
                Speed = 1f,
                HaltCompletelyBit = false,
            };
        if (enactExecGripTag)
            parameters.GripTagToEnact = HeldKey.Run;

        _hull.PreviousRelocateWasAutonomous = true;
        if (Movement.PerformMovement(new LocomotionPacket
        {
            Type = TravelKind.TurnToHeading,
            Params = parameters,
        }) != WeenieProblem.None)

            return false;

        _foreignRelocateSignalQueued = true;
        return true;
    }

    internal bool ReqDirectiveLocomotion(uint locomotion)
    {
        DemandPublished();
        GrabControl();
        LocomotionParams parameters =
            new LocomotionParams
            {
                Autonomous = true,
                ActStamp = _actStamp,
            };
        if (DoLocomotionAtBoundary(locomotion, parameters)
            != WeenieProblem.None)

            return false;

        if ((locomotion & 0x10000000u) is not 0u)
            ++_actStamp;
        _foreignRawLocomotionQueued = new CrudeLocomotionPhase(_unpacker.RawPhase);
        _foreignRelocateSignalQueued = true;
        return true;
    }

    internal SimLocomotionStatsApplication ImposeToonTravelStats(
        in SimLocomotionSkillCapture capture)
    {
        switch (_lifespan)
        {
            case AvatarLocomotionDriverPublicationLifespan.StandalonePublished:
            case AvatarLocomotionDriverPublicationLifespan.CandidatePreparing:
            case AvatarLocomotionDriverPublicationLifespan.RuntimePublished:
                ImposeStats(capture);
                return SimLocomotionStatsApplication.AppliedLive;
            case AvatarLocomotionDriverPublicationLifespan.RuntimeOwnedDormant:
                ImposeStats(capture);
                return SimLocomotionStatsApplication.AppliedDormant;
            default:
                return SimLocomotionStatsApplication.DroppedDisplacedController;
        }
    }

    internal bool AnnounceExhaustionAtTravelBoundary()
    {
        if (_lifespan
            is AvatarLocomotionDriverPublicationLifespan.StandalonePublished
                or AvatarLocomotionDriverPublicationLifespan.CandidatePreparing
                or AvatarLocomotionDriverPublicationLifespan.RuntimePublished)
        {
            _unpacker.AnnounceExhaustion();
            return true;
        }
        return false;
    }

    internal WeenieProblem HaltCompletelyAtKineticsObjectBoundary()
    {
        DemandMutable();
        return _gait.PerformMovement(new LocomotionPacket
        {
            Type = TravelKind.StopCompletely,
        });
    }

    internal void CompleteLeap()
    {
        _chargingLeap = false;
        _leapReach = 0f;
        _unpacker.StandingLongJump = false;
    }

    private void ImposeStats(
        in SimLocomotionSkillCapture capture)
    {
        _actor.AssignAptitudes(capture.RunSkill, capture.JumpSkill);
        _actor.AssignBurden(capture.Burden);
        _actor.AssignStamina(
            capture.CurrentStamina < 0 ? null : (uint)capture.CurrentStamina);
        _pvpFlagSet = EntityContactFlagsExt
            .FromPwdBitfield(capture.OwnPwdBitfield)
            .ToCarrierPhase();
        _actor.AssignAvatarKillerCondition(
            capture.PlayerKillerStatus < 0 ? null : capture.PlayerKillerStatus,
            capture.LastPkAttackTimestamp);
    }

    private WeenieProblem DoLocomotionAtBoundary(
        uint locomotion,
        LocomotionParams parameters)
    {
        DemandMutable();
        _hull.PreviousRelocateWasAutonomous = true;
        return Movement.PerformMovement(new LocomotionPacket
        {
            Type = TravelKind.RawCommand,
            Motion = locomotion,
            Params = parameters,
        });
    }

    private WeenieProblem HaltLocomotionAtBoundary(
        uint locomotion,
        LocomotionParams parameters)
    {
        DemandMutable();
        _hull.PreviousRelocateWasAutonomous = true;
        return Movement.PerformMovement(new LocomotionPacket
        {
            Type = TravelKind.StopRawCommand,
            Motion = locomotion,
            Params = parameters,
        });
    }

    private void RefuseJump(WeenieProblem outcome)
    {
        if (MacAC.Mechanics.Kinetics.KineticTelemetry.ProbeJumpEnabled)
        {
            Console.WriteLine(
                $"[jump] RefuseJump result={outcome} "
                + $"hasCallback={OnInterfaceWording is not null} "
                + $"onWalkable={_hull.OnWalkable} "
                + $"prevJumpHeld={_leapPinnedPrior}");
        }

        if (OnInterfaceWording is null)
            return;

        string? phrase = outcome switch
        {
            WeenieProblem.NotGrounded => TextRefusals.CantLeapInAir,                // 0x24
            WeenieProblem.YouCantJumpFromThisPosition => TextRefusals.CantLeapLocus, // 0x48
            WeenieProblem.CantJumpLoadedDown => TextRefusals.CantLeapPull,           // 0x49
            _ => null,
        };
        if (phrase is not null)
            OnInterfaceWording(phrase, CanonLogTextType.ClientLocal);
    }

    public JumpChargeCapture JumpCharge
        => new(_chargingLeap, _chargingLeap ? _leapReach : 0f);

    internal bool IsStandingStill => _unpacker.IsStandingStill();

    private bool _chargingLeap;

    private float _leapReach;

    private bool _leapPinnedPrior;

    private const float LeapChargeRate =
        1f / (float)MacAC.Mechanics.Fighting.FightInputPlanner.AssaultStrengthUpSecs;

    private const float DualWieldLeapChargeRate =
        1f / (float)MacAC.Mechanics.Fighting.FightInputPlanner.DualWieldStrengthUpSecs;
}
