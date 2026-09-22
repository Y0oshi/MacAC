namespace MacAC.Extensibility.Automation;

public readonly record struct NavigationFix(
    uint CellId,
    double EastWest,
    double NorthSouth,
    double Elevation,
    float HeadingDegrees,
    bool IsOutdoor)
{
    private const double MetresPerLookupUnit = 240d;

    public double HorizontalGapMeters(in NavigationFix another)
    {
        double east = EastWest - another.EastWest;
        double north = NorthSouth - another.NorthSouth;
        return Math.Sqrt(east * east + north * north) * MetresPerLookupUnit;
    }
}

public readonly record struct NavigationEntry(
    uint ObjectId,
    string Name,
    NavigationFix Position)
{
    public bool IsDoor { get; init; }

    public bool IsOpen { get; init; }

    public bool IsLocked { get; init; }

    public bool HasLockPhase { get; init; }

    public int LockDifficulty { get; init; }
}

/// <summary>Local movement state, sampled atomically once per extension tick.</summary>
public readonly record struct NavigationFrame(
    bool IsAvailable,
    bool IsPortalSpace,
    uint LocalObjectId,
    NavigationFix Position,
    bool IsMoving,
    bool IsAirborne)
{
    public NavigationFix ConfirmedLocus { get; init; }

    public ulong ConfirmedLocusRev { get; init; }
}

public readonly record struct MovementIntent(
    bool Forward = false,
    bool Backward = false,
    bool StrafeLeft = false,
    bool StrafeRight = false,
    bool TurnLeft = false,
    bool TurnRight = false,
    bool Run = true,
    bool Jump = false);

public enum NavigationOutcome
{
    Unavailable = 0,
    Accepted,
    Rejected,
}

public interface INavigationControls
{
    NavigationFrame Snapshot { get; }

    bool TryFetchObject(uint objectIdent, out NavigationEntry val);

    bool TrySeekObject(
        string label,
        in NavigationFix nearby,
        double ceilingGapMeters,
        out NavigationEntry val)
    {
        val = default;
        return false;
    }

    IReadOnlyList<NavigationEntry> CaptureObjects() => Array.Empty<NavigationEntry>();

    NavigationOutcome AssignTravelIntent(in MovementIntent intent);

    NavigationOutcome WipeTravelIntent();

    NavigationOutcome FaceHeading(float bearingDeg) => NavigationOutcome.Unavailable;
}
