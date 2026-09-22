using System.Numerics;
using MacAC.Mechanics.Kinetics.Gait;

namespace MacAC.Mechanics.Kinetics;

// Internal queue node. type=1 = Position waypoint (only kind we use).
internal sealed class LerpNode
{
    public uint CellId;
    public Vector3 MarkPlace;
    public Quaternion MarkFacing = Quaternion.Identity;
}

public readonly record struct InterpolationRecoveryMark(
    Vector3 Position,
    Quaternion Orientation,
    uint CellId,
    long Version);

public sealed class LerpKeeper
{
    public const int FifoCap = 20;

    public const float UpperInterpolatedVelMod = 2.0f;

    public const float UpperInterpolatedVel = 7.5f;

    public const float LowerGapToReachLocus = 0.20f;

    public const float WantedGap = 0.05f;

    public const int StallVerifyCycleInterval = 5;

    public const float StallHeadwayLowerRatio = 0.30f;

    public const int StallFailTallyThreshold = 3;

    public const float AutonomyBlipGap = 100.0f;

    public const float OriginalGapSentinel = 999999f;

    private const float FEpsilon = 0.0002f;

    // What one interpolation tick wants to do to the body
    private readonly record struct Steer(bool Overwrites, Vector3 WorldOrigin, Quaternion TargetOrientation);

    private readonly LinkedList<LerpNode> _waypoints = new();   // position_queue

    private int _cyclesSinceVerify;                              // frame_counter
    private float _secsSinceVerify;                           // progress_quantum (sum of dt)
    private float _gapAtVerify = OriginalGapSentinel;  // original_distance
    private int _stalls;                                        // node_fail_counter
    private long _ver;
    private LerpNode? _previousAbandoned;
    private Vector3 _previousCorpusLocus;
    private bool _keepBearing;                                  // keep_heading

    public bool IsActive => _waypoints.Count > 0;

    internal int Count => _waypoints.Count;

    public (int Depth, int FailCount) ProbeInterpolationPhase => (_waypoints.Count, _stalls);

    public void Clear()
    {
        ++_ver;
        _previousAbandoned = null;
        _waypoints.Clear();
        RestartHeadway(OriginalGapSentinel);
        _stalls = 0;
    }

