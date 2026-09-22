using System.Numerics;

namespace MacAC.Extensibility.Automation;

public enum ProjectilePathKind
{
    Straight = 0,
    Arc,
    Missile,
}

public enum ProjectilePathOutcome
{
    Unavailable = 0,
    Clear,
    Blocked,
    InvalidTarget,
    BudgetExceeded,
    Error,
}

public readonly record struct ProjectileTraceSample(
    Vector3 WorldPosition,
    bool IsClear,
    float Radius);

public readonly record struct ProjectilePathVerdict(
    ProjectilePathOutcome Status,
    int CollisionChecks = 0,
    uint BlockingObjectId = 0u,
    string? Notice = null)
{
    public bool IsClear => Status == ProjectilePathOutcome.Clear;

    public IReadOnlyList<ProjectileTraceSample> DiagSpecimens { get; init; } =
        Array.Empty<ProjectileTraceSample>();
}

public interface IProjectileControls
{
    bool IsAvailable => false;

    ProjectilePathVerdict EvaluatePath(
        uint markObjectIdent,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float missileRadius,
        float hopGap,
        int ceilingImpactChecks) => new(ProjectilePathOutcome.Unavailable);

    ProjectilePathVerdict EvaluatePathWithDiagnostics(
        uint markObjectIdent,
        ProjectilePathKind sort,
        StrikeHeight markHeight,
        float missileRadius,
        float hopGap,
        int ceilingImpactChecks)
    {
        return EvaluatePath(
            markObjectIdent,
            sort,
            markHeight,
            missileRadius,
            hopGap,
            ceilingImpactChecks);
    }

    void ShowDebugSamples(IReadOnlyList<ProjectileTraceSample> specimens)
    {
    }
}
