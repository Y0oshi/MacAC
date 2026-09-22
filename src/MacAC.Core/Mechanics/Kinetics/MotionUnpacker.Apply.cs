using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

public sealed partial class MotionUnpacker
{
    public void apply_raw_movement(CrudeLocomotionPhase raw)
    {
        RawPhase.CurrentHoldKey = raw.CurrentHoldKey;

        InterpretedPhase.ForwardCommand = Lane(raw.ForwardCommand, raw.ForwardSpeed, raw.ForwardHoldKey, out InterpretedPhase.ForwardSpeed);
        InterpretedPhase.FlankHopDirective = Lane(raw.SidestepDirective, raw.SidestepPace, raw.SidestepGripTag, out InterpretedPhase.FlankHopPace);
        InterpretedPhase.PivotDirective = Lane(raw.TurnDirective, raw.TurnPace, raw.PivotGripTag, out InterpretedPhase.PivotPace);
    }

    public void apply_current_movement(bool abortRelocateTo, bool allowLeap)
    {
        if (KineticsObjRef is null || !Initted)
            return;
        Reapply(abortRelocateTo, allowLeap, WeenieObjRef);
    }

    public void apply_raw_movement(bool abortRelocateTo, bool allowLeap)
    {
        if (KineticsObjRef is null)
            return;
        apply_raw_movement(RawPhase);
        ImposeLatestTravelInterpreted(abortRelocateTo, allowLeap);
    }

    public void AnnounceExhaustion()
    {
        if (KineticsObjRef is null || !Initted)
            return;
        Reapply(abortRelocateTo: false, allowLeap: true, WeenieObjRef);
    }

    public void SetWeenieObject(IWeenieActor? weenie)
    {
        WeenieObjRef = weenie;
        if (KineticsObjRef is null || !Initted)
            return;
        Reapply(abortRelocateTo: true, allowLeap: true, weenie);
    }

    public void AssignKineticsObject(KineticBody? kineticsObjRef)
    {
        KineticsObjRef = kineticsObjRef;
        if (kineticsObjRef is null || !Initted)
            return;
        Reapply(abortRelocateTo: true, allowLeap: true, WeenieObjRef);
    }

    public void set_hold_run(bool holdingExec, bool interrupt)
    {
        bool execTagUp = !holdingExec;
        bool notCurrentlyExec = RawPhase.CurrentHoldKey != HeldKey.Run;
        if (execTagUp == notCurrentlyExec)
            return;

        RawPhase.CurrentHoldKey = holdingExec ? HeldKey.Run : HeldKey.None;
        apply_current_movement(abortRelocateTo: interrupt, allowLeap: true);
    }

    public void SetHoldKey(HeldKey tag, bool abortRelocateTo)
    {
        HeldKey latest = RawPhase.CurrentHoldKey;
        if (tag == latest)
            return;

        // Only the Run↔None edges do anything; Invalid is never applied.
        bool edits = tag switch
        {
            HeldKey.None => latest == HeldKey.Run,
            HeldKey.Run => latest != HeldKey.Run,
            _ => false,
        };
        if (!edits)
            return;

        RawPhase.CurrentHoldKey = tag;
        apply_current_movement(abortRelocateTo, allowLeap: true);
    }

    public int ShiftToInterpretedPhase(in InboundDecodedState ims, IDecodedMotionTap? drain = null)
    {
        if (KineticsObjRef is null)
            return 0;

        RawPhase.CurrentStyle = ims.CurrentStyling;

        bool allowLeap = LocomotionAllowsLeap(InterpretedPhase.ForwardCommand) == WeenieProblem.None;

        InterpretedPhase.LatestStyle = ims.CurrentStyling;
        InterpretedPhase.ForwardCommand = ims.ForwardCommand;
        InterpretedPhase.ForwardSpeed = ims.ForwardSpeed;
        InterpretedPhase.FlankHopDirective = ims.FlankStepCommand;
        InterpretedPhase.FlankHopPace = ims.FlankStepSpeed;
        InterpretedPhase.PivotDirective = ims.PivotCommand;
        InterpretedPhase.PivotPace = ims.PivotSpeed;

        ImposeInterpretedTravel(ims.CurrentStyling, drain, abortRelocateTo: true, allowLeap: allowLeap);

        if (ims.Actions is { } acts)
        {
            foreach (InboundMotionVerb act in acts)
            {
                int incoming = act.Stamp & 0x7FFF;
                if (!StampIsNewer(incoming, SrvActStamp & 0x7FFF))
                    continue;

                // Local player skips its own autonomous echoes (305977)
                if (IsOwnAvatar && act.Autonomous)
                    continue;

                SrvActStamp = incoming;
                DoInterpretedLocomotion(act.Command, new LocomotionParams { Speed = act.Speed, Autonomous = act.Autonomous }, drain);
            }
        }

        return 1;
    }

