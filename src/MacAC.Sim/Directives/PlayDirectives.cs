using MacAC.Sim.Play;

namespace MacAC.Sim;

public enum SimSelectionDirective
{
    SelectClosestHostile,
    SelectPrevious,
    ExamineSelected,
    UseSelected,
    PickUpSelected,
}

public enum SimFightingDirective
{
    ToggleMode,
}

public enum SimLocomotionDirective
{
    ToggleRunLock,
    Stop,
    Ready,
    Sit,
    Crouch,
    Sleep,
    StopCompletely,
    FinishJump,
}

public enum SimPortalDirective
{
    RecallLifestone,
    RecallMarketplace,
    RecallHouse,
    RecallMansion,
}

public interface ISimSelectionDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, SimSelectionDirective directive);

    SimDirectiveResult ChooseObject(SimEpochTicket anticipatedGen, uint objectIdent);

    SimDirectiveResult Clear(SimEpochTicket anticipatedGen);
}

public interface ISimFightingDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, SimFightingDirective directive);

    SimDirectiveResult PerformAssault(SimEpochTicket anticipatedGen, in SimFightingAttackInput directive);
}

public readonly record struct SimArcanaDirective(uint SpellId);

public interface ISimArcanaDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, in SimArcanaDirective directive);
}

public interface ISimLocomotionDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, SimLocomotionDirective directive);

    SimDirectiveResult PerformLocomotion(SimEpochTicket anticipatedGen, uint locomotionDirective);

    SimDirectiveResult AssignIntent(SimEpochTicket anticipatedGen, in LocomotionInput feed);

    SimDirectiveResult WipeIntent(SimEpochTicket anticipatedGen);

    SimDirectiveResult PivotToBearing(SimEpochTicket anticipatedGen, float bearingDeg, bool enactExecGripTag = false);
}

public interface ISimPortalDirectives
{
    SimDirectiveResult Execute(SimEpochTicket anticipatedGen, SimPortalDirective directive);
}
