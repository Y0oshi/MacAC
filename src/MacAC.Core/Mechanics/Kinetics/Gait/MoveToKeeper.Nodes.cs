using System.Numerics;

namespace MacAC.Mechanics.Kinetics.Gait;

/// <summary>The approach-node queue: starting each node and driving it per tick.</summary>
public sealed partial class MoveToKeeper
{
    public void AppendPivotToBearingJoint(float bearing) => _plan.AddLast(new ApproachNode(TravelKind.TurnToHeading, bearing));

    public void AppendRelocateToLocusJoint() => _plan.AddLast(new ApproachNode(TravelKind.MoveToPosition, 0f));

    public void DropQueuedActsFront()
    {
        if (_plan.First is not null)
            _plan.RemoveFirst();
    }

    public void CommenceUpcomingJoint()
    {
        if (_plan.First is { } front)
        {
            switch (front.Value.Type)
            {
                case TravelKind.MoveToPosition:
                    CommenceRelocateAhead();
                    break;
                case TravelKind.TurnToHeading:
                    CommencePivotToBearing();
                    break;
                    // unknown node type: stall (defensive)
            }
            return;
        }

        // Plan exhausted: optionally glue to the object, then report done.
        (uint stickTo, float stickRadius, float stickHeight) = (TopTierObjectIdent, SoughtObjectRadius, SoughtObjectHeight);
        bool sticky = Parameters.Sticky;

        CleanUp();
        if (HasKineticsObjRef)
            _hub.StopCompletely();
        if (sticky)
            StickTo?.Invoke(stickTo, stickRadius, stickHeight);
        RelocateToDone?.Invoke(WeenieProblem.None);
    }

