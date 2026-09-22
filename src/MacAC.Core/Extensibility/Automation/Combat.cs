namespace MacAC.Extensibility.Automation;

public enum CombatPosture
{
    Unknown = 0,
    Peace,
    Melee,
    Missile,
    Magic,
}

public enum StrikeHeight
{
    High = 1,
    Medium = 2,
    Low = 3,
}

public readonly record struct HostileEntry(
    uint ObjectId,
    string Name,
    uint WeenieClassId,
    float Distance,
    float RelativeAngleDegrees,
    bool IsHealthKnown,
    float HealthFraction)
{
    public int SpeciesIdent { get; init; }

    public string SpeciesLabel { get; init; } = string.Empty;

    /// <summary>Spawn/appraisal maximum HP; zero until the host learns it.</summary>
    public int CeilingHealth { get; init; }

    public bool HasShield { get; init; }

    public ushort Incarnation { get; init; }

    public long HealthRev { get; init; }

    public double SecsSinceHealthRefresh { get; init; } = double.PositiveInfinity;
}

public readonly record struct CombatFrame(
    uint SelectedObjectId,
    CombatPosture Mode,
    StrikeHeight AttackHeight,
    float DesiredPower,
    float PowerBarLevel,
    bool BuildInProgress,
    bool RequestInProgress,
    bool ServerResponsePending,
    bool RepeatAttackInProgress)
{
    public long WrapUpRev { get; init; }

    public uint WrapUpSeries { get; init; }

    public uint WrapUpWeenieProblem { get; init; }
}

public enum CombatOutcome
{
    Unavailable = 0,
    InvalidTarget,
    WrongMode,
    Busy,
    AlreadyReady,
    ModeChangeSent,
    Started,
    Released,
    Stopped,
    Refused,
}

public readonly record struct CombatVerdict(
    CombatOutcome Status,
    string? Notice = null)
{
    public bool Accepted
    {
        get
        {
            return Status is
        CombatOutcome.AlreadyReady
        or CombatOutcome.ModeChangeSent
        or CombatOutcome.Started
        or CombatOutcome.Released
        or CombatOutcome.Stopped;
        }
    }
}

public interface ICombatControls
{
    CombatFrame Snapshot { get; }

    IReadOnlyList<HostileEntry> GrabHostileMarks(float ceilingGap);

    CombatVerdict JoinDefaultManner();

    CombatVerdict JoinManner(CombatPosture manner) => new(CombatOutcome.Unavailable);

    CombatVerdict CommencePhysicalAssault(uint markObjectIdent, StrikeHeight height, float strength);

    CombatVerdict FreePhysicalAssault();

    CombatVerdict CancelPhysicalAssault();

    CombatVerdict DismissGhostMark(uint markObjectIdent) => new(CombatOutcome.Unavailable);
}
