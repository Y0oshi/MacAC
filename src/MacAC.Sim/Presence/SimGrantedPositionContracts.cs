namespace MacAC.Sim.Presence;

internal enum SimGrantedPositionExecutionStatus : byte
{
    NotApplicable,

    Rejected,

    Contention,

    DeferredCell,

    Committed,
}