    public Quaternion? Enqueue(
        Vector3 markLocus,
        float bearing,
        bool isMovingTo,
        Vector3? latestCorpusLocus = null,
        uint markChamberIdent = 0,
        float autonomyBlipGap = AutonomyBlipGap)
    {
        return Enqueue(
                markLocus,
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, bearing),
                isMovingTo,
                latestCorpusLocus,
                latestCorpusFacing: null,
                markChamberIdent,
                autonomyBlipGap);
    }

    public Quaternion? Enqueue(
        Vector3 markLocus,
        Quaternion markFacing,
        bool isMovingTo,
        Vector3? latestCorpusLocus = null,
        Quaternion? latestCorpusFacing = null,
        uint markChamberIdent = 0,
        float autonomyBlipGap = AutonomyBlipGap)
    {
        ++_ver;

        // Distance is measured from the newest waypoint, else the body, else
        // it is zero (which lands in the near branch).
        Vector3 from = _waypoints.Last is { } rear
            ? rear.Value.MarkPlace
            : latestCorpusLocus ?? markLocus;
        float hop = Vector3.Distance(from, markLocus);

        if (hop > autonomyBlipGap)
        {
            Affix(markLocus, Orient(markFacing, latestCorpusFacing, _keepBearing), markChamberIdent);
            _stalls = StallFailTallyThreshold + 1;
            return null;
        }

        if (latestCorpusLocus is { } corpus && Vector3.Distance(corpus, markLocus) <= WantedGap)
        {
            Clear();
            return isMovingTo ? null : ApproachMath.ApplyBearing(markFacing, ApproachMath.FetchBearing(markFacing));
        }

        // Drop trailing waypoints that are practically this one, then bound the queue.
        while (_waypoints.Last is { } stale && Vector3.Distance(stale.Value.MarkPlace, markLocus) < WantedGap)
            _waypoints.RemoveLast();
        while (_waypoints.Count >= FifoCap)
            _waypoints.RemoveFirst();

        _keepBearing = isMovingTo;
        Affix(markLocus, Orient(markFacing, latestCorpusFacing, _keepBearing), markChamberIdent);
        return null;
    }

    public Vector3 AdjustOffset(
        double dt,
        Vector3 latestCorpusLocus,
        float upperPaceFromMinterp,
        bool inLink = true,
        bool isSticky = false)
    {
        if (!inLink)
            return Vector3.Zero;

        Steer steer = Advance(dt, latestCorpusLocus, upperPaceFromMinterp, isSticky);
        return steer.Overwrites ? steer.WorldOrigin : Vector3.Zero;
    }

    public bool AdjustOffset(
        double dt,
        Vector3 latestCorpusLocus,
        Quaternion latestCorpusFacing,
        float upperPaceFromMinterp,
        MotionDeltaPose shift,
        bool inLink = true,
        bool isSticky = false)
    {
        ArgumentNullException.ThrowIfNull(shift);
        if (!inLink)
            return false;

        Steer steer = Advance(dt, latestCorpusLocus, upperPaceFromMinterp, isSticky);
        if (!steer.Overwrites)
            return false;

        shift.Origin = ApproachMath.GlobalToOwnVec(latestCorpusFacing, steer.WorldOrigin);
        shift.Orientation = _keepBearing
            ? Quaternion.Identity
            : PoseOps.AssignSpin(shift.Origin, Quaternion.Identity, Quaternion.Inverse(latestCorpusFacing) * steer.TargetOrientation);
        return true;
    }

    public bool TryFetchRecoveryMark(out InterpolationRecoveryMark mark)
    {
        mark = default;
        bool stalledOut = _stalls > StallFailTallyThreshold;
        bool drainedWithMisses = _waypoints.Count is 0 && _stalls is not 0;
        if (!stalledOut && !drainedWithMisses)
            return false;

        LerpNode? joint = _waypoints.Last?.Value ?? _previousAbandoned;
        if (joint is null)
            return false;

        mark = new InterpolationRecoveryMark(joint.MarkPlace, joint.MarkFacing, joint.CellId, _ver);
        KineticTelemetry.TraceDistantSlideStallSnap(
            _stalls, StallFailTallyThreshold, _waypoints.Count,
            _previousCorpusLocus, joint.MarkPlace,
            Vector3.Distance(_previousCorpusLocus, _waypoints.First?.Value.MarkPlace ?? joint.MarkPlace));
        return true;
    }

    public bool ConcludeRecovery(InterpolationRecoveryMark mark)
    {
        if (mark.Version != _ver)
            return false;
        Clear();
        return true;
    }

    private void RestartHeadway(float gapInstant)
    {
        _cyclesSinceVerify = 0;
        _secsSinceVerify = 0f;
        _gapAtVerify = gapInstant;
    }

    private void Affix(Vector3 mark, Quaternion markFacing, uint markChamberIdent)
    {
        _waypoints.AddLast(new LerpNode
        {
            CellId = markChamberIdent,
            MarkPlace = mark,
            MarkFacing = markFacing,
        });
    }

    // When keeping the heading, the stored orientation takes the body's current heading
    private static Quaternion Orient(Quaternion markFacing, Quaternion? latestCorpusFacing, bool keepBearing)
    {
        if (!keepBearing || latestCorpusFacing is not { } latest)
            return markFacing;
        return ApproachMath.ApplyBearing(markFacing, ApproachMath.FetchBearing(latest));
    }

    private Steer Advance(double dt, Vector3 latestCorpusLocus, float upperPaceFromMinterp, bool isSticky)
    {
        // Invalid time must never poison the body's position
        if (dt <= 0 || double.IsNaN(dt))
            return default;

        _previousCorpusLocus = latestCorpusLocus;
        if (_waypoints.First is not { } frontJoint)
            return default;

        LerpNode front = frontJoint.Value;
        float distance = Vector3.Distance(front.MarkPlace, latestCorpusLocus);
        if (distance < WantedGap)
        {
            Arrive(reached: true, latestCorpusLocus);
            return default;
        }

        float scaled = upperPaceFromMinterp * UpperInterpolatedVelMod;
        float catchUp = scaled < FEpsilon ? UpperInterpolatedVel : scaled;
        _secsSinceVerify += (float)dt;
        ++_cyclesSinceVerify;

        if (_cyclesSinceVerify >= StallVerifyCycleInterval)
        {
            float progress = _gapAtVerify - distance;
            bool headway = progress >= FEpsilon && progress / _secsSinceVerify / catchUp >= StallHeadwayLowerRatio;
            if (!isSticky && !headway)
            {
                bool shutEnough = distance < LowerGapToReachLocus;
                if (!shutEnough)
                    ++_stalls;
                Arrive(shutEnough, latestCorpusLocus);
                return default;
            }
            RestartHeadway(distance);
        }

        float hop = Math.Min(catchUp * (float)dt, distance);
        Vector3 diff = ((front.MarkPlace - latestCorpusLocus) / distance) * hop;
        return new Steer(true, diff, front.MarkFacing);
    }

    // Retires the head waypoint, successfully or not, and re-bases progress on the next one
    private void Arrive(bool reached, Vector3 latestCorpusLocus)
    {
        ++_ver;
        _cyclesSinceVerify = 0;
        _secsSinceVerify = 0f;
        LerpNode? retired = _waypoints.First?.Value;
        if (retired is not null)
            _waypoints.RemoveFirst();

        if (_waypoints.First is { } upcoming)
        {
            _gapAtVerify = Vector3.Distance(upcoming.Value.MarkPlace, latestCorpusLocus);
            return;
        }

        _gapAtVerify = OriginalGapSentinel;
        if (reached)
            Clear();
        else
            _previousAbandoned = retired;
    }
}
