namespace MacAC.Mechanics.Kinetics;

public sealed class PlayerActor(int execAptitude = 0, int leapAptitude = 0, float burden = 0f) : IWeenieActor
{
    public const float CanLeapPullThreshold = 2.0f;

    private const int DefaultPkCondition = 8;
    private const float PkAssaultPaneSecs = 20.0f;
    private const float Gravity2g = 19.6f;

    private int _execAptitude = execAptitude;
    private int _leapAptitude = leapAptitude;
    private float _burden = burden;
    private int? _pkCondition;
    private float? _previousPkAssaultAt;
    private uint? _stamina;

    public void AssignAptitudes(int execAptitude, int leapAptitude)
    {
        _execAptitude = execAptitude;
        _leapAptitude = leapAptitude;
    }

    public void AssignBurden(float burden) => _burden = burden;

    public void AssignStamina(uint? latestStamina) => _stamina = latestStamina;

    public void AssignAvatarKillerCondition(int? avatarKillerCondition, float? previousPkAssaultStamp)
    {
        _pkCondition = avatarKillerCondition;
        _previousPkAssaultAt = previousPkAssaultStamp;
    }

    public bool InqExecRate(out float rate)
    {
        rate = LocomotionSystem.FetchExecRate(_burden, Net(_execAptitude));
        return true;
    }

    public bool InqLeapVel(float reach, out float vz)
    {
        float height = LocomotionSystem.FetchLeapHeight(_burden, Net(_leapAptitude), reach);
        vz = MathF.Sqrt(height * Gravity2g);
        return true;
    }

    public bool CanLeap(float reach) => _burden < CanLeapPullThreshold;

    public bool IsTheAvatar() => true;

    public bool HopStaminaPrice(float reach, out int price)
    {
        price = LocomotionSystem.LeapStaminaPrice(reach, _burden, RecentlyPkEngaged());
        return true;
    }

    public static float ObtainExecRate(float burden, int execAptitude) => LocomotionSystem.FetchExecRate(burden, execAptitude);

    public static float ObtainLeapHeight(float burden, int leapAptitude, float reach) => LocomotionSystem.FetchLeapHeight(burden, leapAptitude, reach);

    public static float FetchBurdenMod(float burden) => BurdenSystem.PullMod(burden);

    // An exhausted player moves as though unskilled
    private int Net(int aptitude) => _stamina == 0 ? 0 : aptitude;

    // PK or PKLite and attacked within the last twenty seconds
    private bool RecentlyPkEngaged()
    {
        int condition = _pkCondition ?? DefaultPkCondition;
        if (condition is not (4 or 0x40) || _previousPkAssaultAt is not { } attackedAt)
            return false;
        float instant = System.Environment.TickCount64 / 1000f;
        return (attackedAt + PkAssaultPaneSecs) >= instant;
    }
}
