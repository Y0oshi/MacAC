namespace MacAC.Mechanics.Kinetics;

public sealed class RemoteActor : IWeenieActor
{
    public bool InqExecRate(out float rate)
    {
        rate = 0f;
        return false;
    }

    public bool InqLeapVel(float reach, out float vz)
    {
        vz = 0f;
        return false;
    }

    public bool CanLeap(float reach) => true;
}
