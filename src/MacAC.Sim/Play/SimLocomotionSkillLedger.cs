namespace MacAC.Sim.Play;

public readonly record struct SimLocomotionSkillCapture(
    int RunSkill,
    int JumpSkill,
    long Revision,
    float Burden = 0f,
    int CurrentStamina = -1,
    uint OwnPwdBitfield = 0u,
    int PlayerKillerStatus = -1,
    float? LastPkAttackTimestamp = null)
{
    public bool IsComplete => RunSkill >= 0 && JumpSkill >= 0;
}

public sealed class SimLocomotionSkillLedger
{
    private int _exec = -1;
    private int _leap = -1;
    private float _burden;
    private int _stamina = -1;
    private uint _pwdBitset;
    private int _pkCondition = -1;
    private float _previousPkAssault;
    private bool _hasPreviousPkAssault;
    private long _rev;

    public int ExecAptitude => Volatile.Read(ref _exec);
    public int LeapAptitude => Volatile.Read(ref _leap);
    public float Burden => Volatile.Read(ref _burden);
    public int CurrentStamina => Volatile.Read(ref _stamina);
    public uint OwnPwdBitfield => Volatile.Read(ref _pwdBitset);
    public int PlayerKillerStatus => Volatile.Read(ref _pkCondition);
    public float? LastPkAttackTimestamp
    {
        get
        {
            return Volatile.Read(ref _hasPreviousPkAssault) ? Volatile.Read(ref _previousPkAssault) : null;
        }
    }

    public bool IsComplete => _exec >= 0 && _leap >= 0;
    public long Revision => Interlocked.Read(ref _rev);

    /// <summary>True when nothing has been written since construction or the last reset.</summary>
    public bool IsPristine
    {
        get
        {
            return ExecAptitude == -1 && LeapAptitude == -1 && Burden == 0f && CurrentStamina == -1
        && OwnPwdBitfield is 0u && PlayerKillerStatus == -1 && LastPkAttackTimestamp is null;
        }
    }

    public SimLocomotionSkillCapture Snapshot
    {
        get
        {
            return new(_exec, _leap, Revision, _burden, _stamina, OwnPwdBitfield, PlayerKillerStatus, LastPkAttackTimestamp);
        }
    }

    /// <summary>Negative values mean "unknown" and leave the current value alone.</summary>
    public void Update(int execAptitude, int leapAptitude)
    {
        bool altered = execAptitude >= 0 && Place(ref _exec, execAptitude);
        altered |= leapAptitude >= 0 && Place(ref _leap, leapAptitude);
        Tick(altered);
    }

    public void RefreshBurden(float burden)
    {
        if (Burden == burden)
            return;
        _burden = burden;
        Tick(true);
    }

    public void RefreshStamina(int latestStamina) => Tick(Place(ref _stamina, latestStamina));

    public void RefreshOwnPwdBitfield(uint bitfield) => Tick(Place(ref _pwdBitset, bitfield));

    public void RefreshAvatarKillerCondition(int avatarKillerCondition, float? previousPkAssaultStamp)
    {
        bool altered = Place(ref _pkCondition, avatarKillerCondition);
        bool has = previousPkAssaultStamp.HasValue;
        float at = previousPkAssaultStamp ?? 0f;
        if (_hasPreviousPkAssault != has || (has && _previousPkAssault != at))
        {
            _previousPkAssault = at;
            _hasPreviousPkAssault = has;
            altered = true;
        }
        Tick(altered);
    }

    public void ResetSession()
    {
        _exec = -1;
        _leap = -1;
        _burden = 0f;
        _stamina = -1;
        _pwdBitset = 0u;
        _pkCondition = -1;
        _previousPkAssault = 0f;
        _hasPreviousPkAssault = false;
        Interlocked.Increment(ref _rev);
    }

    // Writes a value when it differs; returns whether it did
    private static bool Place<T>(ref T socket, T val) where T : IEquatable<T>
    {
        if (socket.Equals(val))
            return false;
        socket = val;
        return true;
    }

    private void Tick(bool altered)
    {
        if (altered)
            Interlocked.Increment(ref _rev);
    }
}