    public void ImposeInterpretedTravel(uint latestStyling, IDecodedMotionTap? drain, bool abortRelocateTo = false, bool allowLeap = false)
    {
        if (KineticsObjRef is null)
            return;

        LocomotionParams p = new LocomotionParams
        {
            SetHoldKey = false,
            ModifyInterpretedState = false,
            CancelMoveTo = abortRelocateTo,
            DisableJumpDuringLink = !allowLeap,
        };

        if (InterpretedPhase.ForwardCommand == LocomotionDirective.ExecAhead)
            MyExecRate = InterpretedPhase.ForwardSpeed;

        p.Speed = 1.0f;
        DoInterpretedLocomotion(latestStyling, p, drain);

        if (!contact_allows_move(InterpretedPhase.ForwardCommand))
        {
            p.Speed = 1.0f;
            DoInterpretedLocomotion(LocomotionDirective.Falling, p, drain);
        }
        else if (StandingLongJump)
        {
            p.Speed = 1.0f;
            DoInterpretedLocomotion(LocomotionDirective.Ready, p, drain);
            HaltInterpretedLocomotion(LocomotionDirective.FlankHopRight, p, drain);
        }
        else
        {
            p.Speed = InterpretedPhase.ForwardSpeed;
            DoInterpretedLocomotion(InterpretedPhase.ForwardCommand, p, drain);
            if (InterpretedPhase.FlankHopDirective is 0)
            {
                HaltInterpretedLocomotion(LocomotionDirective.FlankHopRight, p, drain);
            }
            else
            {
                p.Speed = InterpretedPhase.FlankHopPace;
                DoInterpretedLocomotion(InterpretedPhase.FlankHopDirective, p, drain);
            }
        }

        if (InterpretedPhase.PivotDirective is not 0)
        {
            p.Speed = InterpretedPhase.PivotPace;
            DoInterpretedLocomotion(InterpretedPhase.PivotDirective, p, drain);
            return;
        }

        HaltInterpretedLocomotion(LocomotionDirective.TurnRight, p, drain);
    }

    private uint Lane(uint directive, float pace, HeldKey gripTag, out float adjustedPace)
    {
        adjust_motion(ref directive, ref pace, gripTag);
        adjustedPace = pace;
        return directive;
    }

    // The local player replays its raw keys; everything else replays the interpreted state it was
    // given
    private void Reapply(bool abortRelocateTo, bool allowLeap, IWeenieActor? weenie)
    {
        bool isTheAvatar = weenie is null || weenie.IsTheAvatar();
        if (isTheAvatar && KineticsObjRef!.PreviousRelocateWasAutonomous)
            apply_raw_movement(abortRelocateTo, allowLeap);
        else
            ImposeLatestTravelInterpreted(abortRelocateTo, allowLeap);
    }

    private void ImposeLatestTravelInterpreted(bool abortRelocateTo, bool allowLeap)
    {
        if (KineticsObjRef is null)
            return;

        if (DefaultSink is not null)
        {
            ImposeInterpretedTravel(InterpretedPhase.LatestStyle, DefaultSink, abortRelocateTo, allowLeap);
            return;
        }

        // No animation layer: drive the body directly
        if (InterpretedPhase.ForwardCommand == LocomotionDirective.ExecAhead)
            MyExecRate = InterpretedPhase.ForwardSpeed;
        if (KineticsObjRef.OnWalkable)
            KineticsObjRef.set_local_velocity(get_state_velocity(), KineticsObjRef.PreviousRelocateWasAutonomous);
    }

    // Wrap-aware 15-bit stamp ordering: true when incoming is newer than stored
    private static bool StampIsNewer(int incoming, int stored)
    {
        int diff = incoming >= stored ? incoming - stored : stored - incoming;
        return diff <= 0x3FFF ? stored < incoming : incoming < stored;
    }
}