    public void CommenceRelocateAhead()
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }

        float distance = FetchLatestGap();
        float bearing = PivotNeeded(BearingTo(LatestMarkLocus.Frame.Origin));
        Parameters.FetchDirective(distance, bearing, out uint cmd, out HeldKey gripTag, out bool movingAway);
        if (cmd is 0)
        {
            UpcomingJoint();
            return;
        }

        LocomotionParams own = new LocomotionParams
        {
            GripTagToEnact = gripTag,
            CancelMoveTo = false,
            Speed = Parameters.Speed,
        };
        WeenieProblem error = DoLocomotion(cmd, own);
        if (error != WeenieProblem.None)
        {
            CancelMoveTo(error);
            return;
        }

        LatestDirective = cmd;
        MovingAway = movingAway;
        Parameters.GripTagToEnact = gripTag;
        EarlierGap = distance;
        EarlierGapMoment = _hub.Now();
        OriginalGap = distance;
        OriginalGapMoment = _hub.Now();
    }

    public void CommencePivotToBearing()
    {
        if (_plan.First is not { } front || !HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }
        if (_lerp.MotionsQueued())
            return;

        float diff = ApproachMath.BearingDiff(front.Value.Heading, _hub.Heading(), LocomotionDirective.TurnRight);

        // Past 180 the shorter way round is left; within epsilon of a full
        // circle (or of zero) there is nothing to turn.
        bool left = diff > 180f;
        bool alreadyFacing = left ? diff + ApproachMath.Epsilon >= 360f : diff <= ApproachMath.Epsilon;
        if (alreadyFacing)
        {
            UpcomingJoint();
            return;
        }

        uint pivot = left ? LocomotionDirective.PivotLeft : LocomotionDirective.TurnRight;
        WeenieProblem error = DoLocomotion(pivot, OwnParameters());
        if (error != WeenieProblem.None)
        {
            CancelMoveTo(error);
            return;
        }

        LatestDirective = pivot;
        EarlierBearing = diff;
    }

    public float FetchLatestGap()
    {
        if (!HasKineticsObjRef)
            return 0f;

        Vector3 me = Here;
        Vector3 mark = LatestMarkLocus.Frame.Origin;
        if (!Parameters.UseSpheres)
            return Vector3.Distance(me, mark);

        return ApproachMath.CylinderGap(
            _hub.Radius(), _hub.Height(), me,
            SoughtObjectRadius, SoughtObjectHeight, mark);
    }

    public bool VerifyHeadwayMade(float latestGap)
    {
        double passed = _hub.Now() - EarlierGapMoment;
        if (passed <= 1.0)
            return true;

        float recent = MovingAway ? latestGap - EarlierGap : EarlierGap - latestGap;
        if (recent / (float)passed < HeadwayRatePerSecond)
            return false;

        EarlierGap = latestGap;
        EarlierGapMoment = _hub.Now();

        float overall = MovingAway ? latestGap - OriginalGap : OriginalGap - latestGap;
        float overallRate = overall / (float)(_hub.Now() - OriginalGapMoment);
        return overallRate >= HeadwayRatePerSecond;
    }

    public void EmployTime()
    {
        if (!HasKineticsObjRef || !_hub.Contact())
            return;
        if (_plan.First is not { } front)
            return;

        // An object move waits for the first target update before it drives.
        bool loaded = TopTierObjectIdent is 0 || TravelKindPhase == TravelKind.Invalid || Initialized;
        if (!loaded)
            return;

        switch (front.Value.Type)
        {
            case TravelKind.MoveToPosition:
                ProcessRelocateToLocus();
                break;
            case TravelKind.TurnToHeading:
                ProcessPivotToBearing();
                break;
        }
    }

    public void ProcessRelocateToLocus()
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }

        Vector3 me = Here;
        var own = OwnParameters();

        if (_lerp.MotionsQueued())
        {
            DiscardAuxPivot(own);
        }
        else
        {
            NavigateTowardMark(me, own);
        }

        float distance = FetchLatestGap();
        if (VerifyHeadwayMade(distance))
        {
            FailHeadwayTally = 0;
            bool arrived = MovingAway ? distance >= Parameters.LowerGap : distance <= Parameters.GapToObject;
            if (arrived)
            {
                DropQueuedActsFront();
                HaltLocomotion(LatestDirective, own);
                LatestDirective = 0;
                DiscardAuxPivot(own);
                CommenceUpcomingJoint();
            }
            else if (Vector3.Distance(StartingLocus.Frame.Origin, Here) > Parameters.FailGap)
            {
                CancelMoveTo(WeenieProblem.YouChargedTooFar);
            }
        }
        else
        {
            if (!_hub.IsInterpolating() && !_lerp.MotionsQueued())
                FailHeadwayTally += 1;
        }

        RetuneMarkQuantum(distance);
    }

    public void ProcessPivotToBearing()
    {
        if (!HasKineticsObjRef)
        {
            CancelMoveTo(WeenieProblem.NoPhysicsObject);
            return;
        }

        uint cmd = LatestDirective;
        if (cmd != LocomotionDirective.PivotLeft && cmd != LocomotionDirective.TurnRight)
        {
            CommencePivotToBearing();
            return;
        }

        var front = _plan.First!.Value;
        float bearing = _hub.Heading();

        if (ApproachMath.BearingGreater(bearing, front.Heading, cmd))
        {
            FailHeadwayTally = 0;
            _hub.SetHeading(front.Heading, true);
            DropQueuedActsFront();
            HaltLocomotion(LatestDirective, OwnParameters(withPace: false));
            LatestDirective = 0;
            CommenceUpcomingJoint();
            return;
        }

        float turned = ApproachMath.BearingDiff(bearing, EarlierBearing, cmd);
        EarlierBearing = bearing;
        if (turned < 180f && turned > ApproachMath.Epsilon)
        {
            FailHeadwayTally = 0;
            return;
        }

        if (!_hub.IsInterpolating() && !_lerp.MotionsQueued())
            FailHeadwayTally += 1;
    }

    // Retires the head node and starts whatever follows it
    private void UpcomingJoint()
    {
        DropQueuedActsFront();
        CommenceUpcomingJoint();
    }

    // Keeps an auxiliary turn running while the target is outside the 20° cone ahead
    private void NavigateTowardMark(Vector3 me, LocomotionParams own)
    {
        float wanted = Parameters.FetchWantedBearing(LatestDirective, MovingAway) + ApproachMath.PlaceBearing(me, LatestMarkLocus.Frame.Origin);
        if (wanted >= 360f)
            wanted -= 360f;

        float diff = PivotNeeded(wanted);
        if (diff <= ArrivalConeDeg || diff >= 360f - ArrivalConeDeg)
        {
            DiscardAuxPivot(own);
            return;
        }

        uint pivot = diff >= 180f ? LocomotionDirective.PivotLeft : LocomotionDirective.TurnRight;
        if (pivot != AuxDirective)
        {
            DoLocomotion(pivot, own);
            AuxDirective = pivot;
        }
    }

    // Asks the tracked object for updates about as often as we expect to reach it
    private void RetuneMarkQuantum(float distance)
    {
        if (TopTierObjectIdent is 0 || TravelKindPhase == TravelKind.Invalid)
            return;

        float pace = _hub.Velocity().Length();
        if (pace > 0.1)
        {
            float eta = distance / pace;
            if (MathF.Abs(eta - (float)_hub.TargetQuantum()) >= 1.0f)
                _hub.SetTargetQuantum(eta);
        }
    }
}
