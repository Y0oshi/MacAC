using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

public sealed class StickyKeeper(IKineticObjHost host)
{
    public const float StickyRadius = 0.3f;

    public const float StickyMoment = 1.0f;

    private const float TrailPaceFactor = 5.0f;

    private const float BackupTrailPace = 15.0f;

    private readonly IKineticObjHost _hub = host ?? throw new ArgumentNullException(nameof(host));

    public uint TargetId { get; private set; }

    public float MarkRadius { get; private set; }

    public Locus MarkLocus { get; private set; }

    public bool Initialized { get; private set; }

    public double StickyTimeoutMoment { get; private set; }

    public void UnStick()
    {
        if (TargetId is not 0)
            Release();
    }

    public void StickTo(uint objectIdent, float markRadius, float markHeight)
    {
        _ = markHeight;
        if (TargetId is not 0)
            Release();

        MarkRadius = markRadius;
        TargetId = objectIdent;
        Initialized = false;
        StickyTimeoutMoment = _hub.CurTime + StickyMoment;

        _hub.AssignMark(0, objectIdent, 0.5f, 0.5);
    }

    public void UseTime()
    {
        if (TargetId is not 0 && _hub.CurTime > StickyTimeoutMoment)
            Release();
    }

    public void ProcessUpdateTarget(TargetFacts details)
    {
        if (details.ObjectId != TargetId)
            return;

        if (details.Status == TargetPhase.Ok)
        {
            Initialized = true;
            MarkLocus = details.TargetPosition;
        }
        else if (TargetId is not 0)
        {
            Release();
        }
    }

    public void AdjustOffset(MotionDeltaPose shift, double quantum)
    {
        if (TargetId is 0 || !Initialized)
            return;

        Locus self = _hub.Position;
        Locus markSpot = _hub.FetchRelationshipMark(TargetId)?.Position ?? MarkLocus;

        Vector3 toward = ApproachMath.GlobalToOwnVec(self.Frame.Orientation, markSpot.Frame.Origin - self.Frame.Origin);
        toward.Z = 0f;
        shift.Origin = toward;

        float gap = ApproachMath.CylinderGapNoZ(_hub.Radius, self.Frame.Origin, MarkRadius, markSpot.Frame.Origin) - StickyRadius;

        if (ApproachMath.StandardizeVerifySmall(ref shift.Origin))
            shift.Origin = Vector3.Zero;

        float pace = _hub.MinterpMaxSpeed is { } upperPace ? upperPace * TrailPaceFactor : 0f;
        if (pace < ApproachMath.Epsilon)
            pace = BackupTrailPace;

        AdjustOffsetRest(self, markSpot, shift, gap, pace, quantum);
    }

    private void AdjustOffsetRest(Locus self, Locus markSpot, MotionDeltaPose shift, float gap, float pace, double quantum)
    {
        float hop = pace * (float)quantum;
        if (hop >= MathF.Abs(gap))
            hop = gap;
        else if (gap < 0f)
            hop = -hop;
        shift.Origin *= hop;
        float facing = ApproachMath.FetchBearing(self.Frame.Orientation);
        float wanted = ApproachMath.PlaceBearing(self.Frame.Origin, markSpot.Frame.Origin);
        AdjustOffsetTail(shift, facing, wanted);
    }

    private void AdjustOffsetTail(MotionDeltaPose shift, float facing, float wanted)
    {
        float pivot = wanted - facing;
        if (MathF.Abs(pivot) < ApproachMath.Epsilon)
            pivot = 0f;
        if (pivot < -ApproachMath.Epsilon)
            pivot += 360f;
        shift.AssignHeading(pivot);
    }

    // Drops the target and tells the host to stop whatever it was doing
    private void Release()
    {
        TargetId = 0;
        Initialized = false;
        _hub.WipeMark();
        _hub.InterruptCurrentMovement();
    }
}
