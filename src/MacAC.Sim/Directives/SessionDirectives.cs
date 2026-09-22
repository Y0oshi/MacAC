namespace MacAC.Sim;

public interface ISimSessionDirectives
{
    SimSessionStartResult Start(SimEpochTicket anticipatedGen);

    SimSessionStartResult Reconnect(SimEpochTicket anticipatedGen);

    SimTeardownAck Stop(SimEpochTicket anticipatedGen);
}
